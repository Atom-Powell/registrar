# Governance Library Normalization Tool Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a PowerShell tool that inventories a locally synced ORG governance SharePoint library, proposes normalization of root-level files into a config-defined baseline, executes only what a human approves, and reports drift across the rest of the library.

**Architecture:** A PowerShell module (`scripts/GovLib`) of single-responsibility function files, driven by one entry script (`scripts/normalize-governance-library.ps1`) with six modes. The folder taxonomy lives as versioned data in `config/library-baseline.json`, never in code. The Registrar engine is not modified and gains no ability to write to the document library.

**Tech Stack:** PowerShell 7.x, Pester 6 (5.x-compatible syntax), CSV interchange, JSON Lines undo journal. No third-party modules. No .NET changes.

## Global Constraints

- PowerShell 7.0 minimum. Every script file starts with `#Requires -Version 7.0`.
- Target OS is Windows 11. Do not assume Linux paths.
- ASCII only in all source and config. No em dashes, en dashes, smart quotes, ellipsis characters, or emoji. `scripts/normalize-hygiene.ps1` is the fix if a literal glyph slips in.
- US English.
- No third-party PowerShell modules. Pester is the only test dependency and ships with the runner.
- The tool never deletes a file or folder in any mode. No delete code path may exist.
- The tool never writes to the ID Registry.
- Every function that changes the filesystem must be reachable only from `Invoke-GovApply` or `Invoke-GovUndo`.
- Interchange format is CSV, not xlsx. Writing xlsx would require the third-party `ImportExcel` module or Excel COM; neither is available in CI. Excel opens CSV natively. Naming the table `GovFiles` remains the human Excel-side step the SOP already assigns to Registry Owner.
- Design authority is `docs/superpowers/specs/2026-08-01-governance-library-normalization-design.md`. Where this plan and that spec disagree, the spec wins and the plan is wrong.

---

## File Structure

