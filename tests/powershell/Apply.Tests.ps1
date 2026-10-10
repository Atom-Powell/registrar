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
            [string] $Approved = 'Y', [string] $NewName = '', [string] $Stamp = 'sffffffffffffffff'
        )
        [pscustomobject]@{
            Name = $Name; FolderPath = ''; RelativePath = $Name; Modified = ''
            SourceStamp = $Stamp; ModifiedBy = ''; FileType = 'docx'
            SuggestedFolder = $Suggested; FolderExists = $false; ActionRequired = $Action
            RuleSource = 't'; Notes = ''; Approved = $Approved
            Prefix = ''; Suffix = ''; NewName = $NewName
        }
    }

    function Get-StampFor {
        param([string] $Path)
        $f = Get-Item -LiteralPath $Path
        return (Get-GovSourceStamp -Ticks $f.LastWriteTimeUtc.Ticks -SizeBytes $f.Length)
    }
}

Describe 'Invoke-GovApply' {
    It 'moves an approved row into an existing folder' {
        $root = New-TestLibrary -Files @{ 'a.docx' = 'x'; 'Controlled Documents/Governance/keep.txt' = 'k' }
        $stamp = Get-StampFor (Join-Path $root 'a.docx')
        $plan  = Save-Plan -Root $root -Rows @(New-Row -Name 'a.docx' -Suggested 'Controlled Documents/Governance' -Action 'Move to Existing Folder' -Stamp $stamp)
        $r = Invoke-GovApply -PlanPath $plan -LibraryRoot $root -Baseline $script:Baseline -JournalPath (Join-Path $root 'j.jsonl') -SettleSeconds 0 -SyncCheckWithinSeconds 0
        $r.Moved | Should -Be 1
        Test-Path -LiteralPath (Join-Path $root 'Controlled Documents/Governance/a.docx') | Should -BeTrue
    }

    It 'does not move a row that is not approved' {
        $root = New-TestLibrary -Files @{ 'a.docx' = 'x' }
        $stamp = Get-StampFor (Join-Path $root 'a.docx')
        $plan  = Save-Plan -Root $root -Rows @(New-Row -Name 'a.docx' -Suggested 'Controlled Documents/Governance' -Action 'Create Folder and Move' -Approved '' -Stamp $stamp)
        $r = Invoke-GovApply -PlanPath $plan -LibraryRoot $root -Baseline $script:Baseline -JournalPath (Join-Path $root 'j.jsonl') -SettleSeconds 0 -SyncCheckWithinSeconds 0
        $r.Moved | Should -Be 0
        Test-Path -LiteralPath (Join-Path $root 'a.docx') | Should -BeTrue
    }

    It 'refuses a protected path even when the row is approved' {
        $root = New-TestLibrary -Files @{ 'Document Registry/ORG-ID-Registry.xlsx' = 'x' }
        $row  = New-Row -Name 'ORG-ID-Registry.xlsx' -Suggested 'Archive' -Action 'Create Folder and Move'
        $row.RelativePath = 'Document Registry/ORG-ID-Registry.xlsx'
        $row.SourceStamp = Get-StampFor (Join-Path $root 'Document Registry/ORG-ID-Registry.xlsx')
        $plan = Save-Plan -Root $root -Rows @($row)
        $r = Invoke-GovApply -PlanPath $plan -LibraryRoot $root -Baseline $script:Baseline -JournalPath (Join-Path $root 'j.jsonl') -SettleSeconds 0 -SyncCheckWithinSeconds 0
        $r.Refused | Should -Be 1
        Test-Path -LiteralPath (Join-Path $root 'Document Registry/ORG-ID-Registry.xlsx') | Should -BeTrue
    }

    It 'skips a row whose source changed after planning' {
        $root = New-TestLibrary -Files @{ 'a.docx' = 'x' }
        $plan = Save-Plan -Root $root -Rows @(New-Row -Name 'a.docx' -Suggested 'Controlled Documents/Governance' -Action 'Create Folder and Move' -Stamp 'sdeadbeefdeadbeef')
        $r = Invoke-GovApply -PlanPath $plan -LibraryRoot $root -Baseline $script:Baseline -JournalPath (Join-Path $root 'j.jsonl') -SettleSeconds 0 -SyncCheckWithinSeconds 0
        $r.Skipped | Should -Be 1
        Test-Path -LiteralPath (Join-Path $root 'a.docx') | Should -BeTrue
    }

    It 'never overwrites an existing destination file' {
        $root = New-TestLibrary -Files @{ 'a.docx' = 'new'; 'Controlled Documents/Governance/a.docx' = 'old' }
        $stamp = Get-StampFor (Join-Path $root 'a.docx')
        $plan  = Save-Plan -Root $root -Rows @(New-Row -Name 'a.docx' -Suggested 'Controlled Documents/Governance' -Action 'Move to Existing Folder' -Stamp $stamp)
        $r = Invoke-GovApply -PlanPath $plan -LibraryRoot $root -Baseline $script:Baseline -JournalPath (Join-Path $root 'j.jsonl') -SettleSeconds 0 -SyncCheckWithinSeconds 0
        $r.Skipped | Should -Be 1
        Get-Content -LiteralPath (Join-Path $root 'Controlled Documents/Governance/a.docx') -Raw | Should -BeLike 'old*'
        Test-Path -LiteralPath (Join-Path $root 'a.docx') | Should -BeTrue
    }

    It 'never reduces the total file count' {
        $root = New-TestLibrary -Files @{ 'a.docx' = 'x'; 'b.docx' = 'y'; 'c.docx' = 'z' }
        $rows = foreach ($n in 'a.docx','b.docx','c.docx') {
            New-Row -Name $n -Suggested 'Controlled Documents/Governance' -Action 'Create Folder and Move' `
                    -Stamp (Get-StampFor (Join-Path $root $n))
        }
        $before = (Get-ChildItem -LiteralPath $root -Recurse -File).Count
        $plan = Save-Plan -Root $root -Rows $rows
        Invoke-GovApply -PlanPath $plan -LibraryRoot $root -Baseline $script:Baseline -JournalPath (Join-Path $root 'j.jsonl') -SettleSeconds 0 -SyncCheckWithinSeconds 0 | Out-Null
        (Get-ChildItem -LiteralPath $root -Recurse -File).Count | Should -BeGreaterOrEqual $before
    }

    It 'records a journal entry for a completed move' {
        # Weaker than ordering: this only confirms the entry exists and names the
        # right source after a successful run. It would pass even if the journal
        # write happened after the move. See the ordering test below for the
        # invariant that must never be reversed.
        $root = New-TestLibrary -Files @{ 'a.docx' = 'x' }
        $stamp = Get-StampFor (Join-Path $root 'a.docx')
        $plan  = Save-Plan -Root $root -Rows @(New-Row -Name 'a.docx' -Suggested 'Controlled Documents/Governance' -Action 'Create Folder and Move' -Stamp $stamp)
        $j = Join-Path $root 'j.jsonl'
        Invoke-GovApply -PlanPath $plan -LibraryRoot $root -Baseline $script:Baseline -JournalPath $j -SettleSeconds 0 -SyncCheckWithinSeconds 0 | Out-Null
        $entries = Read-GovJournal -Path $j
        $entries.Count      | Should -Be 1
        $entries[0].Source  | Should -BeLike '*a.docx'
    }

    It 'writes the journal entry before attempting the move' {
        # Forces a failure between the journal write and the move. If the two
        # statements in Invoke-GovApplyRow were ever swapped, the journal entry
        # would not exist here and this assertion would fail.
        $root  = New-TestLibrary -Files @{ 'a.docx' = 'x' }
        $stamp = Get-StampFor (Join-Path $root 'a.docx')
        $plan  = Save-Plan -Root $root -Rows @(New-Row -Name 'a.docx' -Suggested 'Controlled Documents/Governance' -Action 'Create Folder and Move' -Stamp $stamp)
        $j = Join-Path $root 'j.jsonl'

        Mock -ModuleName GovLib Move-Item { throw 'simulated failure' }

        $r = Invoke-GovApply -PlanPath $plan -LibraryRoot $root -Baseline $script:Baseline -JournalPath $j -SettleSeconds 0 -RetryDelayMs 0 -SyncCheckWithinSeconds 0

        $r.Moved   | Should -Be 0
        $r.Skipped | Should -Be 1
        $entries = Read-GovJournal -Path $j
        $entries.Count     | Should -Be 1
        $entries[0].Source | Should -BeLike '*a.docx'
        Test-Path -LiteralPath (Join-Path $root 'a.docx') | Should -BeTrue
    }

    It 'refuses an approved row whose destination folder is protected' {
        $root = New-TestLibrary -Files @{ 'a.docx' = 'x' }
        $stamp = Get-StampFor (Join-Path $root 'a.docx')
        $plan  = Save-Plan -Root $root -Rows @(New-Row -Name 'a.docx' -Suggested 'Architecture Decision Records' -Action 'Create Folder and Move' -Stamp $stamp)
        $r = Invoke-GovApply -PlanPath $plan -LibraryRoot $root -Baseline $script:Baseline -JournalPath (Join-Path $root 'j.jsonl') -SettleSeconds 0 -SyncCheckWithinSeconds 0
        $r.Refused | Should -Be 1
        Test-Path -LiteralPath (Join-Path $root 'a.docx') | Should -BeTrue
        Test-Path -LiteralPath (Join-Path $root 'Architecture Decision Records/a.docx') | Should -BeFalse
    }

    It 'renames when the row carries a NewName' {
        $root = New-TestLibrary -Files @{ 'a.docx' = 'x' }
        $stamp = Get-StampFor (Join-Path $root 'a.docx')
        $plan  = Save-Plan -Root $root -Rows @(New-Row -Name 'a.docx' -Suggested 'Controlled Documents/Governance' -Action 'Create Folder and Move' -NewName 'ORG-GOV-DS-016 - a.docx' -Stamp $stamp)
        Invoke-GovApply -PlanPath $plan -LibraryRoot $root -Baseline $script:Baseline -JournalPath (Join-Path $root 'j.jsonl') -SettleSeconds 0 -SyncCheckWithinSeconds 0 | Out-Null
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

Describe 'Approved column semantics' {
    It 'accepts <Token> as approval and moves the file' -TestCases @(
        @{ Token = 'Y' }, @{ Token = 'y' }, @{ Token = 'Yes' }, @{ Token = 'YES' }
        @{ Token = 'TRUE' }, @{ Token = 'true' }, @{ Token = 'X' }, @{ Token = '1' }
        @{ Token = '  Y  ' }
    ) {
        param([string] $Token)
        $root  = New-TestLibrary -Files @{ 'a.docx' = 'x' }
        $stamp = Get-StampFor (Join-Path $root 'a.docx')
        $plan  = Save-Plan -Root $root -Rows @(New-Row -Name 'a.docx' -Suggested 'Controlled Documents/Governance' `
                                                       -Action 'Create Folder and Move' -Approved $Token -Stamp $stamp)
        $r = Invoke-GovApply -PlanPath $plan -LibraryRoot $root -Baseline $script:Baseline `
                             -JournalPath (Join-Path $root 'j.jsonl') -SettleSeconds 0 -SyncCheckWithinSeconds 0
        $r.Moved | Should -Be 1
        Test-Path -LiteralPath (Join-Path $root 'Controlled Documents/Governance/a.docx') | Should -BeTrue
    }

    It 'refuses to treat <Token> as approval and leaves the file alone' -TestCases @(
        @{ Token = 'N' }, @{ Token = 'n' }, @{ Token = 'No' }, @{ Token = 'FALSE' }
        @{ Token = '0' }, @{ Token = 'maybe' }, @{ Token = '' }, @{ Token = '   ' }
    ) {
        param([string] $Token)
        # 'N' is the reproduced defect: any non-blank value used to move the file.
        $root  = New-TestLibrary -Files @{ 'a.docx' = 'x' }
        $stamp = Get-StampFor (Join-Path $root 'a.docx')
        $plan  = Save-Plan -Root $root -Rows @(New-Row -Name 'a.docx' -Suggested 'Controlled Documents/Governance' `
                                                       -Action 'Create Folder and Move' -Approved $Token -Stamp $stamp)
        $r = Invoke-GovApply -PlanPath $plan -LibraryRoot $root -Baseline $script:Baseline `
                             -JournalPath (Join-Path $root 'j.jsonl') -SettleSeconds 0 -SyncCheckWithinSeconds 0
        $r.Moved | Should -Be 0
        Test-Path -LiteralPath (Join-Path $root 'a.docx') | Should -BeTrue
        Test-Path -LiteralPath (Join-Path $root 'Controlled Documents/Governance/a.docx') | Should -BeFalse
    }

    It 'exposes the accepted token list for the CLI to print' {
        Get-GovApprovalTokens | Should -Be @('Y', 'YES', 'TRUE', 'X', '1')
    }
}

Describe 'Invoke-GovApply containment' {
    It 'refuses an approved row whose destination climbs out of the library root' {
        # Moving a file out of a synced OneDrive root is a server-side deletion.
        $root    = New-TestLibrary -Files @{ 'a.docx' = 'x' }
        $outside = Join-Path (Split-Path -Parent $root) ((Split-Path -Leaf $root) + '-outside')
        New-Item -ItemType Directory -Path $outside -Force | Out-Null

        $stamp = Get-StampFor (Join-Path $root 'a.docx')
        $plan  = Save-Plan -Root $root -Rows @(New-Row -Name 'a.docx' `
                                                       -Suggested ('../' + (Split-Path -Leaf $outside)) `
                                                       -Action 'Create Folder and Move' -Stamp $stamp)
        $r = Invoke-GovApply -PlanPath $plan -LibraryRoot $root -Baseline $script:Baseline `
                             -JournalPath (Join-Path $root 'j.jsonl') -SettleSeconds 0 -SyncCheckWithinSeconds 0

        $r.Refused | Should -Be 1
        $r.Moved   | Should -Be 0
        ($r.Messages -join ' ') | Should -BeLike '*resolves outside the library root*'
        Test-Path -LiteralPath (Join-Path $root 'a.docx')        | Should -BeTrue
        Test-Path -LiteralPath (Join-Path $outside 'a.docx')     | Should -BeFalse
        @(Get-ChildItem -LiteralPath $outside -File).Count       | Should -Be 0
    }

    It 'refuses an approved row whose source climbs out of the library root' {
        $root    = New-TestLibrary -Files @{ 'a.docx' = 'x' }
        $outside = Split-Path -Parent $root
        Set-Content -LiteralPath (Join-Path $outside 'escapee.docx') -Value 'x' -Encoding utf8NoBOM

        $row = New-Row -Name 'escapee.docx' -Suggested 'Controlled Documents/Governance' -Action 'Create Folder and Move'
        $row.RelativePath = '../escapee.docx'
        $plan = Save-Plan -Root $root -Rows @($row)

        $r = Invoke-GovApply -PlanPath $plan -LibraryRoot $root -Baseline $script:Baseline `
                             -JournalPath (Join-Path $root 'j.jsonl') -SettleSeconds 0 -SyncCheckWithinSeconds 0

        $r.Refused | Should -Be 1
        $r.Moved   | Should -Be 0
        Test-Path -LiteralPath (Join-Path $outside 'escapee.docx') | Should -BeTrue
    }

    It 'writes no journal entry for a row it refuses on containment' {
        # Containment is checked before the journal write, so a refused row
        # leaves nothing for undo to reverse.
        $root  = New-TestLibrary -Files @{ 'a.docx' = 'x' }
        $stamp = Get-StampFor (Join-Path $root 'a.docx')
        $plan  = Save-Plan -Root $root -Rows @(New-Row -Name 'a.docx' -Suggested '../elsewhere' `
                                                       -Action 'Create Folder and Move' -Stamp $stamp)
        $j = Join-Path $root 'j.jsonl'
        Invoke-GovApply -PlanPath $plan -LibraryRoot $root -Baseline $script:Baseline `
                        -JournalPath $j -SettleSeconds 0 -SyncCheckWithinSeconds 0 | Out-Null
        @(Read-GovJournal -Path $j).Count | Should -Be 0
    }
}

Describe 'Get-GovSourceStamp' {
    It 'is stable for the same ticks and size' {
        (Get-GovSourceStamp -Ticks 638000000000000000 -SizeBytes 1024) |
            Should -Be (Get-GovSourceStamp -Ticks 638000000000000000 -SizeBytes 1024)
    }

    It 'changes when either the ticks or the size change' {
        $base = Get-GovSourceStamp -Ticks 638000000000000000 -SizeBytes 1024
        (Get-GovSourceStamp -Ticks 638000000000000001 -SizeBytes 1024) | Should -Not -Be $base
        (Get-GovSourceStamp -Ticks 638000000000000000 -SizeBytes 1025) | Should -Not -Be $base
    }

    It 'is short and never all digits, so Excel cannot coerce it to a number' {
        $stamp = Get-GovSourceStamp -Ticks 638000000000000000 -SizeBytes 1024
        $stamp | Should -Match '^s[0-9a-f]{16}$'
    }
}
