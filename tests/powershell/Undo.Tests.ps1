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
        ($r.Messages -join ' ') | Should -BeLike '*is occupied. Never overwriting.*'
    }

    It 'tolerates a journal entry whose destination never materialized' {
        $root = New-TestLibrary -Files @{}
        $j = Join-Path $root 'j.jsonl'
        New-GovJournal -Path $j | Out-Null
        Add-GovJournalEntry -JournalPath $j -Action 'move' -Source (Join-Path $root 'a.docx') -Destination (Join-Path $root 'X/a.docx')
        $r = Invoke-GovUndo -JournalPath $j
        $r.Flagged | Should -Be 1
        ($r.Messages -join ' ') | Should -BeLike '*is not present. Nothing to reverse.*'
    }

    It 'flags rather than moves when Move-Item fails for a reason unrelated to the guards' {
        # Both explicit guards must pass here (destination present, source free)
        # so execution genuinely reaches Move-Item and its generic catch branch.
        $root = New-TestLibrary -Files @{ 'a.docx' = 'x' }
        $src  = Join-Path $root 'b.docx'
        $dst  = Join-Path $root 'a.docx'
        $j = Join-Path $root 'j.jsonl'
        New-GovJournal -Path $j | Out-Null
        Add-GovJournalEntry -JournalPath $j -Action 'move' -Source $src -Destination $dst

        Mock -ModuleName GovLib Move-Item { throw 'simulated failure' }

        $r = Invoke-GovUndo -JournalPath $j
        $r.Flagged  | Should -Be 1
        $r.Restored | Should -Be 0
        Test-Path -LiteralPath $dst | Should -BeTrue
        Test-Path -LiteralPath $src | Should -BeFalse
        ($r.Messages -join ' ') | Should -BeLike '*simulated failure*'
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

Describe 'Invoke-GovUndo containment' {
    It 'flags an entry whose destination falls outside a supplied LibraryRoot' {
        $root    = New-TestLibrary -Files @{}
        $outside = New-TestLibrary -Files @{ 'stray.docx' = 'x' }
        $j = Join-Path $root 'j.jsonl'
        New-GovJournal -Path $j | Out-Null
        Add-GovJournalEntry -JournalPath $j -Action 'move' `
                            -Source (Join-Path $root 'stray.docx') -Destination (Join-Path $outside 'stray.docx')

        $r = Invoke-GovUndo -JournalPath $j -LibraryRoot $root
        $r.Flagged  | Should -Be 1
        $r.Restored | Should -Be 0
        ($r.Messages -join ' ') | Should -BeLike '*resolves outside the library root*'
        Test-Path -LiteralPath (Join-Path $outside 'stray.docx') | Should -BeTrue
    }

    It 'flags an entry whose source falls outside a supplied LibraryRoot' {
        $root    = New-TestLibrary -Files @{ 'a.docx' = 'x' }
        $outside = New-TestLibrary -Files @{}
        $j = Join-Path $root 'j.jsonl'
        New-GovJournal -Path $j | Out-Null
        Add-GovJournalEntry -JournalPath $j -Action 'move' `
                            -Source (Join-Path $outside 'a.docx') -Destination (Join-Path $root 'a.docx')

        $r = Invoke-GovUndo -JournalPath $j -LibraryRoot $root
        $r.Flagged  | Should -Be 1
        $r.Restored | Should -Be 0
        Test-Path -LiteralPath (Join-Path $root 'a.docx')    | Should -BeTrue
        Test-Path -LiteralPath (Join-Path $outside 'a.docx') | Should -BeFalse
    }

    It 'restores an entry that stays inside a supplied LibraryRoot' {
        $root = New-TestLibrary -Files @{ 'Sub/a.docx' = 'x' }
        $j = Join-Path $root 'j.jsonl'
        New-GovJournal -Path $j | Out-Null
        Add-GovJournalEntry -JournalPath $j -Action 'move' `
                            -Source (Join-Path $root 'a.docx') -Destination (Join-Path $root 'Sub/a.docx')

        $r = Invoke-GovUndo -JournalPath $j -LibraryRoot $root
        $r.Restored | Should -Be 1
        $r.Flagged  | Should -Be 0
        Test-Path -LiteralPath (Join-Path $root 'a.docx') | Should -BeTrue
    }

    It 'still runs without a LibraryRoot and says containment was not enforced' {
        # Undo must stay usable from a journal alone, so the operator is told
        # rather than blocked.
        $root = New-TestLibrary -Files @{ 'Sub/a.docx' = 'x' }
        $j = Join-Path $root 'j.jsonl'
        New-GovJournal -Path $j | Out-Null
        Add-GovJournalEntry -JournalPath $j -Action 'move' `
                            -Source (Join-Path $root 'a.docx') -Destination (Join-Path $root 'Sub/a.docx')

        $r = Invoke-GovUndo -JournalPath $j
        $r.Restored    | Should -Be 1
        $r.Messages[0] | Should -BeLike '*Containment was not enforced*'
    }
}
