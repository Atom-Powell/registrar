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
        param([string] $Name, [string] $Stamp)
        [pscustomobject]@{
            Name = $Name; FolderPath = ''; RelativePath = $Name; Modified = ''
            SourceStamp = $Stamp; ModifiedBy = ''; FileType = 'docx'
            SuggestedFolder = 'Controlled Documents/Governance'; FolderExists = $false
            ActionRequired = 'Create Folder and Move'; RuleSource = 't'; Notes = ''
            Approved = 'Y'; Prefix = ''; Suffix = ''; NewName = ''
        }
    }
    function Get-SyncStampFor {
        param([string] $Path)
        $f = Get-Item -LiteralPath $Path
        return (Get-GovSourceStamp -Ticks $f.LastWriteTimeUtc.Ticks -SizeBytes $f.Length)
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
        $stamp = Get-SyncStampFor (Join-Path $root 'Report (1).docx')
        $plan  = Save-SyncPlan -Root $root -Rows @(New-SyncRow -Name 'Report (1).docx' -Stamp $stamp)
        $r = Invoke-GovApply -PlanPath $plan -LibraryRoot $root -Baseline $script:Baseline `
                             -JournalPath (Join-Path $root 'j.jsonl') -SettleSeconds 0 -RetryDelayMs 0 `
                             -SyncCheckWithinSeconds 0
        $r.Moved   | Should -Be 0
        $r.Skipped | Should -Be 1
        ($r.Messages -join ' ') | Should -BeLike '*conflict copy*'
        Test-Path -LiteralPath (Join-Path $root 'Report (1).docx') | Should -BeTrue
    }

    It 'retries a locked file and then skips it without losing it' {
        $root  = New-TestLibrary -Files @{ 'a.docx' = 'x' }
        $full  = Join-Path $root 'a.docx'
        $stamp = Get-SyncStampFor $full
        $plan  = Save-SyncPlan -Root $root -Rows @(New-SyncRow -Name 'a.docx' -Stamp $stamp)
        $fs = [System.IO.File]::Open($full, 'Open', 'ReadWrite', 'None')
        try {
            $r = Invoke-GovApply -PlanPath $plan -LibraryRoot $root -Baseline $script:Baseline `
                                 -JournalPath (Join-Path $root 'j.jsonl') -SettleSeconds 0 `
                                 -RetryCount 2 -RetryDelayMs 0 -SyncCheckWithinSeconds 0
            $r.Moved   | Should -Be 0
            $r.Skipped | Should -Be 1
        }
        finally { $fs.Dispose() }
        Test-Path -LiteralPath $full | Should -BeTrue
    }

    It 'warns but still proceeds when the library is not sync-quiet' {
        # Deliberately does not pass -SyncCheckWithinSeconds 0: this is the one
        # Apply test left exercising the default not-quiet path, so the warning
        # behavior itself stays covered.
        $root  = New-TestLibrary -Files @{ 'a.docx' = 'x' }
        $stamp = Get-SyncStampFor (Join-Path $root 'a.docx')
        $plan  = Save-SyncPlan -Root $root -Rows @(New-SyncRow -Name 'a.docx' -Stamp $stamp)
        $warnings = $null
        $r = Invoke-GovApply -PlanPath $plan -LibraryRoot $root -Baseline $script:Baseline `
                             -JournalPath (Join-Path $root 'j.jsonl') -SettleSeconds 0 -RetryDelayMs 0 `
                             -WarningVariable warnings -WarningAction SilentlyContinue
        $r.Moved | Should -Be 1
        ($warnings -join ' ') | Should -BeLike '*mid-sync*'
    }
}