**Created:**

    config/library-baseline.json          folder taxonomy and rules as versioned data
    scripts/GovLib/GovLib.psd1            module manifest
    scripts/GovLib/GovLib.psm1            root module; dot-sources function files
    scripts/GovLib/Baseline.ps1           load and validate the baseline config
    scripts/GovLib/Paths.ps1              doNotTouch matching, containment, path length
    scripts/GovLib/Registry.ps1           ID Registry CSV reader
    scripts/GovLib/Identity.ps1           filename Document ID extraction and resolution
    scripts/GovLib/Inventory.ps1          local sweep and SharePoint export join
    scripts/GovLib/Plan.ps1               plan row construction and tagging
    scripts/GovLib/Journal.ps1            append-only undo journal
    scripts/GovLib/Apply.ps1              execution of approved rows
    scripts/GovLib/Undo.ps1               reverse run over a journal
    scripts/GovLib/Verify.ps1             post-run verification
    scripts/GovLib/Report.ps1             full-library read-only drift report
    scripts/normalize-governance-library.ps1   entry point, six modes
    tests/powershell/TestHelpers.psm1     synthetic tree builder
    tests/powershell/*.Tests.ps1          one test file per module file

**Modified:**

    .github/workflows/ci.yml              add a Pester step

Each `GovLib` file holds one responsibility and is independently testable. Files that change together live together: identity resolution needs both the registry and artifact-types, so `Identity.ps1` consumes `Registry.ps1` rather than duplicating parsing.

---

### Task 1: Module scaffold, baseline config, and CI wiring

Establishes the module, the baseline data, its validating loader, the test helper every later task uses, and the CI step so all subsequent tasks are covered from the start.

**Files:**
- Create: `config/library-baseline.json`
- Create: `scripts/GovLib/GovLib.psd1`
- Create: `scripts/GovLib/GovLib.psm1`
- Create: `scripts/GovLib/Baseline.ps1`
- Create: `tests/powershell/TestHelpers.psm1`
- Create: `tests/powershell/Baseline.Tests.ps1`
- Modify: `.github/workflows/ci.yml`

**Interfaces:**
- Consumes: nothing.
- Produces: `Import-GovBaseline -Path <string>` returning a PSCustomObject with properties `baselineVersion`, `nestingOrder`, `folderTree`, `placementRules`, `dispositions`, `fileNaming`, `identification`, `doNotTouch`. Throws a terminating error on any validation failure. Also produces `New-TestLibrary` and `New-TestBaseline` test helpers.

- [ ] **Step 1: Create the baseline config**

Create `config/library-baseline.json`:

```json
{
  "baselineVersion": "1.0",
  "sourceOfTruth": "Governor baseline 2026-08-01 plus follow-up 2026-08-01",
  "nestingOrder": "category-first",

  "folderTree": [
    { "path": "Controlled Documents", "createIfEmpty": true, "status": "governed",
      "ruleSource": "ADR-001 Repository Architecture" },
    { "path": "Controlled Documents/Governance", "createIfEmpty": true, "status": "governed",
      "ruleSource": "ADR-002 Governance Layer" },
    { "path": "Controlled Documents/Operational", "createIfEmpty": true, "status": "unconfirmed",
      "note": "repository-targets.json says Operational Artifacts. Unresolved owner decision.",
      "ruleSource": "ADR-002 Operational Layer" },
    { "path": "Controlled Documents/Training", "createIfEmpty": true, "status": "governed",
      "ruleSource": "ADR-002 Governance Layer" },
    { "path": "Controlled Documents/Risk", "createIfEmpty": true, "status": "governed",
      "ruleSource": "ADR-002 Operational Layer" },
    { "path": "Controlled Documents/Internal", "createIfEmpty": true, "status": "governed",
      "ruleSource": "ORG-ID-Registry.xlsx Section 5 category INT" },
    { "path": "Master Development Workspace", "createIfEmpty": true, "status": "governed",
      "ruleSource": "ADR-001 Repository Architecture" },
    { "path": "Architecture Decision Records", "createIfEmpty": true, "status": "governed",
      "ruleSource": "ADR-001 Repository Architecture" },
    { "path": "Document Registry", "createIfEmpty": true, "status": "governed",
      "ruleSource": "ADR-003 Document Registry equals Artifact Catalog" },
    { "path": "Templates", "createIfEmpty": true, "status": "governed",
      "ruleSource": "ADR-003 Templates equals Reusable Components" },
    { "path": "Templates/Approved", "createIfEmpty": true, "status": "governed",
      "ruleSource": "ORG-MST-SPEC-001 Section 15" },
    { "path": "Archive", "createIfEmpty": true, "status": "unconfirmed",
      "note": "Spec Section 15 says _Archive. Deployed library says Archive. Confirm empirically.",
      "ruleSource": "ORG-MST-SPEC-001 Section 15 vs observed" }
  ],

  "placementRules": {
    "decidingFieldsInPriorityOrder": ["approvalStatus", "idCategory"],
    "mappings": [
      { "match": { "approvalStatus": "Draft" },
        "targetPath": "Master Development Workspace",
        "status": "governed", "ruleSource": "ADR-001 development branch" },
      { "match": { "approvalStatus": "In Review" },
        "targetPath": "Master Development Workspace",
        "status": "governed", "ruleSource": "ADR-001 development branch" },
      { "match": { "approvalStatus": "Approved", "idCategory": "GOV" },
        "targetPath": "Controlled Documents/Governance",
        "status": "governed", "ruleSource": "ADR-002 Governance Layer" },
      { "match": { "approvalStatus": "Approved", "idCategory": "TRN" },
        "targetPath": "Controlled Documents/Training",
        "status": "governed", "ruleSource": "ADR-002 Governance Layer" },
      { "match": { "approvalStatus": "Approved", "idCategory": "RSK" },
        "targetPath": "Controlled Documents/Risk",
        "status": "governed", "ruleSource": "ADR-002 Operational Layer" },
      { "match": { "approvalStatus": "Approved", "idCategory": "OPS" },
        "targetPath": "Controlled Documents/Operational",
        "status": "unconfirmed", "ruleSource": "ADR-002 Operational Layer" },
      { "match": { "approvalStatus": "Approved", "idCategory": "INT" },
        "targetPath": "Controlled Documents/Internal",
        "status": "governed", "ruleSource": "ORG-ID-Registry.xlsx Section 5" },
      { "match": { "approvalStatus": "Approved", "idCategory": "MST" },
        "targetPath": "Templates/Approved",
        "status": "unconfirmed",
        "note": "ADR-001 and ADR-003 are in tension for MST. Unresolved owner decision.",
        "ruleSource": "ADR-003 Templates equals Reusable Components" },
      { "match": { "approvalStatus": "Superseded" },
        "targetPath": "Archive",
        "status": "unconfirmed", "ruleSource": "ORG-MST-SPEC-001 Section 15" },
      { "match": { "approvalStatus": "Retired" },
        "targetPath": null,
        "status": "notDefined", "decider": "ADR-001 successor",
        "ruleSource": "No controlled document names a Retired folder" }
    ]
  },

  "dispositions": {
    "retired":       { "action": "leaveInPlace", "status": "notDefined",
                       "decider": "ADR-001 successor" },
    "nonControlled": { "action": "leaveInPlace", "status": "notDefined",
                       "decider": "ADR or operational runbook" },
    "unrecognized":  { "action": "escalate", "status": "notDefined",
                       "decider": "Registry Owner" }
  },

  "fileNaming": {
    "status": "notDefined",
    "template": null,
    "decider": "ORG-MST-SPEC-001 documentation owner or a new ADR"
  },

  "identification": {
    "method": "none",
    "status": "blocked",
    "note": "No governed automated method. Identity is resolved by a human in the workbook.",
    "ruleSource": "Governor follow-up answer 7"
  },

  "doNotTouch": [
    { "pattern": "Architecture Decision Records/*",
      "reason": "Repository history. Reorganization destroys design rationale traceability.",
      "ruleSource": "ADR-003 Governor Agent Design" },
    { "pattern": "Document Registry/ORG-ID-Registry.xlsx",
      "reason": "Authoritative state. No automated process may modify it.",
      "ruleSource": "ORG-ID-Registry.xlsx Instructions Sections 1 and 2" },
    { "pattern": "Templates/Approved/*",
      "reason": "Controlled artifacts. Version changes require a governed registry transition.",
      "ruleSource": "ORG-MST-SPEC-001 Section 12" },
    { "pattern": "Document Registry/Blockers/*",
      "reason": "Active governance blocker records.",
      "ruleSource": "Observed in library" }
  ]
}
```

- [ ] **Step 2: Create the module manifest and root module**

Create `scripts/GovLib/GovLib.psd1`:

```powershell
@{
    RootModule        = 'GovLib.psm1'
    ModuleVersion     = '1.0.0'
    GUID              = 'b3f1c2d4-5a6e-4f70-8b91-2c3d4e5f6a7b'
    Author            = 'ORG Registry Owner'
    Description       = 'Governance library normalization helpers for The Registrar.'
    PowerShellVersion = '7.0'
    FunctionsToExport = @('Import-GovBaseline')
}
```

Create `scripts/GovLib/GovLib.psm1`:

```powershell
#Requires -Version 7.0
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

foreach ($file in Get-ChildItem -Path $PSScriptRoot -Filter '*.ps1' -File | Sort-Object Name) {
    . $file.FullName
}
```

- [ ] **Step 3: Write the failing tests for the baseline loader**

Create `tests/powershell/Baseline.Tests.ps1`:

```powershell
#Requires -Version 7.0
BeforeAll {
    $script:RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
    Import-Module (Join-Path $script:RepoRoot 'scripts/GovLib/GovLib.psd1') -Force
    Import-Module (Join-Path $PSScriptRoot 'TestHelpers.psm1') -Force
}

Describe 'Import-GovBaseline' {
    It 'loads the shipped baseline config' {
        $path = Join-Path $script:RepoRoot 'config/library-baseline.json'
        $b = Import-GovBaseline -Path $path
        $b.baselineVersion | Should -Be '1.0'
        $b.folderTree.Count | Should -BeGreaterThan 0
        $b.identification.status | Should -Be 'blocked'
    }

    It 'throws when the file does not exist' {
        { Import-GovBaseline -Path 'C:\nope\missing.json' } | Should -Throw '*not found*'
    }

    It 'throws when a rule carries an unknown status value' {
        $tmp = New-TestBaseline -OverrideJson '{ "baselineVersion": "1.0", "nestingOrder": "category-first", "folderTree": [ { "path": "X", "createIfEmpty": true, "status": "banana", "ruleSource": "t" } ], "placementRules": { "decidingFieldsInPriorityOrder": [], "mappings": [] }, "dispositions": {}, "fileNaming": { "status": "notDefined" }, "identification": { "status": "blocked" }, "doNotTouch": [] }'
        { Import-GovBaseline -Path $tmp } | Should -Throw '*banana*'
    }

    It 'throws naming the first required top-level section that is missing' {
        # Sections are checked in declaration order, so nestingOrder is named first.
        $tmp = New-TestBaseline -OverrideJson '{ "baselineVersion": "1.0" }'
        { Import-GovBaseline -Path $tmp } | Should -Throw '*nestingOrder*'
    }

    It 'throws when folderTree specifically is missing' {
        $tmp = New-TestBaseline -OverrideJson '{ "baselineVersion": "1.0", "nestingOrder": "category-first" }'
        { Import-GovBaseline -Path $tmp } | Should -Throw '*folderTree*'
    }
}

Describe 'Repository hygiene' {
    It 'has no literal non-ASCII characters in GovLib or the baseline config' {
        $files = @(Get-ChildItem (Join-Path $script:RepoRoot 'scripts/GovLib') -File) +
                 @(Get-Item (Join-Path $script:RepoRoot 'config/library-baseline.json'))
        foreach ($f in $files) {
            $hits = Select-String -Path $f.FullName -Pattern '[^\x00-\x7F]' -AllMatches
            $hits | Should -BeNullOrEmpty -Because "$($f.Name) must be ASCII only"
        }
    }
}
```

- [ ] **Step 4: Create the test helper module**

Create `tests/powershell/TestHelpers.psm1`:

```powershell
#Requires -Version 7.0
Set-StrictMode -Version Latest

function New-TestRoot {
    $path = Join-Path ([System.IO.Path]::GetTempPath()) ("govlib-" + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $path -Force | Out-Null
    return $path
}

function New-TestBaseline {
    param([string] $OverrideJson)
    $root = New-TestRoot
    $path = Join-Path $root 'library-baseline.json'
    Set-Content -LiteralPath $path -Value $OverrideJson -Encoding utf8NoBOM
    return $path
}

function New-TestLibrary {
    <#
      Builds a synthetic library. Files is a hashtable of relative path to content.
      Returns the library root path.
    #>
    param([hashtable] $Files = @{})
    $root = New-TestRoot
    foreach ($rel in $Files.Keys) {
        $full = Join-Path $root $rel
        $dir = Split-Path -Parent $full
        if (-not (Test-Path -LiteralPath $dir)) {
            New-Item -ItemType Directory -Path $dir -Force | Out-Null
        }
        Set-Content -LiteralPath $full -Value $Files[$rel] -Encoding utf8NoBOM
    }
    return $root
}

function Get-TreeSnapshot {
    <# Relative path plus length for every file, sorted. Used to prove read-only behavior. #>
    param([string] $Root)
    Get-ChildItem -LiteralPath $Root -Recurse -File |
        ForEach-Object { '{0}|{1}' -f $_.FullName.Substring($Root.Length).TrimStart('\'), $_.Length } |
        Sort-Object
}

Export-ModuleMember -Function New-TestRoot, New-TestBaseline, New-TestLibrary, Get-TreeSnapshot
```

- [ ] **Step 5: Run the tests to verify they fail**

Run: `pwsh -NoProfile -Command "Invoke-Pester -Path tests/powershell/Baseline.Tests.ps1 -Output Detailed"`

Expected: FAIL. `Import-GovBaseline` is not defined.

- [ ] **Step 6: Implement the baseline loader**

Create `scripts/GovLib/Baseline.ps1`:

```powershell
#Requires -Version 7.0

$script:GovValidStatus = @('governed', 'unconfirmed', 'notDefined', 'blocked')

function Import-GovBaseline {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [string] $Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Baseline config not found: $Path"
    }

    try {
        $baseline = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    }
    catch {
        throw "Baseline config is not valid JSON: $Path. $($_.Exception.Message)"
    }

    foreach ($section in @('baselineVersion','nestingOrder','folderTree','placementRules',
                           'dispositions','fileNaming','identification','doNotTouch')) {
        if (-not $baseline.PSObject.Properties.Name.Contains($section)) {
            throw "Baseline config is missing required section '$section': $Path"
        }
    }

    foreach ($entry in $baseline.folderTree) {
        Assert-GovStatus -Value $entry.status -Context "folderTree entry '$($entry.path)'"
    }
    foreach ($mapping in $baseline.placementRules.mappings) {
        Assert-GovStatus -Value $mapping.status -Context "placementRules mapping"
    }
    Assert-GovStatus -Value $baseline.fileNaming.status -Context 'fileNaming'
    Assert-GovStatus -Value $baseline.identification.status -Context 'identification'

    return $baseline
}

function Assert-GovStatus {
    param([string] $Value, [string] $Context)
    if ($Value -notin $script:GovValidStatus) {
        throw "Unknown status '$Value' in $Context. Valid values: $($script:GovValidStatus -join ', ')."
    }
}
```

Update `FunctionsToExport` in `GovLib.psd1` to `@('Import-GovBaseline')` (already set in Step 2).

- [ ] **Step 7: Run the tests to verify they pass**

Run: `pwsh -NoProfile -Command "Invoke-Pester -Path tests/powershell/Baseline.Tests.ps1 -Output Detailed"`

Expected: PASS, 5 tests.

- [ ] **Step 8: Wire Pester into CI**

In `.github/workflows/ci.yml`, add this step after the existing `dotnet test` step:

```yaml
      - name: Pester
        shell: pwsh
        run: |
          $c = New-PesterConfiguration
          $c.Run.Path = 'tests/powershell'
          $c.Run.Exit = $true
          $c.Output.Verbosity = 'Detailed'
          Invoke-Pester -Configuration $c
```

- [ ] **Step 9: Commit**

```bash
git add config/library-baseline.json scripts/GovLib tests/powershell .github/workflows/ci.yml
git commit -m "feat: GovLib module scaffold, baseline config, and Pester CI step"
```

---

### Task 2: Path utilities

Protection matching, root containment, and path length limits. Every later task depends on these, and they are the enforcement point for invariant 3.

**Files:**
- Create: `scripts/GovLib/Paths.ps1`
- Create: `tests/powershell/Paths.Tests.ps1`
- Modify: `scripts/GovLib/GovLib.psd1`

**Interfaces:**
- Consumes: `Import-GovBaseline` from Task 1.
- Produces:
  - `Test-GovProtectedPath -RelativePath <string> -Baseline <object>` returns `[bool]`.
  - `Get-GovProtectionReason -RelativePath <string> -Baseline <object>` returns `[string]` or `$null`.
  - `Test-GovPathUnderRoot -Path <string> -Root <string>` returns `[bool]`.
  - `Test-GovPathLength -Path <string> [-MaxLength <int>]` returns `[bool]`. Default MaxLength 255.

- [ ] **Step 1: Write the failing tests**

Create `tests/powershell/Paths.Tests.ps1`:

```powershell
#Requires -Version 7.0
BeforeAll {
    $script:RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
    Import-Module (Join-Path $script:RepoRoot 'scripts/GovLib/GovLib.psd1') -Force
    $script:Baseline = Import-GovBaseline -Path (Join-Path $script:RepoRoot 'config/library-baseline.json')
}

Describe 'Test-GovProtectedPath' {
    It 'protects files under a wildcard pattern' {
        Test-GovProtectedPath -RelativePath 'Architecture Decision Records/ADR-001.docx' -Baseline $script:Baseline |
            Should -BeTrue
    }
    It 'protects nested files under a wildcard pattern' {
        Test-GovProtectedPath -RelativePath 'Templates/Approved/sub/thing.docx' -Baseline $script:Baseline |
            Should -BeTrue
    }
    It 'protects an exact file pattern' {
        Test-GovProtectedPath -RelativePath 'Document Registry/ORG-ID-Registry.xlsx' -Baseline $script:Baseline |
            Should -BeTrue
    }
    It 'does not protect an unrelated file' {
        Test-GovProtectedPath -RelativePath 'stray-file.docx' -Baseline $script:Baseline |
            Should -BeFalse
    }
    It 'matches case-insensitively' {
        Test-GovProtectedPath -RelativePath 'architecture decision records/x.docx' -Baseline $script:Baseline |
            Should -BeTrue
    }
    It 'treats forward and back slashes as equivalent' {
        Test-GovProtectedPath -RelativePath 'Architecture Decision Records\ADR-001.docx' -Baseline $script:Baseline |
            Should -BeTrue
    }
}

Describe 'Get-GovProtectionReason' {
    It 'returns the configured reason for a protected path' {
        Get-GovProtectionReason -RelativePath 'Document Registry/ORG-ID-Registry.xlsx' -Baseline $script:Baseline |
            Should -BeLike '*Authoritative state*'
    }
    It 'returns null for an unprotected path' {
        Get-GovProtectionReason -RelativePath 'stray.docx' -Baseline $script:Baseline | Should -BeNullOrEmpty
    }
}

Describe 'Test-GovPathUnderRoot' {
    It 'returns true for a path inside the root' {
        Test-GovPathUnderRoot -Path 'C:\lib\sub\file.txt' -Root 'C:\lib' | Should -BeTrue
    }
    It 'returns false for a sibling directory with a shared prefix' {
        Test-GovPathUnderRoot -Path 'C:\library-other\file.txt' -Root 'C:\lib' | Should -BeFalse
    }
    It 'returns false for a path outside the root' {
        Test-GovPathUnderRoot -Path 'C:\elsewhere\file.txt' -Root 'C:\lib' | Should -BeFalse
    }
}

Describe 'Test-GovPathLength' {
    It 'accepts a short path' {
        Test-GovPathLength -Path 'C:\lib\a.docx' | Should -BeTrue
    }
    It 'rejects a path over the limit' {
        $long = 'C:\lib\' + ('a' * 300) + '.docx'
        Test-GovPathLength -Path $long | Should -BeFalse
    }
    It 'honors an explicit limit' {
        Test-GovPathLength -Path 'C:\lib\abcdef.docx' -MaxLength 5 | Should -BeFalse
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `pwsh -NoProfile -Command "Invoke-Pester -Path tests/powershell/Paths.Tests.ps1 -Output Detailed"`

Expected: FAIL. Functions not defined.

- [ ] **Step 3: Implement the path utilities**

Create `scripts/GovLib/Paths.ps1`:

```powershell
#Requires -Version 7.0

function ConvertTo-GovNormalPath {
    param([string] $Path)
    return ($Path -replace '\\', '/').TrimStart('/')
}

function Get-GovProtectionReason {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $RelativePath,
        [Parameter(Mandatory)] [object] $Baseline
    )
    $normal = ConvertTo-GovNormalPath $RelativePath
    foreach ($rule in $Baseline.doNotTouch) {
        $pattern = ConvertTo-GovNormalPath $rule.pattern
        if ($normal -like $pattern) { return $rule.reason }
        # A trailing /* must also protect the folder itself.
        if ($pattern.EndsWith('/*') -and $normal -eq $pattern.Substring(0, $pattern.Length - 2)) {
            return $rule.reason
        }
    }
    return $null
}

function Test-GovProtectedPath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $RelativePath,
        [Parameter(Mandatory)] [object] $Baseline
    )
    return $null -ne (Get-GovProtectionReason -RelativePath $RelativePath -Baseline $Baseline)
}

function Test-GovPathUnderRoot {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $Root
    )
    $p = [System.IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
    $r = [System.IO.Path]::GetFullPath($Root).TrimEnd('\', '/')
    if ($p -eq $r) { return $true }
    return $p.StartsWith($r + [System.IO.Path]::DirectorySeparatorChar,
                         [System.StringComparison]::OrdinalIgnoreCase)
}

function Test-GovPathLength {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $Path,
        [int] $MaxLength = 255
    )
    return $Path.Length -le $MaxLength
}
```

Note: PowerShell's `-like` is case-insensitive by default, which satisfies the case-insensitivity test.

- [ ] **Step 4: Export the new functions**

In `scripts/GovLib/GovLib.psd1`, set:

```powershell
    FunctionsToExport = @(
        'Import-GovBaseline',
        'Test-GovProtectedPath', 'Get-GovProtectionReason',
        'Test-GovPathUnderRoot', 'Test-GovPathLength'
    )
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `pwsh -NoProfile -Command "Invoke-Pester -Path tests/powershell/Paths.Tests.ps1 -Output Detailed"`

Expected: PASS, 14 tests.

- [ ] **Step 6: Commit**

```bash
git add scripts/GovLib/Paths.ps1 scripts/GovLib/GovLib.psd1 tests/powershell/Paths.Tests.ps1
git commit -m "feat: path protection, containment, and length utilities"
```

---

### Task 3: ID Registry reader

Reads the CSV export of the ID Registry. Its failure set deliberately mirrors `src/Registrar.Validation/CsvIdRegistry.cs` so the tool and the engine reject the same bad exports.

**Files:**
- Create: `scripts/GovLib/Registry.ps1`
- Create: `tests/powershell/Registry.Tests.ps1`
- Modify: `scripts/GovLib/GovLib.psd1`

**Interfaces:**
- Consumes: nothing.
- Produces: `Import-GovRegistry -Path <string>` returning a `[hashtable]` keyed by Document ID. Each value is a PSCustomObject with `DocumentId`, `ArtifactType`, `Classification`, `ApprovalStatus`, `Version`. Throws on empty file, missing required column, blank Document ID, or duplicate Document ID.

- [ ] **Step 1: Write the failing tests**

Create `tests/powershell/Registry.Tests.ps1`:

```powershell
#Requires -Version 7.0
BeforeAll {
    $script:RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
    Import-Module (Join-Path $script:RepoRoot 'scripts/GovLib/GovLib.psd1') -Force
    Import-Module (Join-Path $PSScriptRoot 'TestHelpers.psm1') -Force

    function New-RegistryCsv {
        param([string] $Content)
        $root = New-TestRoot
        $path = Join-Path $root 'registry.csv'
        Set-Content -LiteralPath $path -Value $Content -Encoding utf8NoBOM
        return $path
    }
}

Describe 'Import-GovRegistry' {
    It 'reads the repository fixture export' {
        $path = Join-Path $script:RepoRoot 'fixtures/registry-sample.csv'
        $reg = Import-GovRegistry -Path $path
        $reg['ORG-GOV-DS-016'].ApprovalStatus | Should -Be 'Draft'
        $reg['ORG-GOV-DS-017'].ApprovalStatus | Should -Be 'Retired'
        $reg['ORG-MST-SCL-001'].ArtifactType  | Should -Be 'SCL'
    }

    It 'throws on an empty file' {
        $p = New-RegistryCsv -Content ''
        { Import-GovRegistry -Path $p } | Should -Throw '*empty*'
    }

    It 'throws when a required column is missing' {
        $p = New-RegistryCsv -Content "documentId,artifactType,classification,version`nORG-GOV-DS-001,DS,Client-Facing,v1.0"
        { Import-GovRegistry -Path $p } | Should -Throw '*approvalStatus*'
    }

    It 'throws on a blank Document ID' {
        $p = New-RegistryCsv -Content "documentId,artifactType,classification,approvalStatus,version`n,DS,Client-Facing,Draft,v1.0"
        { Import-GovRegistry -Path $p } | Should -Throw '*blank Document ID*'
    }

    It 'throws on a duplicate Document ID' {
        $p = New-RegistryCsv -Content "documentId,artifactType,classification,approvalStatus,version`nORG-GOV-DS-001,DS,Client-Facing,Draft,v1.0`nORG-GOV-DS-001,DS,Client-Facing,Draft,v1.0"
        { Import-GovRegistry -Path $p } | Should -Throw '*duplicate*'
    }

    It 'matches column names case-insensitively' {
        $p = New-RegistryCsv -Content "DocumentID,ArtifactType,Classification,ApprovalStatus,Version`nORG-GOV-DS-001,DS,Client-Facing,Draft,v1.0"
        (Import-GovRegistry -Path $p)['ORG-GOV-DS-001'].Version | Should -Be 'v1.0'
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `pwsh -NoProfile -Command "Invoke-Pester -Path tests/powershell/Registry.Tests.ps1 -Output Detailed"`

Expected: FAIL. `Import-GovRegistry` is not defined.

- [ ] **Step 3: Implement the registry reader**

Create `scripts/GovLib/Registry.ps1`:

```powershell
#Requires -Version 7.0

$script:GovRegistryRequired = @('documentid','artifacttype','classification','approvalstatus','version')

function Import-GovRegistry {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [string] $Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "ID Registry export not found: $Path"
    }

    $rows = @(Import-Csv -LiteralPath $Path)
    if ($rows.Count -eq 0) {
        $raw = (Get-Content -LiteralPath $Path -Raw)
        if ([string]::IsNullOrWhiteSpace($raw)) {
            throw "ID Registry export is empty (no header row): $Path"
        }
    }

    $header = @()
    if ($rows.Count -gt 0) {
        $header = $rows[0].PSObject.Properties.Name
    }
    else {
        $header = (Get-Content -LiteralPath $Path -TotalCount 1) -split ','
    }
    $lookup = @{}
    foreach ($name in $header) { $lookup[$name.Trim().ToLowerInvariant()] = $name }

    foreach ($required in $script:GovRegistryRequired) {
        if (-not $lookup.ContainsKey($required)) {
            throw "ID Registry export is missing the required column '$required': $Path"
        }
    }

    $result = @{}
    foreach ($row in $rows) {
        $id = ([string]$row.($lookup['documentid'])).Trim()
        if ([string]::IsNullOrEmpty($id)) {
            throw "ID Registry export has a row with a blank Document ID: $Path"
        }
        if ($result.ContainsKey($id)) {
            throw "ID Registry export has a duplicate Document ID '$id': $Path"
        }
        $result[$id] = [pscustomobject]@{
            DocumentId     = $id
            ArtifactType   = ([string]$row.($lookup['artifacttype'])).Trim()
            Classification = ([string]$row.($lookup['classification'])).Trim()
            ApprovalStatus = ([string]$row.($lookup['approvalstatus'])).Trim()
            Version        = ([string]$row.($lookup['version'])).Trim()
        }
    }
    return $result
}
```

- [ ] **Step 4: Export the function**

Add `'Import-GovRegistry'` to `FunctionsToExport` in `scripts/GovLib/GovLib.psd1`.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `pwsh -NoProfile -Command "Invoke-Pester -Path tests/powershell/Registry.Tests.ps1 -Output Detailed"`

Expected: PASS, 6 tests.

- [ ] **Step 6: Commit**

```bash
git add scripts/GovLib/Registry.ps1 scripts/GovLib/GovLib.psd1 tests/powershell/Registry.Tests.ps1
git commit -m "feat: ID Registry CSV reader mirroring CsvIdRegistry failure set"
```

---

### Task 4: Identity resolution from filenames

The only point where the tool makes an identity claim. Reads Document IDs from filenames only, never from file content, and validates the category-to-artifact-code pairing against `config/artifact-types.json`.

**Files:**
- Create: `scripts/GovLib/Identity.ps1`
- Create: `tests/powershell/Identity.Tests.ps1`
- Modify: `scripts/GovLib/GovLib.psd1`

**Interfaces:**
- Consumes: `Import-GovRegistry` from Task 3.
- Produces:
  - `Import-GovArtifactTypes -Path <string>` returning a `[hashtable]` of artifact code (uppercase) to category.
  - `Get-GovDocumentIdFromName -Name <string>` returning the matched Document ID `[string]` or `$null`.
  - `Resolve-GovIdentity -Name <string> -Registry <hashtable> -ArtifactTypes <hashtable>` returning a PSCustomObject with `Resolved [bool]`, `DocumentId`, `Category`, `ApprovalStatus`, `Reason`.

- [ ] **Step 1: Write the failing tests**

Create `tests/powershell/Identity.Tests.ps1`:

```powershell
#Requires -Version 7.0
BeforeAll {
    $script:RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
    Import-Module (Join-Path $script:RepoRoot 'scripts/GovLib/GovLib.psd1') -Force
    $script:Types    = Import-GovArtifactTypes -Path (Join-Path $script:RepoRoot 'config/artifact-types.json')
    $script:Registry = Import-GovRegistry -Path (Join-Path $script:RepoRoot 'fixtures/registry-sample.csv')
}

Describe 'Import-GovArtifactTypes' {
    It 'maps artifact codes to their categories' {
        $script:Types['DS']  | Should -Be 'GOV'
        $script:Types['TCL'] | Should -Be 'TRN'
        $script:Types['SCL'] | Should -Be 'MST'
    }
}

Describe 'Get-GovDocumentIdFromName' {
    It 'extracts an ID embedded in a filename' {
        Get-GovDocumentIdFromName -Name 'ORG-GOV-DS-016 - Northwind Data Sheet.docx' |
            Should -Be 'ORG-GOV-DS-016'
    }
    It 'returns null when no ID is present' {
        Get-GovDocumentIdFromName -Name 'ORG - Some Artifact - Client - v1.0.docx' |
            Should -BeNullOrEmpty
    }
    It 'returns null for a partial or malformed ID' {
        Get-GovDocumentIdFromName -Name 'ORG-GOV-DS.docx' | Should -BeNullOrEmpty
    }
}

Describe 'Resolve-GovIdentity' {
    It 'resolves a registered ID to its registry status and category' {
        $r = Resolve-GovIdentity -Name 'ORG-GOV-DS-016 - Thing.docx' -Registry $script:Registry -ArtifactTypes $script:Types
        $r.Resolved       | Should -BeTrue
        $r.Category       | Should -Be 'GOV'
        $r.ApprovalStatus | Should -Be 'Draft'
    }

    It 'refuses an illegal category and code pairing' {
        $r = Resolve-GovIdentity -Name 'ORG-GOV-TCL-001 - Thing.docx' -Registry $script:Registry -ArtifactTypes $script:Types
        $r.Resolved | Should -BeFalse
        $r.Reason   | Should -BeLike '*pairing*'
    }

    It 'refuses an unknown artifact code' {
        $r = Resolve-GovIdentity -Name 'ORG-GOV-ZZZ-001 - Thing.docx' -Registry $script:Registry -ArtifactTypes $script:Types
        $r.Resolved | Should -BeFalse
        $r.Reason   | Should -BeLike '*not a known artifact code*'
    }

    It 'refuses an ID that is absent from the registry' {
        $r = Resolve-GovIdentity -Name 'ORG-GOV-DS-999 - Thing.docx' -Registry $script:Registry -ArtifactTypes $script:Types
        $r.Resolved | Should -BeFalse
        $r.Reason   | Should -BeLike '*not in the ID Registry*'
    }

    It 'reports no ID when the filename carries none' {
        $r = Resolve-GovIdentity -Name 'random notes.docx' -Registry $script:Registry -ArtifactTypes $script:Types
        $r.Resolved | Should -BeFalse
        $r.Reason   | Should -BeLike '*no Document ID*'
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `pwsh -NoProfile -Command "Invoke-Pester -Path tests/powershell/Identity.Tests.ps1 -Output Detailed"`

Expected: FAIL. Functions not defined.

- [ ] **Step 3: Implement identity resolution**

Create `scripts/GovLib/Identity.ps1`:

```powershell
#Requires -Version 7.0

# Matches the Document ID scheme ORG-[CATEGORY]-[ARTIFACT CODE]-[NNN].
# Both boundaries are asserted deliberately. Without the trailing (?!\d),
# ORG-GOV-DS-0165 truncates to the registered ORG-GOV-DS-016 and resolves
# with the wrong identity; without the leading lookbehind, a character glued
# onto ORG still matches. This is the tool's only identity claim and nothing
# downstream re-checks it. Owner ruling 2026-08-01.
$script:GovIdPattern = '(?<![A-Z0-9])ORG-[A-Z]{3}-[A-Z]{2,4}-\d{3}(?!\d)'

function Import-GovArtifactTypes {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [string] $Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Artifact types config not found: $Path"
    }
    $json = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    $map = @{}
    foreach ($type in $json.artifactTypes) {
        $map[$type.code.ToUpperInvariant()] = $type.category
    }
    if ($map.Count -eq 0) {
        throw "Artifact types config contained no artifactTypes entries: $Path"
    }
    return $map
}

function Get-GovDocumentIdFromName {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [string] $Name)

    $m = [regex]::Match($Name.ToUpperInvariant(), $script:GovIdPattern)
    if ($m.Success) { return $m.Value }
    return $null
}

function Resolve-GovIdentity {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string]    $Name,
        [Parameter(Mandatory)] [hashtable] $Registry,
        [Parameter(Mandatory)] [hashtable] $ArtifactTypes
    )

    $result = [pscustomobject]@{
        Resolved       = $false
        DocumentId     = $null
        Category       = $null
        ApprovalStatus = $null
        Reason         = $null
    }

    $id = Get-GovDocumentIdFromName -Name $Name
    if (-not $id) {
        $result.Reason = 'Filename carries no Document ID.'
        return $result
    }
    $result.DocumentId = $id

    $parts    = $id -split '-'
    $category = $parts[1]
    $code     = $parts[2]

    if (-not $ArtifactTypes.ContainsKey($code)) {
        $result.Reason = "'$code' is not a known artifact code."
        return $result
    }
    if ($ArtifactTypes[$code] -ne $category) {
        $result.Reason = "Illegal category and code pairing: '$code' belongs to category '$($ArtifactTypes[$code])', not '$category'."
        return $result
    }
    if (-not $Registry.ContainsKey($id)) {
        $result.Reason = "'$id' is not in the ID Registry export."
        return $result
    }

    $result.Category       = $category
    $result.ApprovalStatus = $Registry[$id].ApprovalStatus
    $result.Resolved       = $true
    return $result
}
```

- [ ] **Step 4: Export the functions**

Add `'Import-GovArtifactTypes'`, `'Get-GovDocumentIdFromName'`, `'Resolve-GovIdentity'` to `FunctionsToExport`.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `pwsh -NoProfile -Command "Invoke-Pester -Path tests/powershell/Identity.Tests.ps1 -Output Detailed"`

Expected: PASS, 10 tests.

- [ ] **Step 6: Commit**

```bash
git add scripts/GovLib/Identity.ps1 scripts/GovLib/GovLib.psd1 tests/powershell/Identity.Tests.ps1
git commit -m "feat: filename-based Document ID resolution with pairing validation"
```

---

### Task 5: Inventory

Read-only sweep that never opens file content, plus the optional SharePoint export join that supplies `Modified By` and surfaces sync divergence.

**Files:**
- Create: `scripts/GovLib/Inventory.ps1`
- Create: `tests/powershell/Inventory.Tests.ps1`
- Modify: `scripts/GovLib/GovLib.psd1`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces:
  - `Get-GovInventory -LibraryRoot <string>` returning an array of PSCustomObject with `Name`, `FolderPath`, `RelativePath`, `Modified`, `ModifiedTicks`, `ModifiedBy`, `FileType`, `SizeBytes`, `IsPlaceholder`, `Zone`. `Zone` is `Root` or `Nested`.
  - `Join-GovSharePointExport -Inventory <array> -ExportPath <string>` returning a PSCustomObject with `Rows`, `OnlyInExport`, `OnlyOnDisk`.

- [ ] **Step 1: Write the failing tests**

Create `tests/powershell/Inventory.Tests.ps1`:

```powershell
#Requires -Version 7.0
BeforeAll {
    $script:RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
    Import-Module (Join-Path $script:RepoRoot 'scripts/GovLib/GovLib.psd1') -Force
    Import-Module (Join-Path $PSScriptRoot 'TestHelpers.psm1') -Force
}

Describe 'Get-GovInventory' {
    It 'classifies root files as Root and nested files as Nested' {
        $root = New-TestLibrary -Files @{
            'loose.docx'                              = 'a'
            'Controlled Documents/Governance/in.docx' = 'b'
        }
        $inv = Get-GovInventory -LibraryRoot $root
        ($inv | Where-Object Name -eq 'loose.docx').Zone | Should -Be 'Root'
        ($inv | Where-Object Name -eq 'in.docx').Zone    | Should -Be 'Nested'
    }

    It 'records a relative path using forward slashes' {
        $root = New-TestLibrary -Files @{ 'Controlled Documents/Governance/in.docx' = 'b' }
        (Get-GovInventory -LibraryRoot $root)[0].RelativePath |
            Should -Be 'Controlled Documents/Governance/in.docx'
    }

    It 'leaves ModifiedBy empty because NTFS does not track it' {
        $root = New-TestLibrary -Files @{ 'a.docx' = 'a' }
        (Get-GovInventory -LibraryRoot $root)[0].ModifiedBy | Should -BeNullOrEmpty
    }

    It 'never opens file content' {
        # A file held open with no share permissions cannot be read.
        # Inventory must still succeed, proving it only touches metadata.
        $root = New-TestLibrary -Files @{ 'locked.docx' = 'secret' }
        $full = Join-Path $root 'locked.docx'
        $fs = [System.IO.File]::Open($full, 'Open', 'Read', 'None')
        try {
            $inv = Get-GovInventory -LibraryRoot $root
            ($inv | Where-Object Name -eq 'locked.docx') | Should -Not -BeNullOrEmpty
        }
        finally { $fs.Dispose() }
    }

    It 'throws when the library root does not exist' {
        { Get-GovInventory -LibraryRoot 'C:\nope\missing' } | Should -Throw '*not found*'
    }
}

Describe 'Join-GovSharePointExport' {
    BeforeAll {
        function New-ExportCsv {
            param([string] $Content)
            $r = New-TestRoot
            $p = Join-Path $r 'export.csv'
            Set-Content -LiteralPath $p -Value $Content -Encoding utf8NoBOM
            return $p
        }
    }

    It 'fills ModifiedBy from the export' {
        $root = New-TestLibrary -Files @{ 'a.docx' = 'a' }
        $inv  = Get-GovInventory -LibraryRoot $root
        $exp  = New-ExportCsv -Content "Name,Folder path,Modified By`na.docx,,Jane Doe"
        $j = Join-GovSharePointExport -Inventory $inv -ExportPath $exp
        ($j.Rows | Where-Object Name -eq 'a.docx').ModifiedBy | Should -Be 'Jane Doe'
    }

    It 'reports rows present in the export but absent on disk' {
        $root = New-TestLibrary -Files @{ 'a.docx' = 'a' }
        $inv  = Get-GovInventory -LibraryRoot $root
        $exp  = New-ExportCsv -Content "Name,Folder path,Modified By`na.docx,,Jane`ncloud.docx,,Bob"
        (Join-GovSharePointExport -Inventory $inv -ExportPath $exp).OnlyInExport |
            Should -Contain 'cloud.docx'
    }

    It 'reports rows present on disk but absent from the export' {
        $root = New-TestLibrary -Files @{ 'a.docx' = 'a'; 'pending.docx' = 'b' }
        $inv  = Get-GovInventory -LibraryRoot $root
        $exp  = New-ExportCsv -Content "Name,Folder path,Modified By`na.docx,,Jane"
        (Join-GovSharePointExport -Inventory $inv -ExportPath $exp).OnlyOnDisk |
            Should -Contain 'pending.docx'
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `pwsh -NoProfile -Command "Invoke-Pester -Path tests/powershell/Inventory.Tests.ps1 -Output Detailed"`

Expected: FAIL. Functions not defined.

- [ ] **Step 3: Implement inventory**

Create `scripts/GovLib/Inventory.ps1`:

```powershell
#Requires -Version 7.0

# FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS. Set on cloud-only OneDrive placeholders.
$script:GovRecallAttribute = 0x00400000

function Get-GovInventory {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [string] $LibraryRoot)

    if (-not (Test-Path -LiteralPath $LibraryRoot -PathType Container)) {
        throw "Library root not found: $LibraryRoot"
    }
    $rootFull = [System.IO.Path]::GetFullPath($LibraryRoot).TrimEnd('\', '/')

    # Metadata only. No call here opens file content.
    Get-ChildItem -LiteralPath $rootFull -Recurse -File -Force | ForEach-Object {
        $relative = $_.FullName.Substring($rootFull.Length).TrimStart('\', '/') -replace '\\', '/'
        $folder   = Split-Path -Parent $relative
        [pscustomobject]@{
            Name          = $_.Name
            FolderPath    = $folder
            RelativePath  = $relative
            Modified      = $_.LastWriteTimeUtc.ToString('o')
            ModifiedTicks = $_.LastWriteTimeUtc.Ticks
            ModifiedBy    = ''
            FileType      = $_.Extension.TrimStart('.')
            SizeBytes     = $_.Length
            IsPlaceholder = (([int]$_.Attributes -band $script:GovRecallAttribute) -ne 0)
            Zone          = if ([string]::IsNullOrEmpty($folder)) { 'Root' } else { 'Nested' }
        }
    }
}

function Join-GovSharePointExport {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [object[]] $Inventory,
        [Parameter(Mandatory)] [string]   $ExportPath
    )

    if (-not (Test-Path -LiteralPath $ExportPath -PathType Leaf)) {
        throw "SharePoint export not found: $ExportPath"
    }

    $export = @(Import-Csv -LiteralPath $ExportPath)
    $byKey  = @{}
    foreach ($row in $export) {
        $folder = ''
        if ($row.PSObject.Properties.Name -contains 'Folder path') {
            $folder = ([string]$row.'Folder path').Trim().Trim('/')
        }
        $key = if ($folder) { "$folder/$($row.Name)" } else { [string]$row.Name }
        $byKey[$key.ToLowerInvariant()] = $row
    }

    $seen = @{}
    $rows = foreach ($item in $Inventory) {
        $key = $item.RelativePath.ToLowerInvariant()
        if ($byKey.ContainsKey($key)) {
            $seen[$key] = $true
            $match = $byKey[$key]
            if ($match.PSObject.Properties.Name -contains 'Modified By') {
                $item.ModifiedBy = [string]$match.'Modified By'
            }
        }
        $item
    }

    $onlyInExport = foreach ($key in $byKey.Keys) {
        if (-not $seen.ContainsKey($key)) { $byKey[$key].Name }
    }
    $onlyOnDisk = foreach ($item in $Inventory) {
        if (-not $byKey.ContainsKey($item.RelativePath.ToLowerInvariant())) { $item.Name }
    }

    return [pscustomobject]@{
        Rows         = @($rows)
        OnlyInExport = @($onlyInExport)
        OnlyOnDisk   = @($onlyOnDisk)
    }
}
```

- [ ] **Step 4: Export the functions**

Add `'Get-GovInventory'` and `'Join-GovSharePointExport'` to `FunctionsToExport`.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `pwsh -NoProfile -Command "Invoke-Pester -Path tests/powershell/Inventory.Tests.ps1 -Output Detailed"`

Expected: PASS, 8 tests.

- [ ] **Step 6: Commit**

```bash
git add scripts/GovLib/Inventory.ps1 scripts/GovLib/GovLib.psd1 tests/powershell/Inventory.Tests.ps1
git commit -m "feat: metadata-only inventory sweep with SharePoint export join"
```

---

### Task 6: Plan construction and tagging

Builds the plan rows. This is where the identification constraint becomes behavior: the tool proposes a target only for rows it resolved from a filename, and proposes nothing for everything else.

**Files:**
- Create: `scripts/GovLib/Plan.ps1`
- Create: `tests/powershell/Plan.Tests.ps1`
- Modify: `scripts/GovLib/GovLib.psd1`

**Interfaces:**
- Consumes: `Import-GovBaseline`, `Test-GovProtectedPath`, `Get-GovProtectionReason`, `Test-GovPathLength`, `Resolve-GovIdentity`, `Get-GovInventory`.
- Produces: `New-GovPlan` returning an array of plan rows with these exact properties: `Name`, `FolderPath`, `RelativePath`, `Modified`, `SourceModifiedTicks`, `ModifiedBy`, `FileType`, `SuggestedFolder`, `FolderExists`, `ActionRequired`, `RuleSource`, `Notes`, `Approved`, `Prefix`, `Suffix`, `NewName`.

  Signature:

  ```
  New-GovPlan -Inventory <object[]> -Baseline <object> -LibraryRoot <string>
              [-Registry <hashtable>] [-ArtifactTypes <hashtable>]
              [-TagMode None|DocumentId|Custom] [-TagValue <string>]
              [-TagPosition Prefix|Suffix] [-TagScope <string>]
  ```

  `ActionRequired` is one of `Move to Existing Folder`, `Create Folder and Move`, `Review Required`, `Escalate`, `Leave in Place`.

- [ ] **Step 1: Write the failing tests**

Create `tests/powershell/Plan.Tests.ps1`:

```powershell
#Requires -Version 7.0
BeforeAll {
    $script:RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
    Import-Module (Join-Path $script:RepoRoot 'scripts/GovLib/GovLib.psd1') -Force
    Import-Module (Join-Path $PSScriptRoot 'TestHelpers.psm1') -Force

    $script:Baseline = Import-GovBaseline     -Path (Join-Path $script:RepoRoot 'config/library-baseline.json')
    $script:Types    = Import-GovArtifactTypes -Path (Join-Path $script:RepoRoot 'config/artifact-types.json')
    $script:Registry = Import-GovRegistry      -Path (Join-Path $script:RepoRoot 'fixtures/registry-sample.csv')
}

Describe 'New-GovPlan' {
    It 'proposes a target for a root file whose filename carries a registered Draft ID' {
        $root = New-TestLibrary -Files @{ 'ORG-GOV-DS-016 - Thing.docx' = 'x' }
        $plan = New-GovPlan -Inventory (Get-GovInventory -LibraryRoot $root) -Baseline $script:Baseline `
                            -LibraryRoot $root -Registry $script:Registry -ArtifactTypes $script:Types
        $plan[0].SuggestedFolder | Should -Be 'Master Development Workspace'
        $plan[0].ActionRequired  | Should -BeIn @('Move to Existing Folder', 'Create Folder and Move')
        $plan[0].Approved        | Should -BeNullOrEmpty
    }

    It 'marks an unidentifiable root file Review Required and proposes nothing' {
        $root = New-TestLibrary -Files @{ 'random notes.docx' = 'x' }
        $plan = New-GovPlan -Inventory (Get-GovInventory -LibraryRoot $root) -Baseline $script:Baseline `
                            -LibraryRoot $root -Registry $script:Registry -ArtifactTypes $script:Types
        $plan[0].ActionRequired  | Should -Be 'Review Required'
        $plan[0].SuggestedFolder | Should -BeNullOrEmpty
    }

    It 'marks a Retired file Leave in Place even though it was identified' {
        $root = New-TestLibrary -Files @{ 'ORG-GOV-DS-017 - Old.docx' = 'x' }
        $plan = New-GovPlan -Inventory (Get-GovInventory -LibraryRoot $root) -Baseline $script:Baseline `
                            -LibraryRoot $root -Registry $script:Registry -ArtifactTypes $script:Types
        $plan[0].ActionRequired | Should -Be 'Leave in Place'
        $plan[0].RuleSource     | Should -BeLike '*notDefined*'
    }

    It 'marks every row Review Required when no registry is supplied' {
        $root = New-TestLibrary -Files @{ 'ORG-GOV-DS-016 - Thing.docx' = 'x' }
        $plan = New-GovPlan -Inventory (Get-GovInventory -LibraryRoot $root) -Baseline $script:Baseline `
                            -LibraryRoot $root
        $plan[0].ActionRequired  | Should -Be 'Review Required'
        $plan[0].SuggestedFolder | Should -BeNullOrEmpty
    }

    It 'excludes nested files from the plan entirely' {
        # Capture with @() at the call site. A bare assignment of no output
        # yields $null, and @($null).Count is 1, which would mask a real bug.
        $root = New-TestLibrary -Files @{ 'Controlled Documents/Governance/in.docx' = 'x' }
        $plan = @(New-GovPlan -Inventory (Get-GovInventory -LibraryRoot $root) -Baseline $script:Baseline `
                              -LibraryRoot $root -Registry $script:Registry -ArtifactTypes $script:Types)
        $plan.Count | Should -Be 0
    }

    It 'sets FolderExists false and Create Folder and Move when the target is absent' {
        $root = New-TestLibrary -Files @{ 'ORG-GOV-DS-016 - Thing.docx' = 'x' }
        $plan = New-GovPlan -Inventory (Get-GovInventory -LibraryRoot $root) -Baseline $script:Baseline `
                            -LibraryRoot $root -Registry $script:Registry -ArtifactTypes $script:Types
        $plan[0].FolderExists   | Should -BeFalse
        $plan[0].ActionRequired | Should -Be 'Create Folder and Move'
    }

    It 'records the source modification ticks for staleness detection' {
        $root = New-TestLibrary -Files @{ 'a.docx' = 'x' }
        $plan = New-GovPlan -Inventory (Get-GovInventory -LibraryRoot $root) -Baseline $script:Baseline -LibraryRoot $root
        $plan[0].SourceModifiedTicks | Should -BeGreaterThan 0
    }
}

Describe 'New-GovPlan tagging' {
    It 'leaves tag columns empty when fileNaming status is notDefined' {
        $root = New-TestLibrary -Files @{ 'ORG-GOV-DS-016 - Thing.docx' = 'x' }
        $plan = New-GovPlan -Inventory (Get-GovInventory -LibraryRoot $root) -Baseline $script:Baseline `
                            -LibraryRoot $root -Registry $script:Registry -ArtifactTypes $script:Types `
                            -TagMode DocumentId
        $plan[0].Prefix  | Should -BeNullOrEmpty
        $plan[0].NewName | Should -BeNullOrEmpty
        $plan[0].Notes   | Should -BeLike '*fileNaming*'
    }

    It 'fills the Document ID prefix when fileNaming is governed' {
        $governed = $script:Baseline | ConvertTo-Json -Depth 20 | ConvertFrom-Json
        $governed.fileNaming.status = 'governed'
        $root = New-TestLibrary -Files @{ 'ORG-GOV-DS-016 - Thing.docx' = 'x' }
        $plan = New-GovPlan -Inventory (Get-GovInventory -LibraryRoot $root) -Baseline $governed `
                            -LibraryRoot $root -Registry $script:Registry -ArtifactTypes $script:Types `
                            -TagMode DocumentId
        $plan[0].NewName | Should -BeLike 'ORG-GOV-DS-016*'
    }

    It 'refuses a rename that would exceed the path length limit' {
        $governed = $script:Baseline | ConvertTo-Json -Depth 20 | ConvertFrom-Json
        $governed.fileNaming.status = 'governed'
        $longName = 'ORG-GOV-DS-016 - ' + ('x' * 200) + '.docx'
        $root = New-TestLibrary -Files @{ $longName = 'x' }
        $plan = New-GovPlan -Inventory (Get-GovInventory -LibraryRoot $root) -Baseline $governed `
                            -LibraryRoot $root -Registry $script:Registry -ArtifactTypes $script:Types `
                            -TagMode Custom -TagValue ('y' * 100) -TagPosition Prefix
        $plan[0].NewName | Should -BeNullOrEmpty
        $plan[0].Notes   | Should -BeLike '*path length*'
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `pwsh -NoProfile -Command "Invoke-Pester -Path tests/powershell/Plan.Tests.ps1 -Output Detailed"`

Expected: FAIL. `New-GovPlan` is not defined.

- [ ] **Step 3: Implement plan construction**

Create `scripts/GovLib/Plan.ps1`:

```powershell
#Requires -Version 7.0

function Get-GovPlacementTarget {
    <# Returns a PSCustomObject with TargetPath, Status, RuleSource for a resolved identity. #>
    param(
        [Parameter(Mandatory)] [object] $Baseline,
        [Parameter(Mandatory)] [string] $ApprovalStatus,
        [string] $Category
    )
    foreach ($mapping in $Baseline.placementRules.mappings) {
        $m = $mapping.match
        if ($m.approvalStatus -ne $ApprovalStatus) { continue }
        if ($m.PSObject.Properties.Name -contains 'idCategory' -and $m.idCategory -ne $Category) { continue }
        return [pscustomobject]@{
            TargetPath = $mapping.targetPath
            Status     = $mapping.status
            RuleSource = $mapping.ruleSource
        }
    }
    return $null
}

function New-GovPlan {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [object[]]  $Inventory,
        [Parameter(Mandatory)] [object]    $Baseline,
        [Parameter(Mandatory)] [string]    $LibraryRoot,
        [hashtable] $Registry,
        [hashtable] $ArtifactTypes,
        [ValidateSet('None','DocumentId','Custom')] [string] $TagMode = 'None',
        [string] $TagValue,
        [ValidateSet('Prefix','Suffix')] [string] $TagPosition = 'Prefix',
        [string] $TagScope
    )

    $namingGoverned = ($Baseline.fileNaming.status -eq 'governed')

    foreach ($item in @($Inventory | Where-Object Zone -eq 'Root')) {

        $row = [pscustomobject]@{
            Name                = $item.Name
            FolderPath          = $item.FolderPath
            RelativePath        = $item.RelativePath
            Modified            = $item.Modified
            SourceModifiedTicks = $item.ModifiedTicks
            ModifiedBy          = $item.ModifiedBy
            FileType            = $item.FileType
            SuggestedFolder     = ''
            FolderExists        = $false
            ActionRequired      = 'Review Required'
            RuleSource          = ''
            Notes               = ''
            Approved            = ''
            Prefix              = ''
            Suffix              = ''
            NewName             = ''
        }

        $reason = Get-GovProtectionReason -RelativePath $item.RelativePath -Baseline $Baseline
        if ($reason) {
            $row.ActionRequired = 'Leave in Place'
            $row.RuleSource     = 'doNotTouch'
            $row.Notes          = $reason
            $row
            continue
        }

        $identity = $null
        if ($Registry -and $ArtifactTypes) {
            $identity = Resolve-GovIdentity -Name $item.Name -Registry $Registry -ArtifactTypes $ArtifactTypes
        }

        if (-not $identity -or -not $identity.Resolved) {
            $row.Notes = if ($identity) { $identity.Reason } else { 'No ID Registry export supplied.' }
            $row.RuleSource = 'identification status blocked; human classification required (SOP Step 3)'
            $row = Set-GovTagColumns -Row $row -TagMode $TagMode -TagValue $TagValue `
                                     -TagPosition $TagPosition -TagScope $TagScope `
                                     -NamingGoverned $namingGoverned -LibraryRoot $LibraryRoot -DocumentId $null
            $row
            continue
        }

        $placement = Get-GovPlacementTarget -Baseline $Baseline -ApprovalStatus $identity.ApprovalStatus -Category $identity.Category

        if (-not $placement -or $placement.Status -eq 'notDefined' -or -not $placement.TargetPath) {
            $row.ActionRequired = 'Leave in Place'
            $row.RuleSource     = "placement status notDefined for approvalStatus '$($identity.ApprovalStatus)'"
            $row.Notes          = 'No governed target folder. Leave in place, log, escalate to Registry Owner.'
            $row
            continue
        }

        $targetFull       = Join-Path $LibraryRoot ($placement.TargetPath -replace '/', '\')
        $row.SuggestedFolder = $placement.TargetPath
        $row.FolderExists    = Test-Path -LiteralPath $targetFull -PathType Container
        $row.ActionRequired  = if ($row.FolderExists) { 'Move to Existing Folder' } else { 'Create Folder and Move' }
        $row.RuleSource      = $placement.RuleSource
        if ($placement.Status -eq 'unconfirmed') {
            $row.Notes = 'Target path status is unconfirmed. Verify against the live library before approving.'
        }

        $row = Set-GovTagColumns -Row $row -TagMode $TagMode -TagValue $TagValue `
                                 -TagPosition $TagPosition -TagScope $TagScope `
                                 -NamingGoverned $namingGoverned -LibraryRoot $LibraryRoot `
                                 -DocumentId $identity.DocumentId
        $row
    }
}

function Set-GovTagColumns {
    param(
        [Parameter(Mandatory)] [object] $Row,
        [string] $TagMode,
        [string] $TagValue,
        [string] $TagPosition,
        [string] $TagScope,
        [bool]   $NamingGoverned,
        [string] $LibraryRoot,
        [string] $DocumentId
    )

    if ($TagMode -eq 'None') { return $Row }

    if (-not $NamingGoverned) {
        $Row.Notes = (@($Row.Notes, 'Tagging skipped: baseline fileNaming status is notDefined.') |
                      Where-Object { $_ }) -join ' '
        return $Row
    }

    if ($TagScope -and -not $Row.RelativePath.StartsWith($TagScope, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $Row
    }

    $tag = if ($TagMode -eq 'DocumentId') { $DocumentId } else { $TagValue }
    if (-not $tag) {
        $Row.Notes = (@($Row.Notes, 'Tagging skipped: no tag value available for this row.') |
                      Where-Object { $_ }) -join ' '
        return $Row
    }

    $stem      = [System.IO.Path]::GetFileNameWithoutExtension($Row.Name)
    $extension = [System.IO.Path]::GetExtension($Row.Name)
    $newName   = if ($TagPosition -eq 'Prefix') { "$tag - $stem$extension" } else { "$stem - $tag$extension" }

    $folder      = if ($Row.SuggestedFolder) { $Row.SuggestedFolder } else { $Row.FolderPath }
    $projected   = Join-Path (Join-Path $LibraryRoot ($folder -replace '/', '\')) $newName

    if (-not (Test-GovPathLength -Path $projected)) {
        $Row.Notes = (@($Row.Notes, "Rename refused: projected path length $($projected.Length) exceeds the limit.") |
                      Where-Object { $_ }) -join ' '
        return $Row
    }

    if ($TagPosition -eq 'Prefix') { $Row.Prefix = $tag } else { $Row.Suffix = $tag }
    $Row.NewName = $newName
    return $Row
}
```

- [ ] **Step 4: Export the functions**

Add `'New-GovPlan'` and `'Get-GovPlacementTarget'` to `FunctionsToExport`.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `pwsh -NoProfile -Command "Invoke-Pester -Path tests/powershell/Plan.Tests.ps1 -Output Detailed"`

Expected: PASS, 10 tests.

- [ ] **Step 6: Commit**

```bash
git add scripts/GovLib/Plan.ps1 scripts/GovLib/GovLib.psd1 tests/powershell/Plan.Tests.ps1
git commit -m "feat: plan construction with identity-gated proposals and tagging"
```

---

### Task 7: Append-only undo journal

Written and flushed before each filesystem change so a killed process still leaves an accurate record.

**Files:**
- Create: `scripts/GovLib/Journal.ps1`
- Create: `tests/powershell/Journal.Tests.ps1`
- Modify: `scripts/GovLib/GovLib.psd1`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `New-GovJournal -Path <string>` creates the file and returns its path.
  - `Add-GovJournalEntry -JournalPath <string> -Action <string> -Source <string> -Destination <string>` appends one JSON object per line and flushes to disk.
  - `Read-GovJournal -Path <string>` returns an array of entries with `Action`, `Source`, `Destination`, `TimestampUtc`.

- [ ] **Step 1: Write the failing tests**

Create `tests/powershell/Journal.Tests.ps1`:

```powershell
#Requires -Version 7.0
BeforeAll {
    $script:RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
    Import-Module (Join-Path $script:RepoRoot 'scripts/GovLib/GovLib.psd1') -Force
    Import-Module (Join-Path $PSScriptRoot 'TestHelpers.psm1') -Force
}

Describe 'Journal' {
    It 'creates an empty journal file' {
        $p = Join-Path (New-TestRoot) 'undo-manifest.jsonl'
        New-GovJournal -Path $p | Should -Be $p
        Test-Path -LiteralPath $p | Should -BeTrue
    }

    It 'appends entries that survive an immediate read' {
        $p = Join-Path (New-TestRoot) 'undo-manifest.jsonl'
        New-GovJournal -Path $p | Out-Null
        Add-GovJournalEntry -JournalPath $p -Action 'move' -Source 'a.docx' -Destination 'X/a.docx'
        Add-GovJournalEntry -JournalPath $p -Action 'rename' -Source 'b.docx' -Destination 'c.docx'
        $entries = Read-GovJournal -Path $p
        $entries.Count          | Should -Be 2
        $entries[0].Action      | Should -Be 'move'
        $entries[1].Destination | Should -Be 'c.docx'
    }

    It 'writes each entry on its own line so a partial run stays parseable' {
        $p = Join-Path (New-TestRoot) 'undo-manifest.jsonl'
        New-GovJournal -Path $p | Out-Null
        Add-GovJournalEntry -JournalPath $p -Action 'move' -Source 'a' -Destination 'b'
        (Get-Content -LiteralPath $p | Measure-Object -Line).Lines | Should -Be 1
    }

    It 'stamps every entry with a UTC timestamp' {
        $p = Join-Path (New-TestRoot) 'undo-manifest.jsonl'
        New-GovJournal -Path $p | Out-Null
        Add-GovJournalEntry -JournalPath $p -Action 'move' -Source 'a' -Destination 'b'
        (Read-GovJournal -Path $p)[0].TimestampUtc | Should -Not -BeNullOrEmpty
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `pwsh -NoProfile -Command "Invoke-Pester -Path tests/powershell/Journal.Tests.ps1 -Output Detailed"`

Expected: FAIL. Functions not defined.

- [ ] **Step 3: Implement the journal**

Create `scripts/GovLib/Journal.ps1`:

```powershell
#Requires -Version 7.0

function New-GovJournal {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [string] $Path)

    $dir = Split-Path -Parent $Path
    if ($dir -and -not (Test-Path -LiteralPath $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }
    if (-not (Test-Path -LiteralPath $Path)) {
        New-Item -ItemType File -Path $Path | Out-Null
    }
    return $Path
}

function Add-GovJournalEntry {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $JournalPath,
        [Parameter(Mandatory)] [string] $Action,
        [Parameter(Mandatory)] [string] $Source,
        [Parameter(Mandatory)] [string] $Destination
    )

    $entry = [pscustomobject]@{
        TimestampUtc = (Get-Date).ToUniversalTime().ToString('o')
        Action       = $Action
        Source       = $Source
        Destination  = $Destination
    }
    $line = $entry | ConvertTo-Json -Compress

    # StreamWriter with AutoFlush guarantees the line reaches disk before the
    # caller performs the filesystem change this entry describes.
    $writer = [System.IO.StreamWriter]::new($JournalPath, $true, [System.Text.UTF8Encoding]::new($false))
    try {
        $writer.AutoFlush = $true
        $writer.WriteLine($line)
    }
    finally { $writer.Dispose() }
}

function Read-GovJournal {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [string] $Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Undo journal not found: $Path"
    }
    Get-Content -LiteralPath $Path |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        ForEach-Object { $_ | ConvertFrom-Json }
}
```

- [ ] **Step 4: Export the functions**

Add `'New-GovJournal'`, `'Add-GovJournalEntry'`, `'Read-GovJournal'` to `FunctionsToExport`.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `pwsh -NoProfile -Command "Invoke-Pester -Path tests/powershell/Journal.Tests.ps1 -Output Detailed"`

Expected: PASS, 4 tests.

- [ ] **Step 6: Commit**

```bash
git add scripts/GovLib/Journal.ps1 scripts/GovLib/GovLib.psd1 tests/powershell/Journal.Tests.ps1
git commit -m "feat: append-only undo journal flushed before each change"
```

---

### Task 8: Apply

Executes approved rows only. This task carries invariants 1, 2, 3, 4, and 6.

**Files:**
- Create: `scripts/GovLib/Apply.ps1`
- Create: `tests/powershell/Apply.Tests.ps1`
- Modify: `scripts/GovLib/GovLib.psd1`

**Interfaces:**
- Consumes: `Import-GovBaseline`, `Get-GovProtectionReason`, `Test-GovPathLength`, `New-GovJournal`, `Add-GovJournalEntry`.
- Produces: `Invoke-GovApply -PlanPath <string> -LibraryRoot <string> -Baseline <object> -JournalPath <string> [-BatchSize <int>] [-SettleSeconds <int>]` returning a PSCustomObject with `Moved [int]`, `Skipped [int]`, `Refused [int]`, `Messages [string[]]`, `JournalPath [string]`.

- [ ] **Step 1: Write the failing tests**

Create `tests/powershell/Apply.Tests.ps1`:

```powershell
#Requires -Version 7.0
BeforeAll {
    $script:RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
    Import-Module (Join-Path $script:RepoRoot 'scripts/GovLib/GovLib.psd1') -Force
    Import-Module (Join-Path $PSScriptRoot 'TestHelpers.psm1') -Force
    $script:Baseline = Import-GovBaseline -Path (Join-Path $script:RepoRoot 'config/library-baseline.json')

    function Save-Plan {
        param([object[]] $Rows, [string] $Root)
        $p = Join-Path $Root 'plan.csv'
        $Rows | Export-Csv -LiteralPath $p -NoTypeInformation -Encoding utf8NoBOM
        return $p
    }

    function New-Row {
        param(
            [string] $Name, [string] $Suggested, [string] $Action,
            [string] $Approved = 'Y', [string] $NewName = '', [long] $Ticks = 0
        )
        [pscustomobject]@{
            Name = $Name; FolderPath = ''; RelativePath = $Name; Modified = ''
            SourceModifiedTicks = $Ticks; ModifiedBy = ''; FileType = 'docx'
            SuggestedFolder = $Suggested; FolderExists = $false; ActionRequired = $Action
            RuleSource = 't'; Notes = ''; Approved = $Approved
            Prefix = ''; Suffix = ''; NewName = $NewName
        }
    }
}

Describe 'Invoke-GovApply' {
    It 'moves an approved row into an existing folder' {
        $root = New-TestLibrary -Files @{ 'a.docx' = 'x'; 'Controlled Documents/Governance/keep.txt' = 'k' }
        $ticks = (Get-Item (Join-Path $root 'a.docx')).LastWriteTimeUtc.Ticks
        $plan  = Save-Plan -Root $root -Rows @(New-Row -Name 'a.docx' -Suggested 'Controlled Documents/Governance' -Action 'Move to Existing Folder' -Ticks $ticks)
        $r = Invoke-GovApply -PlanPath $plan -LibraryRoot $root -Baseline $script:Baseline -JournalPath (Join-Path $root 'j.jsonl') -SettleSeconds 0
        $r.Moved | Should -Be 1
        Test-Path -LiteralPath (Join-Path $root 'Controlled Documents/Governance/a.docx') | Should -BeTrue
    }

    It 'does not move a row that is not approved' {
        $root = New-TestLibrary -Files @{ 'a.docx' = 'x' }
        $ticks = (Get-Item (Join-Path $root 'a.docx')).LastWriteTimeUtc.Ticks
        $plan  = Save-Plan -Root $root -Rows @(New-Row -Name 'a.docx' -Suggested 'Controlled Documents/Governance' -Action 'Create Folder and Move' -Approved '' -Ticks $ticks)
        $r = Invoke-GovApply -PlanPath $plan -LibraryRoot $root -Baseline $script:Baseline -JournalPath (Join-Path $root 'j.jsonl') -SettleSeconds 0
        $r.Moved | Should -Be 0
        Test-Path -LiteralPath (Join-Path $root 'a.docx') | Should -BeTrue
    }

    It 'refuses a protected path even when the row is approved' {
        $root = New-TestLibrary -Files @{ 'Document Registry/ORG-ID-Registry.xlsx' = 'x' }
        $row  = New-Row -Name 'ORG-ID-Registry.xlsx' -Suggested 'Archive' -Action 'Create Folder and Move'
        $row.RelativePath = 'Document Registry/ORG-ID-Registry.xlsx'
        $row.SourceModifiedTicks = (Get-Item (Join-Path $root 'Document Registry/ORG-ID-Registry.xlsx')).LastWriteTimeUtc.Ticks
        $plan = Save-Plan -Root $root -Rows @($row)
        $r = Invoke-GovApply -PlanPath $plan -LibraryRoot $root -Baseline $script:Baseline -JournalPath (Join-Path $root 'j.jsonl') -SettleSeconds 0
        $r.Refused | Should -Be 1
        Test-Path -LiteralPath (Join-Path $root 'Document Registry/ORG-ID-Registry.xlsx') | Should -BeTrue
    }

    It 'skips a row whose source changed after planning' {
        $root = New-TestLibrary -Files @{ 'a.docx' = 'x' }
        $plan = Save-Plan -Root $root -Rows @(New-Row -Name 'a.docx' -Suggested 'Controlled Documents/Governance' -Action 'Create Folder and Move' -Ticks 1)
        $r = Invoke-GovApply -PlanPath $plan -LibraryRoot $root -Baseline $script:Baseline -JournalPath (Join-Path $root 'j.jsonl') -SettleSeconds 0
        $r.Skipped | Should -Be 1
        Test-Path -LiteralPath (Join-Path $root 'a.docx') | Should -BeTrue
    }

    It 'never overwrites an existing destination file' {
        $root = New-TestLibrary -Files @{ 'a.docx' = 'new'; 'Controlled Documents/Governance/a.docx' = 'old' }
        $ticks = (Get-Item (Join-Path $root 'a.docx')).LastWriteTimeUtc.Ticks
        $plan  = Save-Plan -Root $root -Rows @(New-Row -Name 'a.docx' -Suggested 'Controlled Documents/Governance' -Action 'Move to Existing Folder' -Ticks $ticks)
        $r = Invoke-GovApply -PlanPath $plan -LibraryRoot $root -Baseline $script:Baseline -JournalPath (Join-Path $root 'j.jsonl') -SettleSeconds 0
        $r.Skipped | Should -Be 1
        Get-Content -LiteralPath (Join-Path $root 'Controlled Documents/Governance/a.docx') -Raw | Should -BeLike 'old*'
        Test-Path -LiteralPath (Join-Path $root 'a.docx') | Should -BeTrue
    }

    It 'never reduces the total file count' {
        $root = New-TestLibrary -Files @{ 'a.docx' = 'x'; 'b.docx' = 'y'; 'c.docx' = 'z' }
        $rows = foreach ($n in 'a.docx','b.docx','c.docx') {
            New-Row -Name $n -Suggested 'Controlled Documents/Governance' -Action 'Create Folder and Move' `
                    -Ticks (Get-Item (Join-Path $root $n)).LastWriteTimeUtc.Ticks
        }
        $before = (Get-ChildItem -LiteralPath $root -Recurse -File).Count
        $plan = Save-Plan -Root $root -Rows $rows
        Invoke-GovApply -PlanPath $plan -LibraryRoot $root -Baseline $script:Baseline -JournalPath (Join-Path $root 'j.jsonl') -SettleSeconds 0 | Out-Null
        (Get-ChildItem -LiteralPath $root -Recurse -File).Count | Should -BeGreaterOrEqual $before
    }

    It 'records every completed move in the journal before performing it' {
        $root = New-TestLibrary -Files @{ 'a.docx' = 'x' }
        $ticks = (Get-Item (Join-Path $root 'a.docx')).LastWriteTimeUtc.Ticks
        $plan  = Save-Plan -Root $root -Rows @(New-Row -Name 'a.docx' -Suggested 'Controlled Documents/Governance' -Action 'Create Folder and Move' -Ticks $ticks)
        $j = Join-Path $root 'j.jsonl'
        Invoke-GovApply -PlanPath $plan -LibraryRoot $root -Baseline $script:Baseline -JournalPath $j -SettleSeconds 0 | Out-Null
        $entries = Read-GovJournal -Path $j
        $entries.Count      | Should -Be 1
        $entries[0].Source  | Should -BeLike '*a.docx'
    }

    It 'renames when the row carries a NewName' {
        $root = New-TestLibrary -Files @{ 'a.docx' = 'x' }
        $ticks = (Get-Item (Join-Path $root 'a.docx')).LastWriteTimeUtc.Ticks
        $plan  = Save-Plan -Root $root -Rows @(New-Row -Name 'a.docx' -Suggested 'Controlled Documents/Governance' -Action 'Create Folder and Move' -NewName 'ORG-GOV-DS-016 - a.docx' -Ticks $ticks)
        Invoke-GovApply -PlanPath $plan -LibraryRoot $root -Baseline $script:Baseline -JournalPath (Join-Path $root 'j.jsonl') -SettleSeconds 0 | Out-Null
        Test-Path -LiteralPath (Join-Path $root 'Controlled Documents/Governance/ORG-GOV-DS-016 - a.docx') | Should -BeTrue
    }

    It 'throws when an approved row has a blank SuggestedFolder' {
        $root = New-TestLibrary -Files @{ 'a.docx' = 'x' }
        $plan = Save-Plan -Root $root -Rows @(New-Row -Name 'a.docx' -Suggested '' -Action 'Move to Existing Folder')
        { Invoke-GovApply -PlanPath $plan -LibraryRoot $root -Baseline $script:Baseline -JournalPath (Join-Path $root 'j.jsonl') -SettleSeconds 0 } |
            Should -Throw '*blank Suggested Folder*'
    }

    It 'throws when the plan is missing a required column' {
        $root = New-TestLibrary -Files @{ 'a.docx' = 'x' }
        $p = Join-Path $root 'bad.csv'
        Set-Content -LiteralPath $p -Value "Name,Approved`na.docx,Y" -Encoding utf8NoBOM
        { Invoke-GovApply -PlanPath $p -LibraryRoot $root -Baseline $script:Baseline -JournalPath (Join-Path $root 'j.jsonl') -SettleSeconds 0 } |
            Should -Throw '*missing required column*'
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `pwsh -NoProfile -Command "Invoke-Pester -Path tests/powershell/Apply.Tests.ps1 -Output Detailed"`

Expected: FAIL. `Invoke-GovApply` is not defined.

- [ ] **Step 3: Implement apply**

Create `scripts/GovLib/Apply.ps1`:

```powershell
#Requires -Version 7.0

$script:GovPlanRequiredColumns = @(
    'Name','RelativePath','SourceModifiedTicks','SuggestedFolder',
    'ActionRequired','Approved','NewName'
)

$script:GovMoveActions = @('Move to Existing Folder', 'Create Folder and Move')

function Invoke-GovApply {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $PlanPath,
        [Parameter(Mandatory)] [string] $LibraryRoot,
        [Parameter(Mandatory)] [object] $Baseline,
        [Parameter(Mandatory)] [string] $JournalPath,
        [int] $BatchSize = 25,
        [int] $SettleSeconds = 5
    )

    if (-not (Test-Path -LiteralPath $PlanPath -PathType Leaf)) {
        throw "Plan not found: $PlanPath"
    }
    $rows = @(Import-Csv -LiteralPath $PlanPath)
    if ($rows.Count -eq 0) { throw "Plan is empty: $PlanPath" }

    $columns = $rows[0].PSObject.Properties.Name
    foreach ($required in $script:GovPlanRequiredColumns) {
        if ($columns -notcontains $required) {
            throw "Plan is missing required column '$required': $PlanPath"
        }
    }

    $approved = @($rows | Where-Object {
        -not [string]::IsNullOrWhiteSpace($_.Approved) -and
        $_.ActionRequired -in $script:GovMoveActions
    })

    foreach ($row in $approved) {
        if ([string]::IsNullOrWhiteSpace($row.SuggestedFolder)) {
            throw "Approved row '$($row.Name)' has a blank Suggested Folder. Resolve it before applying."
        }
    }

    New-GovJournal -Path $JournalPath | Out-Null

    $summary = [pscustomobject]@{
        Moved = 0; Skipped = 0; Refused = 0; Messages = @(); JournalPath = $JournalPath
    }

    # SOP order: existing-folder moves first, then create-folder-and-move.
    $ordered = @($approved | Where-Object ActionRequired -eq 'Move to Existing Folder') +
               @($approved | Where-Object ActionRequired -eq 'Create Folder and Move')

    $index = 0
    foreach ($row in $ordered) {
        $index++
        Invoke-GovApplyRow -Row $row -LibraryRoot $LibraryRoot -Baseline $Baseline `
                           -JournalPath $JournalPath -Summary $summary

        if ($SettleSeconds -gt 0 -and $index % $BatchSize -eq 0) {
            Start-Sleep -Seconds $SettleSeconds
        }
    }

    return $summary
}

function Invoke-GovApplyRow {
    param(
        [object] $Row, [string] $LibraryRoot, [object] $Baseline,
        [string] $JournalPath, [object] $Summary
    )

    $reason = Get-GovProtectionReason -RelativePath $Row.RelativePath -Baseline $Baseline
    if ($reason) {
        $Summary.Refused++
        $Summary.Messages += "REFUSED $($Row.Name): protected path. $reason"
        return
    }

    $source = Join-Path $LibraryRoot ($Row.RelativePath -replace '/', '\')
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        $Summary.Skipped++
        $Summary.Messages += "SKIPPED $($Row.Name): source no longer exists."
        return
    }

    $currentTicks = (Get-Item -LiteralPath $source).LastWriteTimeUtc.Ticks
    if ([long]$Row.SourceModifiedTicks -ne $currentTicks) {
        $Summary.Skipped++
        $Summary.Messages += "SKIPPED $($Row.Name): source changed after planning."
        return
    }

    $targetName = if ([string]::IsNullOrWhiteSpace($Row.NewName)) { $Row.Name } else { $Row.NewName }
    $targetDir  = Join-Path $LibraryRoot ($Row.SuggestedFolder -replace '/', '\')
    $target     = Join-Path $targetDir $targetName

    $targetRelative = ($Row.SuggestedFolder.TrimEnd('/') + '/' + $targetName)
    $targetReason = Get-GovProtectionReason -RelativePath $targetRelative -Baseline $Baseline
    if ($targetReason) {
        $Summary.Refused++
        $Summary.Messages += "REFUSED $($Row.Name): destination is protected. $targetReason"
        return
    }

    if (-not (Test-GovPathLength -Path $target)) {
        $Summary.Skipped++
        $Summary.Messages += "SKIPPED $($Row.Name): destination path length $($target.Length) exceeds the limit."
        return
    }

    if (Test-Path -LiteralPath $target) {
        $Summary.Skipped++
        $Summary.Messages += "SKIPPED $($Row.Name): destination already exists. Never overwriting."
        return
    }

    if (-not (Test-Path -LiteralPath $targetDir -PathType Container)) {
        New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
    }

    $action = if ($targetName -ne $Row.Name) { 'move+rename' } else { 'move' }

    # Journal first, then act. A kill between these leaves an entry with no move,
    # which undo tolerates (it flags a missing source) and which never loses a file.
    Add-GovJournalEntry -JournalPath $JournalPath -Action $action -Source $source -Destination $target

    try {
        Move-Item -LiteralPath $source -Destination $target -ErrorAction Stop
        $Summary.Moved++
    }
    catch {
        $Summary.Skipped++
        $Summary.Messages += "SKIPPED $($Row.Name): $($_.Exception.Message)"
    }
}
```

- [ ] **Step 4: Export the function**

Add `'Invoke-GovApply'` to `FunctionsToExport`.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `pwsh -NoProfile -Command "Invoke-Pester -Path tests/powershell/Apply.Tests.ps1 -Output Detailed"`

Expected: PASS, 10 tests.

- [ ] **Step 6: Commit**

```bash
git add scripts/GovLib/Apply.ps1 scripts/GovLib/GovLib.psd1 tests/powershell/Apply.Tests.ps1
git commit -m "feat: apply approved plan rows with journal-first execution"
```

---

### Task 9: Undo

Reverses a run using its journal, subject to the same invariants.

**Files:**
- Create: `scripts/GovLib/Undo.ps1`
- Create: `tests/powershell/Undo.Tests.ps1`
- Modify: `scripts/GovLib/GovLib.psd1`

**Interfaces:**
- Consumes: `Read-GovJournal` from Task 7.
- Produces: `Invoke-GovUndo -JournalPath <string>` returning a PSCustomObject with `Restored [int]`, `Flagged [int]`, `Messages [string[]]`.

- [ ] **Step 1: Write the failing tests**

Create `tests/powershell/Undo.Tests.ps1`:

```powershell
#Requires -Version 7.0
BeforeAll {
    $script:RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
    Import-Module (Join-Path $script:RepoRoot 'scripts/GovLib/GovLib.psd1') -Force
    Import-Module (Join-Path $PSScriptRoot 'TestHelpers.psm1') -Force
    $script:Baseline = Import-GovBaseline -Path (Join-Path $script:RepoRoot 'config/library-baseline.json')
}

Describe 'Invoke-GovUndo' {
    It 'restores a moved file to its original location' {
        $root = New-TestLibrary -Files @{ 'a.docx' = 'x' }
        $src  = Join-Path $root 'a.docx'
        $dst  = Join-Path $root 'Controlled Documents/Governance/a.docx'
        New-Item -ItemType Directory -Path (Split-Path -Parent $dst) -Force | Out-Null
        $j = Join-Path $root 'j.jsonl'
        New-GovJournal -Path $j | Out-Null
        Add-GovJournalEntry -JournalPath $j -Action 'move' -Source $src -Destination $dst
        Move-Item -LiteralPath $src -Destination $dst

        $r = Invoke-GovUndo -JournalPath $j
        $r.Restored | Should -Be 1
        Test-Path -LiteralPath $src | Should -BeTrue
        Test-Path -LiteralPath $dst | Should -BeFalse
    }

    It 'flags rather than overwrites when the original location is occupied' {
        $root = New-TestLibrary -Files @{ 'a.docx' = 'replacement' }
        $src  = Join-Path $root 'a.docx'
        $dst  = Join-Path $root 'moved-a.docx'
        Set-Content -LiteralPath $dst -Value 'moved' -Encoding utf8NoBOM
        $j = Join-Path $root 'j.jsonl'
        New-GovJournal -Path $j | Out-Null
        Add-GovJournalEntry -JournalPath $j -Action 'move' -Source $src -Destination $dst

        $r = Invoke-GovUndo -JournalPath $j
        $r.Flagged  | Should -Be 1
        $r.Restored | Should -Be 0
        Get-Content -LiteralPath $src -Raw | Should -BeLike 'replacement*'
    }

    It 'tolerates a journal entry whose destination never materialized' {
        $root = New-TestLibrary -Files @{}
        $j = Join-Path $root 'j.jsonl'
        New-GovJournal -Path $j | Out-Null
        Add-GovJournalEntry -JournalPath $j -Action 'move' -Source (Join-Path $root 'a.docx') -Destination (Join-Path $root 'X/a.docx')
        $r = Invoke-GovUndo -JournalPath $j
        $r.Flagged | Should -Be 1
    }

    It 'reverses entries in reverse order' {
        $root = New-TestLibrary -Files @{ 'a.docx' = 'x' }
        $j = Join-Path $root 'j.jsonl'
        New-GovJournal -Path $j | Out-Null
        $one = Join-Path $root 'a.docx'
        $two = Join-Path $root 'b.docx'
        $three = Join-Path $root 'c.docx'
        Add-GovJournalEntry -JournalPath $j -Action 'move' -Source $one -Destination $two
        Move-Item -LiteralPath $one -Destination $two
        Add-GovJournalEntry -JournalPath $j -Action 'move' -Source $two -Destination $three
        Move-Item -LiteralPath $two -Destination $three

        $r = Invoke-GovUndo -JournalPath $j
        $r.Restored | Should -Be 2
        Test-Path -LiteralPath $one | Should -BeTrue
    }

    It 'never deletes a file when reversing an entry' {
        # The journal says a.docx arrived from zzz.docx. Undo must restore the
        # name, never remove the file. The docx count must not drop.
        $root = New-TestLibrary -Files @{ 'a.docx' = 'x' }
        $j = Join-Path $root 'j.jsonl'
        New-GovJournal -Path $j | Out-Null
        Add-GovJournalEntry -JournalPath $j -Action 'move' -Source (Join-Path $root 'zzz.docx') -Destination (Join-Path $root 'a.docx')
        Invoke-GovUndo -JournalPath $j | Out-Null
        @(Get-ChildItem -LiteralPath $root -File -Recurse | Where-Object Extension -eq '.docx').Count |
            Should -Be 1
        Test-Path -LiteralPath (Join-Path $root 'zzz.docx') | Should -BeTrue
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `pwsh -NoProfile -Command "Invoke-Pester -Path tests/powershell/Undo.Tests.ps1 -Output Detailed"`

Expected: FAIL. `Invoke-GovUndo` is not defined.

- [ ] **Step 3: Implement undo**

Create `scripts/GovLib/Undo.ps1`:

```powershell
#Requires -Version 7.0

function Invoke-GovUndo {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [string] $JournalPath)

    $entries = @(Read-GovJournal -Path $JournalPath)
    [array]::Reverse($entries)

    $summary = [pscustomobject]@{ Restored = 0; Flagged = 0; Messages = @() }

    foreach ($entry in $entries) {
        if (-not (Test-Path -LiteralPath $entry.Destination -PathType Leaf)) {
            $summary.Flagged++
            $summary.Messages += "FLAGGED: '$($entry.Destination)' is not present. Nothing to reverse."
            continue
        }
        if (Test-Path -LiteralPath $entry.Source) {
            $summary.Flagged++
            $summary.Messages += "FLAGGED: original location '$($entry.Source)' is occupied. Never overwriting."
            continue
        }

        $dir = Split-Path -Parent $entry.Source
        if ($dir -and -not (Test-Path -LiteralPath $dir -PathType Container)) {
            New-Item -ItemType Directory -Path $dir -Force | Out-Null
        }

        try {
            Move-Item -LiteralPath $entry.Destination -Destination $entry.Source -ErrorAction Stop
            $summary.Restored++
        }
        catch {
            $summary.Flagged++
            $summary.Messages += "FLAGGED: $($entry.Destination). $($_.Exception.Message)"
        }
    }

    return $summary
}
```

- [ ] **Step 4: Export the function**

Add `'Invoke-GovUndo'` to `FunctionsToExport`.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `pwsh -NoProfile -Command "Invoke-Pester -Path tests/powershell/Undo.Tests.ps1 -Output Detailed"`

Expected: PASS, 5 tests.

- [ ] **Step 6: Commit**

```bash
git add scripts/GovLib/Undo.ps1 scripts/GovLib/GovLib.psd1 tests/powershell/Undo.Tests.ps1
git commit -m "feat: undo a run by reversing its journal without deleting"
```

---

### Task 10: Verify

Re-inventories after a run and diffs against the plan.

**Files:**
- Create: `scripts/GovLib/Verify.ps1`
- Create: `tests/powershell/Verify.Tests.ps1`
- Modify: `scripts/GovLib/GovLib.psd1`

**Interfaces:**
- Consumes: `Get-GovInventory` from Task 5.
- Produces: `Invoke-GovVerify -PlanPath <string> -LibraryRoot <string>` returning a PSCustomObject with `Landed [int]`, `Missing [string[]]`, `Ok [bool]`.

  Owner rulings 2026-08-01, superseding the code shown below in Step 3:

  1. An `Unexpected` property was originally specified as an always-empty
     array. It was removed. A field named for a check that never runs reports
     an unearned assurance to anything reading `Unexpected.Count -eq 0`.
  2. A blank `SuggestedFolder` skips the row only when the row is ALSO
     unapproved. An approved row with a blank `SuggestedFolder` is the exact
     condition `Invoke-GovApply` treats as fatal, so Verify must not skip it:
     it contributes a descriptive entry to `Missing` and forces `Ok` false.
     Skipping it let Verify report a green run for a batch Apply refuses to
     process, which is the worst failure this function can have.

- [ ] **Step 1: Write the failing tests**

Create `tests/powershell/Verify.Tests.ps1`:

```powershell
#Requires -Version 7.0
BeforeAll {
    $script:RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
    Import-Module (Join-Path $script:RepoRoot 'scripts/GovLib/GovLib.psd1') -Force
    Import-Module (Join-Path $PSScriptRoot 'TestHelpers.psm1') -Force

    function Save-VerifyPlan {
        param([object[]] $Rows, [string] $Root)
        $p = Join-Path $Root 'plan.csv'
        $Rows | Export-Csv -LiteralPath $p -NoTypeInformation -Encoding utf8NoBOM
        return $p
    }
    function New-VerifyRow {
        param([string] $Name, [string] $Suggested, [string] $Approved = 'Y', [string] $NewName = '')
        [pscustomobject]@{
            Name = $Name; RelativePath = $Name; SourceModifiedTicks = 0
            SuggestedFolder = $Suggested; ActionRequired = 'Move to Existing Folder'
            Approved = $Approved; NewName = $NewName
        }
    }
}

Describe 'Invoke-GovVerify' {
    It 'reports Ok when every approved row landed' {
        $root = New-TestLibrary -Files @{ 'Controlled Documents/Governance/a.docx' = 'x' }
        $plan = Save-VerifyPlan -Root $root -Rows @(New-VerifyRow -Name 'a.docx' -Suggested 'Controlled Documents/Governance')
        $r = Invoke-GovVerify -PlanPath $plan -LibraryRoot $root
        $r.Ok      | Should -BeTrue
        $r.Landed  | Should -Be 1
        $r.Missing | Should -BeNullOrEmpty
    }

    It 'reports a missing file when an approved row did not land' {
        $root = New-TestLibrary -Files @{ 'a.docx' = 'x' }
        $plan = Save-VerifyPlan -Root $root -Rows @(New-VerifyRow -Name 'a.docx' -Suggested 'Controlled Documents/Governance')
        $r = Invoke-GovVerify -PlanPath $plan -LibraryRoot $root
        $r.Ok      | Should -BeFalse
        $r.Missing | Should -Contain 'Controlled Documents/Governance/a.docx'
    }

    It 'accounts for a renamed destination' {
        $root = New-TestLibrary -Files @{ 'Controlled Documents/Governance/ORG-GOV-DS-016 - a.docx' = 'x' }
        $plan = Save-VerifyPlan -Root $root -Rows @(New-VerifyRow -Name 'a.docx' -Suggested 'Controlled Documents/Governance' -NewName 'ORG-GOV-DS-016 - a.docx')
        (Invoke-GovVerify -PlanPath $plan -LibraryRoot $root).Ok | Should -BeTrue
    }

    It 'ignores rows that were never approved' {
        $root = New-TestLibrary -Files @{ 'a.docx' = 'x' }
        $plan = Save-VerifyPlan -Root $root -Rows @(New-VerifyRow -Name 'a.docx' -Suggested 'Controlled Documents/Governance' -Approved '')
        (Invoke-GovVerify -PlanPath $plan -LibraryRoot $root).Ok | Should -BeTrue
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `pwsh -NoProfile -Command "Invoke-Pester -Path tests/powershell/Verify.Tests.ps1 -Output Detailed"`

Expected: FAIL. `Invoke-GovVerify` is not defined.

- [ ] **Step 3: Implement verify**

Create `scripts/GovLib/Verify.ps1`:

```powershell
#Requires -Version 7.0

function Invoke-GovVerify {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $PlanPath,
        [Parameter(Mandatory)] [string] $LibraryRoot
    )

    if (-not (Test-Path -LiteralPath $PlanPath -PathType Leaf)) {
        throw "Plan not found: $PlanPath"
    }

    $rows      = @(Import-Csv -LiteralPath $PlanPath)
    $inventory = @(Get-GovInventory -LibraryRoot $LibraryRoot)
    $present   = @{}
    foreach ($item in $inventory) { $present[$item.RelativePath.ToLowerInvariant()] = $true }

    $landed  = 0
    $missing = @()

    foreach ($row in $rows) {
        if ([string]::IsNullOrWhiteSpace($row.Approved)) { continue }
        if ([string]::IsNullOrWhiteSpace($row.SuggestedFolder)) { continue }

        $name     = if ([string]::IsNullOrWhiteSpace($row.NewName)) { $row.Name } else { $row.NewName }
        $expected = ($row.SuggestedFolder.TrimEnd('/') + '/' + $name)

        if ($present.ContainsKey($expected.ToLowerInvariant())) { $landed++ }
        else { $missing += $expected }
    }

    return [pscustomobject]@{
        Landed  = $landed
        Missing = $missing
        Ok      = ($missing.Count -eq 0)
    }
}
```

- [ ] **Step 4: Export the function**

Add `'Invoke-GovVerify'` to `FunctionsToExport`.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `pwsh -NoProfile -Command "Invoke-Pester -Path tests/powershell/Verify.Tests.ps1 -Output Detailed"`

Expected: PASS, 4 tests.

- [ ] **Step 6: Commit**

```bash
git add scripts/GovLib/Verify.ps1 scripts/GovLib/GovLib.psd1 tests/powershell/Verify.Tests.ps1
git commit -m "feat: verify a run by diffing the library against its plan"
```

---

### Task 11: Full-library drift report

Read-only pass over the entire library. Must not change a single byte.

**Files:**
- Create: `scripts/GovLib/Report.ps1`
- Create: `tests/powershell/Report.Tests.ps1`
- Modify: `scripts/GovLib/GovLib.psd1`

**Interfaces:**
- Consumes: `Get-GovInventory`, `Resolve-GovIdentity`, `Get-GovPlacementTarget`, `Get-GovProtectionReason`.
- Produces: `New-GovReport -LibraryRoot <string> -Baseline <object> [-Registry <hashtable>] [-ArtifactTypes <hashtable>] [-RepositoryTargetsPath <string>]` returning a PSCustomObject with `MissingFolders [string[]]`, `UnknownFolders [string[]]`, `Misplaced [object[]]`, `UnregisteredIds [string[]]`, `RegistryRowsWithNoFile [string[]]`, `TargetDivergence [object[]]`, `ArchiveFolderName [string]`, `Markdown [string]`.

- [ ] **Step 1: Write the failing tests**

Create `tests/powershell/Report.Tests.ps1`:

```powershell
#Requires -Version 7.0
BeforeAll {
    $script:RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
    Import-Module (Join-Path $script:RepoRoot 'scripts/GovLib/GovLib.psd1') -Force
    Import-Module (Join-Path $PSScriptRoot 'TestHelpers.psm1') -Force
    $script:Baseline = Import-GovBaseline      -Path (Join-Path $script:RepoRoot 'config/library-baseline.json')
    $script:Types    = Import-GovArtifactTypes -Path (Join-Path $script:RepoRoot 'config/artifact-types.json')
    $script:Registry = Import-GovRegistry      -Path (Join-Path $script:RepoRoot 'fixtures/registry-sample.csv')
}

Describe 'New-GovReport' {
    It 'changes nothing in the library' {
        $root = New-TestLibrary -Files @{
            'a.docx' = 'x'
            'Controlled Documents/Governance/b.docx' = 'y'
        }
        $before = Get-TreeSnapshot -Root $root
        New-GovReport -LibraryRoot $root -Baseline $script:Baseline | Out-Null
        $after = Get-TreeSnapshot -Root $root
        ($after -join '|') | Should -Be ($before -join '|')
    }

    It 'lists baseline folders that are missing' {
        $root = New-TestLibrary -Files @{ 'a.docx' = 'x' }
        (New-GovReport -LibraryRoot $root -Baseline $script:Baseline).MissingFolders |
            Should -Contain 'Controlled Documents/Governance'
    }

    It 'lists folders present that are absent from the baseline' {
        $root = New-TestLibrary -Files @{ 'Some Random Folder/a.docx' = 'x' }
        (New-GovReport -LibraryRoot $root -Baseline $script:Baseline).UnknownFolders |
            Should -Contain 'Some Random Folder'
    }

    It 'flags a file whose ID disagrees with the folder it occupies' {
        # ORG-GOV-DS-016 is Draft, so it belongs in Master Development Workspace.
        $root = New-TestLibrary -Files @{ 'Controlled Documents/Risk/ORG-GOV-DS-016 - a.docx' = 'x' }
        $r = New-GovReport -LibraryRoot $root -Baseline $script:Baseline `
                           -Registry $script:Registry -ArtifactTypes $script:Types
        @($r.Misplaced).Count | Should -BeGreaterThan 0
        $r.Misplaced[0].Expected | Should -Be 'Master Development Workspace'
    }

    It 'reports registry rows that have no corresponding file' {
        $root = New-TestLibrary -Files @{ 'a.docx' = 'x' }
        $r = New-GovReport -LibraryRoot $root -Baseline $script:Baseline `
                           -Registry $script:Registry -ArtifactTypes $script:Types
        $r.RegistryRowsWithNoFile | Should -Contain 'ORG-GOV-DS-016'
    }

    It 'reports divergence against repository-targets.json' {
        $root = New-TestLibrary -Files @{ 'a.docx' = 'x' }
        $r = New-GovReport -LibraryRoot $root -Baseline $script:Baseline `
                           -RepositoryTargetsPath (Join-Path $script:RepoRoot 'config/repository-targets.json')
        ($r.TargetDivergence | Where-Object Category -eq 'OPS') | Should -Not -BeNullOrEmpty
    }

    It 'resolves the archive folder name empirically' {
        $root = New-TestLibrary -Files @{ '_Archive/old.docx' = 'x' }
        (New-GovReport -LibraryRoot $root -Baseline $script:Baseline).ArchiveFolderName | Should -Be '_Archive'
    }

    It 'produces markdown output' {
        $root = New-TestLibrary -Files @{ 'a.docx' = 'x' }
        (New-GovReport -LibraryRoot $root -Baseline $script:Baseline).Markdown |
            Should -BeLike '*Governance Library Drift Report*'
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `pwsh -NoProfile -Command "Invoke-Pester -Path tests/powershell/Report.Tests.ps1 -Output Detailed"`

Expected: FAIL. `New-GovReport` is not defined.

- [ ] **Step 3: Implement the report**

Create `scripts/GovLib/Report.ps1`:

```powershell
#Requires -Version 7.0

function New-GovReport {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $LibraryRoot,
        [Parameter(Mandatory)] [object] $Baseline,
        [hashtable] $Registry,
        [hashtable] $ArtifactTypes,
        [string]    $RepositoryTargetsPath
    )

    $inventory = @(Get-GovInventory -LibraryRoot $LibraryRoot)

    $actualFolders = @(Get-ChildItem -LiteralPath $LibraryRoot -Recurse -Directory -Force |
        ForEach-Object {
            $_.FullName.Substring(([System.IO.Path]::GetFullPath($LibraryRoot).TrimEnd('\','/')).Length).TrimStart('\','/') -replace '\\', '/'
        })

    $baselinePaths = @($Baseline.folderTree | Where-Object { $_.createIfEmpty } | ForEach-Object { $_.path })
    $missingFolders = @($baselinePaths | Where-Object { $_ -notin $actualFolders })

    $allBaselinePaths = @($Baseline.folderTree | ForEach-Object { $_.path })
    $unknownFolders = @($actualFolders | Where-Object {
        $folder = $_
        -not ($allBaselinePaths | Where-Object { $folder -eq $_ -or $folder.StartsWith("$_/") })
    })

    $archiveName = $null
    foreach ($candidate in @('Archive', '_Archive')) {
        if ($actualFolders -contains $candidate) { $archiveName = $candidate; break }
    }

    $misplaced   = @()
    $unregistered = @()
    $seenIds     = @{}

    if ($Registry -and $ArtifactTypes) {
        foreach ($item in $inventory) {
            $identity = Resolve-GovIdentity -Name $item.Name -Registry $Registry -ArtifactTypes $ArtifactTypes
            if (-not $identity.DocumentId) { continue }
            if (-not $identity.Resolved) {
                if ($identity.Reason -like '*not in the ID Registry*') { $unregistered += $identity.DocumentId }
                continue
            }
            $seenIds[$identity.DocumentId] = $true

            $placement = Get-GovPlacementTarget -Baseline $Baseline `
                             -ApprovalStatus $identity.ApprovalStatus -Category $identity.Category
            if (-not $placement -or -not $placement.TargetPath) { continue }
            if (Get-GovProtectionReason -RelativePath $item.RelativePath -Baseline $Baseline) { continue }

            if ($item.FolderPath -ne $placement.TargetPath) {
                $misplaced += [pscustomobject]@{
                    DocumentId = $identity.DocumentId
                    Actual     = $item.FolderPath
                    Expected   = $placement.TargetPath
                    RuleSource = $placement.RuleSource
                }
            }
        }
    }

    $registryRowsWithNoFile = @()
    if ($Registry) {
        $registryRowsWithNoFile = @($Registry.Keys | Where-Object { -not $seenIds.ContainsKey($_) } | Sort-Object)
    }

    $divergence = @()
    if ($RepositoryTargetsPath -and (Test-Path -LiteralPath $RepositoryTargetsPath -PathType Leaf)) {
        $targets = (Get-Content -LiteralPath $RepositoryTargetsPath -Raw | ConvertFrom-Json).targetsByCategory
        foreach ($property in $targets.PSObject.Properties) {
            $category = $property.Name
            $mapping = $Baseline.placementRules.mappings | Where-Object {
                $_.match.PSObject.Properties.Name -contains 'idCategory' -and
                $_.match.idCategory -eq $category -and $_.match.approvalStatus -eq 'Approved'
            } | Select-Object -First 1
            if ($mapping -and $mapping.targetPath -ne $property.Value) {
                $divergence += [pscustomobject]@{
                    Category         = $category
                    EngineTarget     = $property.Value
                    BaselineTarget   = $mapping.targetPath
                }
            }
        }
    }

    $markdown = New-GovReportMarkdown -LibraryRoot $LibraryRoot -MissingFolders $missingFolders `
                    -UnknownFolders $unknownFolders -Misplaced $misplaced `
                    -UnregisteredIds $unregistered -RegistryRowsWithNoFile $registryRowsWithNoFile `
                    -TargetDivergence $divergence -ArchiveFolderName $archiveName

    return [pscustomobject]@{
        MissingFolders         = $missingFolders
        UnknownFolders         = $unknownFolders
        Misplaced              = $misplaced
        UnregisteredIds        = @($unregistered | Sort-Object -Unique)
        RegistryRowsWithNoFile = $registryRowsWithNoFile
        TargetDivergence       = $divergence
        ArchiveFolderName      = $archiveName
        Markdown               = $markdown
    }
}

function New-GovReportMarkdown {
    param(
        [string] $LibraryRoot, [string[]] $MissingFolders, [string[]] $UnknownFolders,
        [object[]] $Misplaced, [string[]] $UnregisteredIds, [string[]] $RegistryRowsWithNoFile,
        [object[]] $TargetDivergence, [string] $ArchiveFolderName
    )

    $lines = @()
    $lines += '# Governance Library Drift Report'
    $lines += ''
    $lines += "Library root: $LibraryRoot"
    $lines += ''
    $lines += 'This report is read-only. It proposes no moves.'
    $lines += ''

    $lines += '## Missing baseline folders'
    $lines += ''
    if ($MissingFolders) { $lines += ($MissingFolders | ForEach-Object { "- $_" }) }
    else { $lines += '- None' }
    $lines += ''

    $lines += '## Folders not in the baseline'
    $lines += ''
    if ($UnknownFolders) { $lines += ($UnknownFolders | ForEach-Object { "- $_" }) }
    else { $lines += '- None' }
    $lines += ''

    $lines += '## Files whose folder disagrees with their registry status'
    $lines += ''
    if ($Misplaced) {
        $lines += '| Document ID | Actual | Expected | Rule source |'
        $lines += '|---|---|---|---|'
        $lines += ($Misplaced | ForEach-Object { "| $($_.DocumentId) | $($_.Actual) | $($_.Expected) | $($_.RuleSource) |" })
    }
    else { $lines += '- None' }
    $lines += ''

    $lines += '## Document IDs in filenames that are not in the registry'
    $lines += ''
    if ($UnregisteredIds) { $lines += ($UnregisteredIds | ForEach-Object { "- $_" }) }
    else { $lines += '- None' }
    $lines += ''

    $lines += '## Registry rows with no corresponding file'
    $lines += ''
    if ($RegistryRowsWithNoFile) { $lines += ($RegistryRowsWithNoFile | ForEach-Object { "- $_" }) }
    else { $lines += '- None' }
    $lines += ''

    $lines += '## Divergence against repository-targets.json'
    $lines += ''
    if ($TargetDivergence) {
        $lines += '| Category | Engine target | Baseline target |'
        $lines += '|---|---|---|'
        $lines += ($TargetDivergence | ForEach-Object { "| $($_.Category) | $($_.EngineTarget) | $($_.BaselineTarget) |" })
    }
    else { $lines += '- None' }
    $lines += ''

    $lines += '## Archive folder name'
    $lines += ''
    $lines += if ($ArchiveFolderName) { "Resolved empirically as '$ArchiveFolderName'." }
              else { 'Neither Archive nor _Archive is present.' }
    $lines += ''

    return ($lines -join "`n")
}
```

- [ ] **Step 4: Export the functions**

Add `'New-GovReport'` to `FunctionsToExport`.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `pwsh -NoProfile -Command "Invoke-Pester -Path tests/powershell/Report.Tests.ps1 -Output Detailed"`

Expected: PASS, 8 tests.

- [ ] **Step 6: Commit**

```bash
git add scripts/GovLib/Report.ps1 scripts/GovLib/GovLib.psd1 tests/powershell/Report.Tests.ps1
git commit -m "feat: read-only full-library drift report"
```

---

### Task 12: Entry point script

Six modes, the folder picker, and the OutputRoot containment refusal.

**Files:**
- Create: `scripts/normalize-governance-library.ps1`
- Create: `tests/powershell/EntryPoint.Tests.ps1`

**Interfaces:**
- Consumes: every exported `GovLib` function.
- Produces: a script with parameters `-Mode Inventory|Plan|Apply|Verify|Report|Undo`, `-LibraryRoot <string>`, `-OutputRoot <string>`, `-RegistryPath <string>`, `-SharePointExport <string>`, `-PlanPath <string>`, `-JournalPath <string>`, `-TagMode`, `-TagValue`, `-TagPosition`, `-TagScope`, `-BrowseForFolder [switch]`. Exit 0 on success, 1 on failure.

- [ ] **Step 1: Write the failing tests**

Create `tests/powershell/EntryPoint.Tests.ps1`:

```powershell
#Requires -Version 7.0
BeforeAll {
    $script:RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
    $script:Script   = Join-Path $script:RepoRoot 'scripts/normalize-governance-library.ps1'
    Import-Module (Join-Path $PSScriptRoot 'TestHelpers.psm1') -Force
}

Describe 'normalize-governance-library.ps1' {
    It 'refuses an OutputRoot inside the library root' {
        $root = New-TestLibrary -Files @{ 'a.docx' = 'x' }
        $out  = Join-Path $root 'runs'
        { & $script:Script -Mode Inventory -LibraryRoot $root -OutputRoot $out } |
            Should -Throw '*inside the library*'
    }

    It 'writes an inventory csv in Inventory mode' {
        $root = New-TestLibrary -Files @{ 'a.docx' = 'x' }
        $out  = New-TestRoot
        & $script:Script -Mode Inventory -LibraryRoot $root -OutputRoot $out
        $LASTEXITCODE | Should -Be 0
        @(Get-ChildItem -LiteralPath $out -Recurse -Filter 'inventory-*.csv').Count | Should -BeGreaterThan 0
    }

    It 'writes a plan csv in Plan mode' {
        $root = New-TestLibrary -Files @{ 'a.docx' = 'x' }
        $out  = New-TestRoot
        & $script:Script -Mode Plan -LibraryRoot $root -OutputRoot $out
        @(Get-ChildItem -LiteralPath $out -Recurse -Filter 'GOV-ROOT-NORMALIZATION-RUN-*.csv').Count |
            Should -BeGreaterThan 0
    }

    It 'writes a markdown report in Report mode' {
        $root = New-TestLibrary -Files @{ 'a.docx' = 'x' }
        $out  = New-TestRoot
        & $script:Script -Mode Report -LibraryRoot $root -OutputRoot $out
        @(Get-ChildItem -LiteralPath $out -Recurse -Filter 'drift-report-*.md').Count | Should -BeGreaterThan 0
    }

    It 'leaves the library unchanged in Report mode' {
        $root   = New-TestLibrary -Files @{ 'a.docx' = 'x'; 'Sub/b.docx' = 'y' }
        $before = Get-TreeSnapshot -Root $root
        & $script:Script -Mode Report -LibraryRoot $root -OutputRoot (New-TestRoot)
        (Get-TreeSnapshot -Root $root) -join '|' | Should -Be ($before -join '|')
    }

    It 'throws when the library root does not exist' {
        { & $script:Script -Mode Inventory -LibraryRoot 'C:\nope\missing' -OutputRoot (New-TestRoot) } |
            Should -Throw '*not found*'
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `pwsh -NoProfile -Command "Invoke-Pester -Path tests/powershell/EntryPoint.Tests.ps1 -Output Detailed"`

Expected: FAIL. The script does not exist.

- [ ] **Step 3: Implement the entry point**

Create `scripts/normalize-governance-library.ps1`:

```powershell
#Requires -Version 7.0
<#
.SYNOPSIS
    Normalize the root of a locally synced ORG governance SharePoint library
    against config/library-baseline.json, and report drift across the rest.

.DESCRIPTION
    Six modes, matching the Governance Root Folder Normalization SOP.

      Inventory  read-only sweep of the library, writes inventory-<stamp>.csv
      Plan       proposes dispositions, writes GOV-ROOT-NORMALIZATION-RUN-<stamp>.csv
      Apply      executes only rows a human marked Approved in that file
      Verify     diffs the library against the plan after a run
      Report     read-only drift report over the whole library
      Undo       reverses a run using its journal

    Nothing moves without an approved plan. Nothing is ever deleted.

.EXAMPLE
    ./scripts/normalize-governance-library.ps1 -Mode Report -BrowseForFolder -OutputRoot C:\gov-runs

.EXAMPLE
    ./scripts/normalize-governance-library.ps1 -Mode Plan -LibraryRoot 'C:\Users\me\ORG\Governance' `
        -RegistryPath .\registry.csv -OutputRoot C:\gov-runs
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('Inventory','Plan','Apply','Verify','Report','Undo')]
    [string] $Mode,

    [string] $LibraryRoot,
    [string] $OutputRoot,
    [string] $RegistryPath,
    [string] $SharePointExport,
    [string] $PlanPath,
    [string] $JournalPath,
    [string] $BaselinePath,

    [ValidateSet('None','DocumentId','Custom')] [string] $TagMode = 'None',
    [string] $TagValue,
    [ValidateSet('Prefix','Suffix')] [string] $TagPosition = 'Prefix',
    [string] $TagScope,

    [int]    $BatchSize = 25,
    [int]    $SettleSeconds = 5,
    [switch] $BrowseForFolder
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
Import-Module (Join-Path $PSScriptRoot 'GovLib/GovLib.psd1') -Force

if (-not $BaselinePath) { $BaselinePath = Join-Path $repoRoot 'config/library-baseline.json' }

function Select-GovFolder {
    Add-Type -AssemblyName System.Windows.Forms
    $dialog = [System.Windows.Forms.FolderBrowserDialog]::new()
    $dialog.Description = 'Select the locally synced governance library root'
    if ($dialog.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) {
        throw 'No folder selected.'
    }
    return $dialog.SelectedPath
}

if ($BrowseForFolder) { $LibraryRoot = Select-GovFolder }

if ($Mode -ne 'Undo') {
    if (-not $LibraryRoot) { throw "LibraryRoot is required for mode '$Mode'. Pass -LibraryRoot or -BrowseForFolder." }
    if (-not (Test-Path -LiteralPath $LibraryRoot -PathType Container)) {
        throw "Library root not found: $LibraryRoot"
    }
}

if (-not $OutputRoot) { $OutputRoot = Join-Path $repoRoot 'gov-runs' }
if ($LibraryRoot -and (Test-GovPathUnderRoot -Path $OutputRoot -Root $LibraryRoot)) {
    throw "OutputRoot '$OutputRoot' is inside the library root. Run artifacts must not sync into SharePoint. Choose a location outside the library."
}

$stamp   = (Get-Date).ToUniversalTime().ToString('yyyyMMdd-HHmmss')
$runDir  = Join-Path (Join-Path $OutputRoot 'runs') $stamp
New-Item -ItemType Directory -Path $runDir -Force | Out-Null

$baseline      = Import-GovBaseline -Path $BaselinePath
$registry      = if ($RegistryPath) { Import-GovRegistry -Path $RegistryPath } else { $null }
$artifactTypes = if ($RegistryPath) { Import-GovArtifactTypes -Path (Join-Path $repoRoot 'config/artifact-types.json') } else { $null }

switch ($Mode) {

    'Inventory' {
        $inventory = @(Get-GovInventory -LibraryRoot $LibraryRoot)
        if ($SharePointExport) {
            $joined = Join-GovSharePointExport -Inventory $inventory -ExportPath $SharePointExport
            $inventory = $joined.Rows
            Write-Host "Only in SharePoint export: $($joined.OnlyInExport.Count)"
            Write-Host "Only on disk:             $($joined.OnlyOnDisk.Count)"
        }
        $out = Join-Path $runDir "inventory-$stamp.csv"
        $inventory | Export-Csv -LiteralPath $out -NoTypeInformation -Encoding utf8NoBOM
        Write-Host "Inventory written: $out ($($inventory.Count) files)"
    }

    'Plan' {
        $inventory = @(Get-GovInventory -LibraryRoot $LibraryRoot)
        if ($SharePointExport) {
            $inventory = (Join-GovSharePointExport -Inventory $inventory -ExportPath $SharePointExport).Rows
        }
        $plan = @(New-GovPlan -Inventory $inventory -Baseline $baseline -LibraryRoot $LibraryRoot `
                              -Registry $registry -ArtifactTypes $artifactTypes `
                              -TagMode $TagMode -TagValue $TagValue -TagPosition $TagPosition -TagScope $TagScope)
        $out = Join-Path $runDir "GOV-ROOT-NORMALIZATION-RUN-$stamp.csv"
        $plan | Export-Csv -LiteralPath $out -NoTypeInformation -Encoding utf8NoBOM
        Write-Host "Plan written: $out ($($plan.Count) rows)"
        Write-Host 'Nothing has moved. Review the plan, fill the Approved column, then run -Mode Apply.'
    }

    'Apply' {
        if (-not $PlanPath) { throw 'PlanPath is required for Apply.' }
        if (-not $JournalPath) { $JournalPath = Join-Path $runDir "undo-manifest-$stamp.jsonl" }
        $result = Invoke-GovApply -PlanPath $PlanPath -LibraryRoot $LibraryRoot -Baseline $baseline `
                                  -JournalPath $JournalPath -BatchSize $BatchSize -SettleSeconds $SettleSeconds
        $result.Messages | ForEach-Object { Write-Host $_ }
        Write-Host "Moved $($result.Moved), skipped $($result.Skipped), refused $($result.Refused)."
        Write-Host "Undo journal: $($result.JournalPath)"
    }

    'Verify' {
        if (-not $PlanPath) { throw 'PlanPath is required for Verify.' }
        $result = Invoke-GovVerify -PlanPath $PlanPath -LibraryRoot $LibraryRoot
        $result.Missing | ForEach-Object { Write-Host "MISSING: $_" }
        Write-Host "Landed $($result.Landed). Ok: $($result.Ok)"
        if (-not $result.Ok) { exit 1 }
    }

    'Report' {
        $report = New-GovReport -LibraryRoot $LibraryRoot -Baseline $baseline `
                                -Registry $registry -ArtifactTypes $artifactTypes `
                                -RepositoryTargetsPath (Join-Path $repoRoot 'config/repository-targets.json')
        $md  = Join-Path $runDir "drift-report-$stamp.md"
        $csv = Join-Path $runDir "drift-misplaced-$stamp.csv"
        Set-Content -LiteralPath $md -Value $report.Markdown -Encoding utf8NoBOM
        @($report.Misplaced) | Export-Csv -LiteralPath $csv -NoTypeInformation -Encoding utf8NoBOM
        Write-Host "Report written: $md"
    }

    'Undo' {
        if (-not $JournalPath) { throw 'JournalPath is required for Undo.' }
        $result = Invoke-GovUndo -JournalPath $JournalPath
        $result.Messages | ForEach-Object { Write-Host $_ }
        Write-Host "Restored $($result.Restored), flagged $($result.Flagged)."
    }
}

exit 0
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `pwsh -NoProfile -Command "Invoke-Pester -Path tests/powershell/EntryPoint.Tests.ps1 -Output Detailed"`

Expected: PASS, 6 tests.

- [ ] **Step 5: Run the whole suite**

Run: `pwsh -NoProfile -Command "Invoke-Pester -Path tests/powershell -Output Detailed"`

Expected: PASS, all tests across all files.

- [ ] **Step 6: Run the hygiene normalizer if any non-ASCII slipped in**

Run: `pwsh -NoProfile -Command "Select-String -Path scripts/GovLib/*.ps1, scripts/normalize-governance-library.ps1, config/library-baseline.json -Pattern '[^\x00-\x7F]'"`

Expected: no output. If there is output, run `./scripts/normalize-hygiene.ps1 <the file>` and re-run.

- [ ] **Step 7: Commit**

```bash
git add scripts/normalize-governance-library.ps1 tests/powershell/EntryPoint.Tests.ps1
git commit -m "feat: normalize-governance-library entry point with six modes"
```

---

### Task 13: Sync hazards

Closes the three items in spec section 8 that Task 8 left out: locked-file retry with backoff, conflict-copy detection, and the best-effort sync-state pre-flight warning. Separated from Task 8 because a reviewer could reasonably accept the core apply loop and reject the heuristics here.

**Files:**
- Create: `scripts/GovLib/Sync.ps1`
- Modify: `scripts/GovLib/Apply.ps1`
- Modify: `scripts/GovLib/GovLib.psd1`
- Create: `tests/powershell/Sync.Tests.ps1`

**Interfaces:**
- Consumes: `Invoke-GovApply` from Task 8.
- Produces:
  - `Test-GovConflictCopy -Name <string>` returns `[bool]`.
  - `Test-GovSyncQuiet -LibraryRoot <string> [-WithinSeconds <int>]` returns a PSCustomObject with `Quiet [bool]`, `TempFiles [string[]]`, `RecentlyModified [string[]]`.
  - `Invoke-GovApply` gains two optional parameters: `-RetryCount <int>` (default 2) and `-RetryDelayMs <int>` (default 500).

- [ ] **Step 1: Write the failing tests**

Create `tests/powershell/Sync.Tests.ps1`:

```powershell
#Requires -Version 7.0
BeforeAll {
    $script:RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
    Import-Module (Join-Path $script:RepoRoot 'scripts/GovLib/GovLib.psd1') -Force
    Import-Module (Join-Path $PSScriptRoot 'TestHelpers.psm1') -Force
    $script:Baseline = Import-GovBaseline -Path (Join-Path $script:RepoRoot 'config/library-baseline.json')

    function Save-SyncPlan {
        param([object[]] $Rows, [string] $Root)
        $p = Join-Path $Root 'plan.csv'
        $Rows | Export-Csv -LiteralPath $p -NoTypeInformation -Encoding utf8NoBOM
        return $p
    }
    function New-SyncRow {
        param([string] $Name, [long] $Ticks)
        [pscustomobject]@{
            Name = $Name; FolderPath = ''; RelativePath = $Name; Modified = ''
            SourceModifiedTicks = $Ticks; ModifiedBy = ''; FileType = 'docx'
            SuggestedFolder = 'Controlled Documents/Governance'; FolderExists = $false
            ActionRequired = 'Create Folder and Move'; RuleSource = 't'; Notes = ''
            Approved = 'Y'; Prefix = ''; Suffix = ''; NewName = ''
        }
    }
}

Describe 'Test-GovConflictCopy' {
    It 'detects a numbered duplicate' {
        Test-GovConflictCopy -Name 'Report (1).docx' | Should -BeTrue
    }
    It 'detects a machine-name conflict copy' {
        Test-GovConflictCopy -Name 'Report-DESKTOP-A1B2C3D.docx' | Should -BeTrue
    }
    It 'detects a named conflicted copy' {
        Test-GovConflictCopy -Name "Report (Jane's conflicted copy 2026-07-14).docx" | Should -BeTrue
    }
    It 'detects a Copy suffix' {
        Test-GovConflictCopy -Name 'Report - Copy.docx' | Should -BeTrue
    }
    It 'does not flag an ordinary filename' {
        Test-GovConflictCopy -Name 'ORG-GOV-DS-016 - Northwind.docx' | Should -BeFalse
    }
    It 'does not flag a version suffix' {
        Test-GovConflictCopy -Name 'ORG - Spec - v1.2.docx' | Should -BeFalse
    }
}

Describe 'Test-GovSyncQuiet' {
    It 'reports quiet for a settled library' {
        $root = New-TestLibrary -Files @{ 'a.docx' = 'x' }
        (Test-GovSyncQuiet -LibraryRoot $root -WithinSeconds 0).Quiet | Should -BeTrue
    }
    It 'reports not quiet when an Office lock file is present' {
        $root = New-TestLibrary -Files @{ 'a.docx' = 'x'; '~$a.docx' = 'lock' }
        $r = Test-GovSyncQuiet -LibraryRoot $root -WithinSeconds 0
        $r.Quiet     | Should -BeFalse
        $r.TempFiles | Should -Contain '~$a.docx'
    }
    It 'reports not quiet when a file changed inside the window' {
        $root = New-TestLibrary -Files @{ 'a.docx' = 'x' }
        $r = Test-GovSyncQuiet -LibraryRoot $root -WithinSeconds 3600
        $r.Quiet            | Should -BeFalse
        $r.RecentlyModified | Should -Contain 'a.docx'
    }
}

Describe 'Invoke-GovApply sync hazards' {
    It 'skips a conflict copy rather than moving it' {
        $root  = New-TestLibrary -Files @{ 'Report (1).docx' = 'x' }
        $ticks = (Get-Item (Join-Path $root 'Report (1).docx')).LastWriteTimeUtc.Ticks
        $plan  = Save-SyncPlan -Root $root -Rows @(New-SyncRow -Name 'Report (1).docx' -Ticks $ticks)
        $r = Invoke-GovApply -PlanPath $plan -LibraryRoot $root -Baseline $script:Baseline `
                             -JournalPath (Join-Path $root 'j.jsonl') -SettleSeconds 0 -RetryDelayMs 0
        $r.Moved   | Should -Be 0
        $r.Skipped | Should -Be 1
        ($r.Messages -join ' ') | Should -BeLike '*conflict copy*'
        Test-Path -LiteralPath (Join-Path $root 'Report (1).docx') | Should -BeTrue
    }

    It 'retries a locked file and then skips it without losing it' {
        $root  = New-TestLibrary -Files @{ 'a.docx' = 'x' }
        $full  = Join-Path $root 'a.docx'
        $ticks = (Get-Item $full).LastWriteTimeUtc.Ticks
        $plan  = Save-SyncPlan -Root $root -Rows @(New-SyncRow -Name 'a.docx' -Ticks $ticks)
        $fs = [System.IO.File]::Open($full, 'Open', 'ReadWrite', 'None')
        try {
            $r = Invoke-GovApply -PlanPath $plan -LibraryRoot $root -Baseline $script:Baseline `
                                 -JournalPath (Join-Path $root 'j.jsonl') -SettleSeconds 0 `
                                 -RetryCount 2 -RetryDelayMs 0
            $r.Moved   | Should -Be 0
            $r.Skipped | Should -Be 1
        }
        finally { $fs.Dispose() }
        Test-Path -LiteralPath $full | Should -BeTrue
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `pwsh -NoProfile -Command "Invoke-Pester -Path tests/powershell/Sync.Tests.ps1 -Output Detailed"`

Expected: FAIL. `Test-GovConflictCopy` is not defined.

- [ ] **Step 3: Implement the sync helpers**

Create `scripts/GovLib/Sync.ps1`:

```powershell
#Requires -Version 7.0

# Conservative patterns. A false positive only causes a row to be skipped and
# flagged for a human, which is the safe direction.
$script:GovConflictPatterns = @(
    '\(\d+\)\.[^.]+$',                       # Report (1).docx
    "conflicted copy",                       # Jane's conflicted copy 2026-07-14
    '-(DESKTOP|LAPTOP)-[A-Z0-9]{5,}\.[^.]+$',# Report-DESKTOP-A1B2C3D.docx
    ' - Copy\.[^.]+$'                        # Report - Copy.docx
)

$script:GovTempPatterns = @('~$*', '*.tmp', '*.partial', '.~lock*')

function Test-GovConflictCopy {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [string] $Name)

    foreach ($pattern in $script:GovConflictPatterns) {
        if ([regex]::IsMatch($Name, $pattern, [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)) {
            return $true
        }
    }
    return $false
}

function Test-GovSyncQuiet {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $LibraryRoot,
        [int] $WithinSeconds = 60
    )

    $temp = @()
    foreach ($pattern in $script:GovTempPatterns) {
        $temp += @(Get-ChildItem -LiteralPath $LibraryRoot -Recurse -File -Force -Filter $pattern -ErrorAction SilentlyContinue |
                   ForEach-Object { $_.Name })
    }

    $recent = @()
    if ($WithinSeconds -gt 0) {
        $cutoff = (Get-Date).ToUniversalTime().AddSeconds(-$WithinSeconds)
        $recent = @(Get-ChildItem -LiteralPath $LibraryRoot -Recurse -File -Force |
                    Where-Object { $_.LastWriteTimeUtc -gt $cutoff } |
                    ForEach-Object { $_.Name })
    }

    return [pscustomobject]@{
        Quiet            = (($temp.Count -eq 0) -and ($recent.Count -eq 0))
        TempFiles        = $temp
        RecentlyModified = $recent
    }
}
```

- [ ] **Step 4: Wire the helpers into Apply**

In `scripts/GovLib/Apply.ps1`, add two parameters to `Invoke-GovApply`:

```powershell
        [int] $RetryCount = 2,
        [int] $RetryDelayMs = 500
```

Immediately after `New-GovJournal -Path $JournalPath | Out-Null`, add the pre-flight warning:

```powershell
    $sync = Test-GovSyncQuiet -LibraryRoot $LibraryRoot
    if (-not $sync.Quiet) {
        Write-Warning ("Library may be mid-sync. Temp files: {0}. Recently modified: {1}. " +
                       "This check is best-effort and cannot read the sync client's state." -f
                       $sync.TempFiles.Count, $sync.RecentlyModified.Count)
    }
```

Pass the retry settings through to each row by changing the call inside the batch loop to:

```powershell
        Invoke-GovApplyRow -Row $row -LibraryRoot $LibraryRoot -Baseline $Baseline `
                           -JournalPath $JournalPath -Summary $summary `
                           -RetryCount $RetryCount -RetryDelayMs $RetryDelayMs
```

Add the two parameters to `Invoke-GovApplyRow`:

```powershell
        [int] $RetryCount = 2,
        [int] $RetryDelayMs = 500
```

Insert the conflict-copy check immediately after the protected-path check inside `Invoke-GovApplyRow`:

```powershell
    if (Test-GovConflictCopy -Name $Row.Name) {
        $Summary.Skipped++
        $Summary.Messages += "SKIPPED $($Row.Name): looks like a sync conflict copy. Resolve it by hand."
        return
    }
```

Replace the final `try`/`catch` around `Move-Item` with a retry loop:

```powershell
    $attempt = 0
    while ($true) {
        try {
            Move-Item -LiteralPath $source -Destination $target -ErrorAction Stop
            $Summary.Moved++
            break
        }
        catch {
            $attempt++
            if ($attempt -gt $RetryCount) {
                $Summary.Skipped++
                $Summary.Messages += "SKIPPED $($Row.Name) after $attempt attempts: $($_.Exception.Message)"
                break
            }
            if ($RetryDelayMs -gt 0) { Start-Sleep -Milliseconds $RetryDelayMs }
        }
    }
```

- [ ] **Step 5: Export the new functions**

Add `'Test-GovConflictCopy'` and `'Test-GovSyncQuiet'` to `FunctionsToExport`.

- [ ] **Step 6: Run the sync tests to verify they pass**

Run: `pwsh -NoProfile -Command "Invoke-Pester -Path tests/powershell/Sync.Tests.ps1 -Output Detailed"`

Expected: PASS, 11 tests.

- [ ] **Step 7: Run the whole suite to confirm Task 8 still passes**

Run: `pwsh -NoProfile -Command "Invoke-Pester -Path tests/powershell -Output Detailed"`

Expected: PASS, every test across every file. The Task 8 apply tests must be unaffected, since their fixtures contain no conflict-copy names and no locked files.

- [ ] **Step 8: Commit**

```bash
git add scripts/GovLib/Sync.ps1 scripts/GovLib/Apply.ps1 scripts/GovLib/GovLib.psd1 tests/powershell/Sync.Tests.ps1
git commit -m "feat: conflict-copy detection, locked-file retry, and sync pre-flight"
```

---

## Follow-ups deliberately not built

Recorded here so they are not mistaken for oversights.

1. **xlsx interchange.** CSV is the format. Producing a real `.xlsx` with a named `GovFiles` table would need `ImportExcel` or Excel COM. Revisit only if the operator finds CSV round-tripping through Excel painful.
2. **Copilot classification hand-off.** SOP Step 3 is human and Copilot work. The plan file is the hand-off artifact; no automation of that step is in scope.
3. **The four open owner decisions** from spec section 10. All four behave as inaction today. Closing any of them is a config edit, not a code change.
4. **Deeper cleanup passes.** The report produces the evidence; authorizing movement inside approved folders needs an SOP revision first.
