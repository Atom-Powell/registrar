#Requires -Version 7.0
BeforeAll {
    $script:RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
    Import-Module (Join-Path $script:RepoRoot 'scripts/GovLib/GovLib.psd1') -Force
    Import-Module (Join-Path $PSScriptRoot 'TestHelpers.psm1') -Force
}

Describe 'New-GovRunContext' {
    It 'refuses an OutputRoot inside the library root' {
        $root = New-TestLibrary -Files @{ 'a.docx' = 'x' }
        $out  = Join-Path $root 'runs'
        { New-GovRunContext -Mode 'Inventory' -LibraryRoot $root -RepoRoot (New-TestRoot) -OutputRoot $out } |
            Should -Throw '*inside the library*'
    }

    It 'throws when the library root does not exist for a non-Undo mode' {
        { New-GovRunContext -Mode 'Inventory' -LibraryRoot 'C:\nope\missing' -RepoRoot (New-TestRoot) -OutputRoot (New-TestRoot) } |
            Should -Throw '*not found*'
    }

    It 'does not require a library root for Undo mode' {
        $repoRoot = New-TestRoot
        $outRoot  = New-TestRoot
        $ctx = New-GovRunContext -Mode 'Undo' -RepoRoot $repoRoot -OutputRoot $outRoot
        $ctx.LibraryRoot | Should -BeNullOrEmpty
        Test-Path -LiteralPath $ctx.RunDir -PathType Container | Should -BeTrue
    }

    It 'defaults OutputRoot to a gov-runs directory under RepoRoot' {
        $root     = New-TestLibrary -Files @{ 'a.docx' = 'x' }
        $repoRoot = New-TestRoot
        $ctx = New-GovRunContext -Mode 'Inventory' -LibraryRoot $root -RepoRoot $repoRoot
        $ctx.OutputRoot | Should -Be (Join-Path $repoRoot 'gov-runs')
    }

    It 'defaults BaselinePath to config/library-baseline.json under RepoRoot' {
        $root     = New-TestLibrary -Files @{ 'a.docx' = 'x' }
        $repoRoot = New-TestRoot
        $ctx = New-GovRunContext -Mode 'Inventory' -LibraryRoot $root -RepoRoot $repoRoot -OutputRoot (New-TestRoot)
        $ctx.BaselinePath | Should -Be (Join-Path $repoRoot 'config/library-baseline.json')
    }

    It 'honors an explicitly supplied BaselinePath' {
        $root     = New-TestLibrary -Files @{ 'a.docx' = 'x' }
        $custom   = Join-Path (New-TestRoot) 'custom-baseline.json'
        $ctx = New-GovRunContext -Mode 'Inventory' -LibraryRoot $root -RepoRoot (New-TestRoot) `
                                  -OutputRoot (New-TestRoot) -BaselinePath $custom
        $ctx.BaselinePath | Should -Be $custom
    }

    It 'creates the run directory under OutputRoot/runs/<stamp>' {
        $root = New-TestLibrary -Files @{ 'a.docx' = 'x' }
        $ctx = New-GovRunContext -Mode 'Inventory' -LibraryRoot $root -RepoRoot (New-TestRoot) -OutputRoot (New-TestRoot)
        Test-Path -LiteralPath $ctx.RunDir -PathType Container | Should -BeTrue
        $ctx.RunDir | Should -Be (Join-Path (Join-Path $ctx.OutputRoot 'runs') $ctx.Stamp)
    }

    It 'stamps the run with a UTC yyyyMMdd-HHmmss timestamp' {
        $root = New-TestLibrary -Files @{ 'a.docx' = 'x' }
        $ctx = New-GovRunContext -Mode 'Inventory' -LibraryRoot $root -RepoRoot (New-TestRoot) -OutputRoot (New-TestRoot)
        $ctx.Stamp | Should -Match '^\d{8}-\d{6}$'
    }

    It 'defaults ArtifactTypesPath to config/artifact-types.json under RepoRoot' {
        $root     = New-TestLibrary -Files @{ 'a.docx' = 'x' }
        $repoRoot = New-TestRoot
        $ctx = New-GovRunContext -Mode 'Inventory' -LibraryRoot $root -RepoRoot $repoRoot -OutputRoot (New-TestRoot)
        $ctx.ArtifactTypesPath | Should -Be (Join-Path $repoRoot 'config/artifact-types.json')
    }

    It 'honors an explicitly supplied ArtifactTypesPath' {
        $root   = New-TestLibrary -Files @{ 'a.docx' = 'x' }
        $custom = Join-Path (New-TestRoot) 'custom-artifact-types.json'
        $ctx = New-GovRunContext -Mode 'Inventory' -LibraryRoot $root -RepoRoot (New-TestRoot) `
                                  -OutputRoot (New-TestRoot) -ArtifactTypesPath $custom
        $ctx.ArtifactTypesPath | Should -Be $custom
    }

    It 'defaults RepositoryTargetsPath to config/repository-targets.json under RepoRoot' {
        $root     = New-TestLibrary -Files @{ 'a.docx' = 'x' }
        $repoRoot = New-TestRoot
        $ctx = New-GovRunContext -Mode 'Inventory' -LibraryRoot $root -RepoRoot $repoRoot -OutputRoot (New-TestRoot)
        $ctx.RepositoryTargetsPath | Should -Be (Join-Path $repoRoot 'config/repository-targets.json')
    }

    It 'honors an explicitly supplied RepositoryTargetsPath' {
        $root   = New-TestLibrary -Files @{ 'a.docx' = 'x' }
        $custom = Join-Path (New-TestRoot) 'custom-repository-targets.json'
        $ctx = New-GovRunContext -Mode 'Inventory' -LibraryRoot $root -RepoRoot (New-TestRoot) `
                                  -OutputRoot (New-TestRoot) -RepositoryTargetsPath $custom
        $ctx.RepositoryTargetsPath | Should -Be $custom
    }

    It 'computes DefaultJournalPath under RunDir named with the stamp' {
        $root = New-TestLibrary -Files @{ 'a.docx' = 'x' }
        $ctx = New-GovRunContext -Mode 'Inventory' -LibraryRoot $root -RepoRoot (New-TestRoot) -OutputRoot (New-TestRoot)
        $ctx.DefaultJournalPath | Should -Be (Join-Path $ctx.RunDir "undo-manifest-$($ctx.Stamp).jsonl")
    }

    It 'computes DefaultJournalPath for Undo mode too, without a library root' {
        $ctx = New-GovRunContext -Mode 'Undo' -RepoRoot (New-TestRoot) -OutputRoot (New-TestRoot)
        $ctx.DefaultJournalPath | Should -Be (Join-Path $ctx.RunDir "undo-manifest-$($ctx.Stamp).jsonl")
    }
}

Describe 'Import-GovRegistryContext' {
    It 'returns null Registry and ArtifactTypes when RegistryPath is not supplied' {
        $result = Import-GovRegistryContext -ArtifactTypesPath (Join-Path $script:RepoRoot 'config/artifact-types.json')
        $result.Registry      | Should -BeNullOrEmpty
        $result.ArtifactTypes | Should -BeNullOrEmpty
    }

    It 'returns null Registry and ArtifactTypes when RegistryPath is an empty string' {
        $result = Import-GovRegistryContext -RegistryPath '' `
                      -ArtifactTypesPath (Join-Path $script:RepoRoot 'config/artifact-types.json')
        $result.Registry      | Should -BeNullOrEmpty
        $result.ArtifactTypes | Should -BeNullOrEmpty
    }

    It 'loads both Registry and ArtifactTypes together when RegistryPath is supplied' {
        $registryPath = Join-Path $script:RepoRoot 'fixtures/registry-sample.csv'
        $typesPath    = Join-Path $script:RepoRoot 'config/artifact-types.json'
        $result = Import-GovRegistryContext -RegistryPath $registryPath -ArtifactTypesPath $typesPath
        $result.Registry['ORG-GOV-DS-016'].ApprovalStatus | Should -Be 'Draft'
        $result.ArtifactTypes['DS'] | Should -Be 'GOV'
    }
}
