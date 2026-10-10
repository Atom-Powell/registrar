#Requires -Version 7.0
BeforeAll {
    $script:RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
    Import-Module (Join-Path $script:RepoRoot 'scripts/GovLib/GovLib.psd1') -Force
    Import-Module (Join-Path $PSScriptRoot 'TestHelpers.psm1') -Force

    $script:Baseline = Import-GovBaseline      -Path (Join-Path $script:RepoRoot 'config/library-baseline.json')
    $script:Types    = Import-GovArtifactTypes -Path (Join-Path $script:RepoRoot 'config/artifact-types.json')
    $script:Registry = Import-GovRegistry      -Path (Join-Path $script:RepoRoot 'fixtures/registry-sample.csv')
}

Describe 'End to end: Inventory to Plan to Apply to Verify to Undo' {
    It 'runs the real pipeline and undo returns the tree to its original state' {
        # Every other suite hand-rolls plan rows, so the seams between the
        # stages are untested. This test wires the real functions together:
        # New-GovPlan output is what Invoke-GovApply consumes, Invoke-GovVerify
        # runs against a real Apply result, and Invoke-GovUndo runs against the
        # journal that Apply actually wrote.
        $registered   = 'ORG-GOV-DS-016 - Data Security Overview.docx'
        $unidentified = 'random notes.docx'

        $root = New-TestLibrary -Files @{
            $registered   = 'registered draft content'
            $unidentified = 'no document id in this name'
        }

        # Run artifacts must live outside the library: anything written inside
        # it would sync to SharePoint and would break the snapshot comparison.
        $work = New-TestRoot

        $before = Get-TreeSnapshot -Root $root

        $inventory = @(Get-GovInventory -LibraryRoot $root)
        $inventory.Count | Should -Be 2

        $plan = @(New-GovPlan -Inventory $inventory -Baseline $script:Baseline -LibraryRoot $root `
                              -Registry $script:Registry -ArtifactTypes $script:Types)
        $plan.Count | Should -Be 2

        $planPath = Join-Path $work 'GOV-ROOT-NORMALIZATION-RUN-endtoend.csv'
        $plan | Export-Csv -LiteralPath $planPath -NoTypeInformation -Encoding utf8BOM

        # The operator step, performed programmatically: reopen the saved plan,
        # approve only the rows that carry a proposed destination, save it back.
        $rows = @(Import-Csv -LiteralPath $planPath)
        $rows.Count | Should -Be 2
        foreach ($row in $rows) {
            if (-not [string]::IsNullOrWhiteSpace($row.SuggestedFolder)) { $row.Approved = 'Y' }
        }
        $rows | Export-Csv -LiteralPath $planPath -NoTypeInformation -Encoding utf8BOM
        @($rows | Where-Object { -not [string]::IsNullOrWhiteSpace($_.Approved) }).Count | Should -Be 1

        $journal = Join-Path $work 'undo-manifest-endtoend.jsonl'
        $applied = Invoke-GovApply -PlanPath $planPath -LibraryRoot $root -Baseline $script:Baseline `
                                   -JournalPath $journal `
                                   -SettleSeconds 0 -RetryDelayMs 0 -SyncCheckWithinSeconds 0

        $applied.Moved   | Should -Be 1
        $applied.Refused | Should -Be 0
        $applied.Skipped | Should -Be 0

        $landedPath = Join-Path $root 'Master Development Workspace/ORG-GOV-DS-016 - Data Security Overview.docx'
        Test-Path -LiteralPath $landedPath                  | Should -BeTrue
        Test-Path -LiteralPath (Join-Path $root $registered) | Should -BeFalse
        Test-Path -LiteralPath (Join-Path $root $unidentified) | Should -BeTrue

        $verified = Invoke-GovVerify -PlanPath $planPath -LibraryRoot $root
        $verified.Missing | Should -BeNullOrEmpty
        $verified.Landed  | Should -Be 1
        $verified.Ok      | Should -BeTrue

        $undone = Invoke-GovUndo -JournalPath $journal -LibraryRoot $root
        $undone.Restored | Should -Be 1
        $undone.Flagged  | Should -Be 0

        # The strongest assertion in the suite: exact equality of the whole
        # file tree, not a count. Undo must leave nothing moved and nothing lost.
        (Get-TreeSnapshot -Root $root) -join '|' | Should -Be ($before -join '|')
    }
}
