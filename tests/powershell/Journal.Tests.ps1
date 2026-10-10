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

    It 'returns an empty array from an empty journal' {
        $p = Join-Path (New-TestRoot) 'undo-manifest.jsonl'
        New-GovJournal -Path $p | Out-Null
        $entries = Read-GovJournal -Path $p
        $entries.Count | Should -Be 0
    }

    It 'preserves existing entries when New-GovJournal is called again' {
        $p = Join-Path (New-TestRoot) 'undo-manifest.jsonl'
        New-GovJournal -Path $p | Out-Null
        Add-GovJournalEntry -JournalPath $p -Action 'move' -Source 'a.docx' -Destination 'X/a.docx'
        Add-GovJournalEntry -JournalPath $p -Action 'rename' -Source 'b.docx' -Destination 'c.docx'
        New-GovJournal -Path $p | Out-Null
        $entries = Read-GovJournal -Path $p
        $entries.Count          | Should -Be 2
        $entries[0].Action      | Should -Be 'move'
        $entries[1].Destination | Should -Be 'c.docx'
    }
}
