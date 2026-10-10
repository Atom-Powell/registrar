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

    This script is a thin CLI shell: prompting (the folder picker), mode
    dispatch, and Write-Host formatting only. Defaulting, validation, and
    other operator-workflow logic live in GovLib (New-GovRunContext) so a
    future GUI can call the same functions directly.

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

$runContext = New-GovRunContext -Mode $Mode -LibraryRoot $LibraryRoot -RepoRoot $repoRoot `
                                 -OutputRoot $OutputRoot -BaselinePath $BaselinePath

$LibraryRoot  = $runContext.LibraryRoot
$OutputRoot   = $runContext.OutputRoot
$runDir       = $runContext.RunDir
$BaselinePath = $runContext.BaselinePath
$stamp        = $runContext.Stamp

$regContext    = Import-GovRegistryContext -RegistryPath $RegistryPath -ArtifactTypesPath $runContext.ArtifactTypesPath
$registry      = $regContext.Registry
$artifactTypes = $regContext.ArtifactTypes

$baseline = Import-GovBaseline -Path $BaselinePath

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
        # utf8BOM, unlike every other artifact this tool writes: the plan is the
        # one file an operator opens in Excel, and without the BOM Excel mangles
        # non-ASCII filenames on save. This is file data, not source.
        $plan | Export-Csv -LiteralPath $out -NoTypeInformation -Encoding utf8BOM
        Write-Host "Plan written: $out ($($plan.Count) rows)"
        Write-Host 'Nothing has moved. Review the plan, fill the Approved column, then run -Mode Apply.'
        Write-Host "Accepted Approved values: $((Get-GovApprovalTokens) -join ', '). Anything else, including N, is not approved."
    }

    'Apply' {
        if (-not $PlanPath) { throw 'PlanPath is required for Apply.' }
        if (-not $JournalPath) { $JournalPath = $runContext.DefaultJournalPath }
        $result = Invoke-GovApply -PlanPath $PlanPath -LibraryRoot $LibraryRoot -Baseline $baseline `
                                  -JournalPath $JournalPath -BatchSize $BatchSize -SettleSeconds $SettleSeconds
        $result.Messages | ForEach-Object { Write-Host $_ }
        Write-Host "Moved $($result.Moved), skipped $($result.Skipped), refused $($result.Refused)."
        Write-Host "Undo journal: $($result.JournalPath)"
        if ($result.Refused -gt 0 -or $result.Skipped -gt 0) { exit 1 }
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
                                -RepositoryTargetsPath $runContext.RepositoryTargetsPath
        $md  = Join-Path $runDir "drift-report-$stamp.md"
        $csv = Join-Path $runDir "drift-misplaced-$stamp.csv"
        Set-Content -LiteralPath $md -Value $report.Markdown -Encoding utf8NoBOM
        @($report.Misplaced) | Export-Csv -LiteralPath $csv -NoTypeInformation -Encoding utf8NoBOM
        Write-Host "Report written: $md"
    }

    'Undo' {
        if (-not $JournalPath) { throw 'JournalPath is required for Undo.' }
        # Undo can run from a journal alone, so LibraryRoot is passed only when
        # the operator supplied one. With it, entries outside the root are flagged.
        $undoArgs = @{ JournalPath = $JournalPath }
        if ($LibraryRoot) { $undoArgs.LibraryRoot = $LibraryRoot }
        $result = Invoke-GovUndo @undoArgs
        $result.Messages | ForEach-Object { Write-Host $_ }
        Write-Host "Restored $($result.Restored), flagged $($result.Flagged)."
        if ($result.Flagged -gt 0) { exit 1 }
    }
}

exit 0
