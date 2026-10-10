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

    It 'validates the status on every dispositions entry' {
        $tmp = New-TestBaseline -OverrideJson '{ "baselineVersion": "1.0", "nestingOrder": "category-first", "folderTree": [], "placementRules": { "decidingFieldsInPriorityOrder": [], "mappings": [] }, "dispositions": { "retired": { "action": "leaveInPlace", "status": "banana" } }, "fileNaming": { "status": "notDefined" }, "identification": { "status": "blocked" }, "doNotTouch": [] }'
        { Import-GovBaseline -Path $tmp } | Should -Throw "*dispositions entry 'retired'*"
    }

    It 'validates the action on every dispositions entry' {
        $tmp = New-TestBaseline -OverrideJson '{ "baselineVersion": "1.0", "nestingOrder": "category-first", "folderTree": [], "placementRules": { "decidingFieldsInPriorityOrder": [], "mappings": [] }, "dispositions": { "retired": { "action": "vanish", "status": "notDefined" } }, "fileNaming": { "status": "notDefined" }, "identification": { "status": "blocked" }, "doNotTouch": [] }'
        { Import-GovBaseline -Path $tmp } | Should -Throw "*Unknown action 'vanish'*"
    }

    It 'throws when a dispositions entry has no action' {
        $tmp = New-TestBaseline -OverrideJson '{ "baselineVersion": "1.0", "nestingOrder": "category-first", "folderTree": [], "placementRules": { "decidingFieldsInPriorityOrder": [], "mappings": [] }, "dispositions": { "retired": { "status": "notDefined" } }, "fileNaming": { "status": "notDefined" }, "identification": { "status": "blocked" }, "doNotTouch": [] }'
        { Import-GovBaseline -Path $tmp } | Should -Throw "*Missing 'action'*"
    }

    It 'accepts the shipped dispositions, including the reserved nonControlled key' {
        $b = Import-GovBaseline -Path (Join-Path $script:RepoRoot 'config/library-baseline.json')
        $b.dispositions.retired.action       | Should -Be 'leaveInPlace'
        $b.dispositions.unrecognized.action  | Should -Be 'reviewRequired'
        $b.dispositions.nonControlled.action | Should -Be 'leaveInPlace'
    }

    It 'throws when folderTree specifically is missing' {
        $tmp = New-TestBaseline -OverrideJson '{ "baselineVersion": "1.0", "nestingOrder": "category-first" }'
        { Import-GovBaseline -Path $tmp } | Should -Throw '*folderTree*'
    }
}

Describe 'Repository hygiene' {
    It 'has no literal non-ASCII characters in any file bound by the ASCII-only rule' {
        # The CLI entry point and this test suite are bound by the same rule as
        # GovLib, so they are scanned too. Other scripts under scripts/ are out
        # of scope for this branch, and normalize-hygiene.ps1 must carry the
        # very glyphs it strips.
        $files = @(Get-ChildItem (Join-Path $script:RepoRoot 'scripts/GovLib') -File) +
                 @(Get-Item (Join-Path $script:RepoRoot 'scripts/normalize-governance-library.ps1')) +
                 @(Get-ChildItem (Join-Path $script:RepoRoot 'tests/powershell') -File) +
                 @(Get-Item (Join-Path $script:RepoRoot 'config/library-baseline.json'))
        foreach ($f in $files) {
            $hits = Select-String -Path $f.FullName -Pattern '[^\x00-\x7F]' -AllMatches
            $hits | Should -BeNullOrEmpty -Because "$($f.Name) must be ASCII only"
        }
    }
}
