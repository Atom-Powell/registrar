#Requires -Version 7.0
BeforeAll {
    $script:RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
    Import-Module (Join-Path $script:RepoRoot 'scripts/GovLib/GovLib.psd1') -Force
    Import-Module (Join-Path $PSScriptRoot 'TestHelpers.psm1') -Force

    function New-RegistryCsv {
        param([string] $Content)
        $root = New-TestRoot
        $path = Join-Path $root 'registry.csv'
        Set-Content -LiteralPath $path -Value $Content -Encoding utf8NoBOM
        return $path
    }
}

Describe 'Import-GovRegistry' {
    It 'reads the repository fixture export' {
        $path = Join-Path $script:RepoRoot 'fixtures/registry-sample.csv'
        $reg = Import-GovRegistry -Path $path
        $reg['ORG-GOV-DS-016'].ApprovalStatus | Should -Be 'Draft'
        $reg['ORG-GOV-DS-017'].ApprovalStatus | Should -Be 'Retired'
        $reg['ORG-MST-SCL-001'].ArtifactType  | Should -Be 'SCL'
    }

    It 'throws on an empty file' {
        $p = New-RegistryCsv -Content ''
        { Import-GovRegistry -Path $p } | Should -Throw '*empty*'
    }

    It 'throws when a required column is missing' {
        $p = New-RegistryCsv -Content "documentId,artifactType,classification,version`nORG-GOV-DS-001,DS,Client-Facing,v1.0"
        { Import-GovRegistry -Path $p } | Should -Throw '*approvalStatus*'
    }

    It 'throws on a blank Document ID' {
        $p = New-RegistryCsv -Content "documentId,artifactType,classification,approvalStatus,version`n,DS,Client-Facing,Draft,v1.0"
        { Import-GovRegistry -Path $p } | Should -Throw '*blank Document ID*'
    }

    It 'throws on a duplicate Document ID' {
        $p = New-RegistryCsv -Content "documentId,artifactType,classification,approvalStatus,version`nORG-GOV-DS-001,DS,Client-Facing,Draft,v1.0`nORG-GOV-DS-001,DS,Client-Facing,Draft,v1.0"
        { Import-GovRegistry -Path $p } | Should -Throw '*duplicate*'
    }

    It 'matches column names case-insensitively' {
        $p = New-RegistryCsv -Content "DocumentID,ArtifactType,Classification,ApprovalStatus,Version`nORG-GOV-DS-001,DS,Client-Facing,Draft,v1.0"
        (Import-GovRegistry -Path $p)['ORG-GOV-DS-001'].Version | Should -Be 'v1.0'
    }

    It 'throws on a short row with missing trailing columns' {
        $p = New-RegistryCsv -Content "documentId,artifactType,classification,approvalStatus,version`nORG-GOV-DS-001,DS,Client-Facing"
        { Import-GovRegistry -Path $p } | Should -Throw '*malformed row*'
    }

    It 'accepts a row with present but empty fields' {
        $p = New-RegistryCsv -Content "documentId,artifactType,classification,approvalStatus,version`nORG-GOV-DS-001,DS,,Draft,v1.0"
        $reg = Import-GovRegistry -Path $p
        $reg['ORG-GOV-DS-001'].Classification | Should -Be ''
    }

    It 'returns an empty hashtable for a header-only file' {
        $p = New-RegistryCsv -Content "documentId,artifactType,classification,approvalStatus,version"
        $reg = Import-GovRegistry -Path $p
        $reg.Count | Should -Be 0
    }
}
