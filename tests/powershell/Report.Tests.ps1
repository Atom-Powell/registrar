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

    It 'reports nothing for registry rows when ArtifactTypes is omitted' {
        # Without ArtifactTypes, identity can never be resolved, so seenIds
        # can never be populated. Reporting every registry row as missing in
        # that situation would be a false positive on every row, not a real
        # finding.
        $root = New-TestLibrary -Files @{ 'a.docx' = 'x' }
        $r = New-GovReport -LibraryRoot $root -Baseline $script:Baseline -Registry $script:Registry
        $r.RegistryRowsWithNoFile | Should -BeNullOrEmpty
    }

    It 'does not report a case-variant subfolder of a baseline folder as unknown' {
        # 'controlled documents/Governance' is itself an exact baseline path,
        # so it would match via the already-case-insensitive -eq comparison
        # even without the fix. Go one level deeper, into a folder that is
        # only a subfolder of a baseline path, to exercise the StartsWith
        # comparison the fix corrected.
        $root = New-TestLibrary -Files @{ 'controlled documents/Governance/Sub/a.docx' = 'x' }
        (New-GovReport -LibraryRoot $root -Baseline $script:Baseline).UnknownFolders |
            Should -Not -Contain 'controlled documents/Governance/Sub'
    }
}
