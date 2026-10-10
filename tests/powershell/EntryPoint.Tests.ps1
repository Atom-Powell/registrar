#Requires -Version 7.0
BeforeAll {
    $script:RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
    $script:Script   = Join-Path $script:RepoRoot 'scripts/normalize-governance-library.ps1'
    Import-Module (Join-Path $script:RepoRoot 'scripts/GovLib/GovLib.psd1') -Force
    Import-Module (Join-Path $PSScriptRoot 'TestHelpers.psm1') -Force

    function Save-CliPlan {
        param([string] $Root, [string] $PlanRoot, [string] $Name, [string] $Suggested, [string] $Approved = 'Y')
        $file  = Get-Item -LiteralPath (Join-Path $Root $Name)
        $stamp = Get-GovSourceStamp -Ticks $file.LastWriteTimeUtc.Ticks -SizeBytes $file.Length
        $row = [pscustomobject]@{
            Name = $Name; FolderPath = ''; RelativePath = $Name; Modified = ''
            SourceStamp = $stamp; ModifiedBy = ''; FileType = 'docx'
            SuggestedFolder = $Suggested; FolderExists = $false
            ActionRequired = 'Create Folder and Move'; RuleSource = 't'; Notes = ''
            Approved = $Approved; Prefix = ''; Suffix = ''; NewName = ''
        }
        $p = Join-Path $PlanRoot 'plan.csv'
        $row | Export-Csv -LiteralPath $p -NoTypeInformation -Encoding utf8BOM
        return $p
    }
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

    It 'writes the plan with a UTF-8 BOM so Excel round-trips non-ASCII filenames' {
        $root = New-TestLibrary -Files @{ 'a.docx' = 'x' }
        $out  = New-TestRoot
        & $script:Script -Mode Plan -LibraryRoot $root -OutputRoot $out
        $plan = @(Get-ChildItem -LiteralPath $out -Recurse -Filter 'GOV-ROOT-NORMALIZATION-RUN-*.csv')[0]
        $bytes = [System.IO.File]::ReadAllBytes($plan.FullName)
        $bytes[0..2] | Should -Be @(0xEF, 0xBB, 0xBF)
    }

    It 'tells the operator which Approved values are accepted' {
        $root = New-TestLibrary -Files @{ 'a.docx' = 'x' }
        $text = & $script:Script -Mode Plan -LibraryRoot $root -OutputRoot (New-TestRoot) 6>&1
        ($text -join ' ') | Should -BeLike '*Y, YES, TRUE, X, 1*'
    }
}

Describe 'normalize-governance-library.ps1 exit codes' {
    It 'exits 0 after an Apply that moved everything it was asked to' {
        $root = New-TestLibrary -Files @{ 'a.docx' = 'x' }
        $out  = New-TestRoot
        $plan = Save-CliPlan -Root $root -PlanRoot $out -Name 'a.docx' -Suggested 'Controlled Documents/Governance'
        & $script:Script -Mode Apply -LibraryRoot $root -OutputRoot $out -PlanPath $plan -SettleSeconds 0
        $LASTEXITCODE | Should -Be 0
    }

    It 'exits 1 after an Apply that refused a row' {
        # A scheduled wrapper must not read "refused everything" as success.
        $root = New-TestLibrary -Files @{ 'a.docx' = 'x' }
        $out  = New-TestRoot
        $plan = Save-CliPlan -Root $root -PlanRoot $out -Name 'a.docx' -Suggested 'Architecture Decision Records'
        & $script:Script -Mode Apply -LibraryRoot $root -OutputRoot $out -PlanPath $plan -SettleSeconds 0
        $LASTEXITCODE | Should -Be 1
        Test-Path -LiteralPath (Join-Path $root 'a.docx') | Should -BeTrue
    }

    It 'exits 1 after an Apply that skipped a row' {
        $root = New-TestLibrary -Files @{ 'a.docx' = 'new'; 'Controlled Documents/Governance/a.docx' = 'old' }
        $out  = New-TestRoot
        $plan = Save-CliPlan -Root $root -PlanRoot $out -Name 'a.docx' -Suggested 'Controlled Documents/Governance'
        & $script:Script -Mode Apply -LibraryRoot $root -OutputRoot $out -PlanPath $plan -SettleSeconds 0
        $LASTEXITCODE | Should -Be 1
    }

    It 'exits 1 after an Undo that flagged an entry' {
        $root = New-TestLibrary -Files @{}
        $out  = New-TestRoot
        $j = Join-Path $out 'j.jsonl'
        New-GovJournal -Path $j | Out-Null
        Add-GovJournalEntry -JournalPath $j -Action 'move' `
                            -Source (Join-Path $root 'a.docx') -Destination (Join-Path $root 'Sub/a.docx')
        & $script:Script -Mode Undo -JournalPath $j -OutputRoot $out
        $LASTEXITCODE | Should -Be 1
    }

    It 'passes LibraryRoot through to Undo when the operator supplied one' {
        $root    = New-TestLibrary -Files @{}
        $outside = New-TestLibrary -Files @{ 'stray.docx' = 'x' }
        $out     = New-TestRoot
        $j = Join-Path $out 'j.jsonl'
        New-GovJournal -Path $j | Out-Null
        Add-GovJournalEntry -JournalPath $j -Action 'move' `
                            -Source (Join-Path $root 'stray.docx') -Destination (Join-Path $outside 'stray.docx')
        $text = & $script:Script -Mode Undo -JournalPath $j -LibraryRoot $root -OutputRoot $out 6>&1
        $LASTEXITCODE | Should -Be 1
        ($text -join ' ') | Should -BeLike '*resolves outside the library root*'
        Test-Path -LiteralPath (Join-Path $outside 'stray.docx') | Should -BeTrue
    }

    It 'exits 0 in Inventory, Plan and Report modes' {
        $root = New-TestLibrary -Files @{ 'a.docx' = 'x' }
        foreach ($mode in 'Inventory', 'Plan', 'Report') {
            & $script:Script -Mode $mode -LibraryRoot $root -OutputRoot (New-TestRoot)
            $LASTEXITCODE | Should -Be 0 -Because "mode $mode is read-only or proposes only"
        }
    }
}
