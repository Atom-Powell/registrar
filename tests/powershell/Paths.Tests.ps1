#Requires -Version 7.0
BeforeAll {
    $script:RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
    Import-Module (Join-Path $script:RepoRoot 'scripts/GovLib/GovLib.psd1') -Force
    $script:Baseline = Import-GovBaseline -Path (Join-Path $script:RepoRoot 'config/library-baseline.json')
}

Describe 'Test-GovProtectedPath' {
    It 'protects files under a wildcard pattern' {
        Test-GovProtectedPath -RelativePath 'Architecture Decision Records/ADR-001.docx' -Baseline $script:Baseline |
            Should -BeTrue
    }
    It 'protects nested files under a wildcard pattern' {
        Test-GovProtectedPath -RelativePath 'Templates/Approved/sub/thing.docx' -Baseline $script:Baseline |
            Should -BeTrue
    }
    It 'protects an exact file pattern' {
        Test-GovProtectedPath -RelativePath 'Document Registry/ORG-ID-Registry.xlsx' -Baseline $script:Baseline |
            Should -BeTrue
    }
    It 'does not protect an unrelated file' {
        Test-GovProtectedPath -RelativePath 'stray-file.docx' -Baseline $script:Baseline |
            Should -BeFalse
    }
    It 'matches case-insensitively' {
        Test-GovProtectedPath -RelativePath 'architecture decision records/x.docx' -Baseline $script:Baseline |
            Should -BeTrue
    }
    It 'treats forward and back slashes as equivalent' {
        Test-GovProtectedPath -RelativePath 'Architecture Decision Records\ADR-001.docx' -Baseline $script:Baseline |
            Should -BeTrue
    }
}

Describe 'Get-GovProtectionReason' {
    It 'returns the configured reason for a protected path' {
        Get-GovProtectionReason -RelativePath 'Document Registry/ORG-ID-Registry.xlsx' -Baseline $script:Baseline |
            Should -BeLike '*Authoritative state*'
    }
    It 'returns null for an unprotected path' {
        Get-GovProtectionReason -RelativePath 'stray.docx' -Baseline $script:Baseline | Should -BeNullOrEmpty
    }
}

Describe 'Test-GovPathUnderRoot' {
    It 'returns true for a path inside the root' {
        Test-GovPathUnderRoot -Path 'C:\lib\sub\file.txt' -Root 'C:\lib' | Should -BeTrue
    }
    It 'returns false for a sibling directory with a shared prefix' {
        Test-GovPathUnderRoot -Path 'C:\library-other\file.txt' -Root 'C:\lib' | Should -BeFalse
    }
    It 'returns false for a path outside the root' {
        Test-GovPathUnderRoot -Path 'C:\elsewhere\file.txt' -Root 'C:\lib' | Should -BeFalse
    }
}

Describe 'Test-GovPathLength' {
    It 'accepts a short path' {
        Test-GovPathLength -Path 'C:\lib\a.docx' | Should -BeTrue
    }
    It 'rejects a path over the limit' {
        $long = 'C:\lib\' + ('a' * 300) + '.docx'
        Test-GovPathLength -Path $long | Should -BeFalse
    }
    It 'honors an explicit limit' {
        Test-GovPathLength -Path 'C:\lib\abcdef.docx' -MaxLength 5 | Should -BeFalse
    }
}
