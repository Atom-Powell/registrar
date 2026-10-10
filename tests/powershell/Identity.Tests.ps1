#Requires -Version 7.0
BeforeAll {
    $script:RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
    Import-Module (Join-Path $script:RepoRoot 'scripts/GovLib/GovLib.psd1') -Force
    $script:Types    = Import-GovArtifactTypes -Path (Join-Path $script:RepoRoot 'config/artifact-types.json')
    $script:Registry = Import-GovRegistry -Path (Join-Path $script:RepoRoot 'fixtures/registry-sample.csv')
}

Describe 'Import-GovArtifactTypes' {
    It 'maps artifact codes to their categories' {
        $script:Types['DS']  | Should -Be 'GOV'
        $script:Types['TCL'] | Should -Be 'TRN'
        $script:Types['SCL'] | Should -Be 'MST'
    }
}

Describe 'Get-GovDocumentIdFromName' {
    It 'extracts an ID embedded in a filename' {
        Get-GovDocumentIdFromName -Name 'ORG-GOV-DS-016 - Northwind Data Sheet.docx' |
            Should -Be 'ORG-GOV-DS-016'
    }
    It 'returns null when no ID is present' {
        Get-GovDocumentIdFromName -Name 'ORG - Some Artifact - Client - v1.0.docx' |
            Should -BeNullOrEmpty
    }
    It 'returns null for a partial or malformed ID' {
        Get-GovDocumentIdFromName -Name 'ORG-GOV-DS.docx' | Should -BeNullOrEmpty
    }
    It 'returns null for a four-digit sequence (no truncation to three)' {
        Get-GovDocumentIdFromName -Name 'ORG-GOV-DS-0165 - Thing.docx' | Should -BeNullOrEmpty
    }
    It 'returns null when a character is glued to the leading ORG' {
        Get-GovDocumentIdFromName -Name 'XORG-GOV-DS-016 - Thing.docx' | Should -BeNullOrEmpty
    }
    It 'accepts a legitimate separator before the ID' {
        Get-GovDocumentIdFromName -Name 'copy of ORG-GOV-DS-016 - Thing.docx' | Should -Be 'ORG-GOV-DS-016'
    }
}

Describe 'Resolve-GovIdentity' {
    It 'resolves a registered ID to its registry status and category' {
        $r = Resolve-GovIdentity -Name 'ORG-GOV-DS-016 - Thing.docx' -Registry $script:Registry -ArtifactTypes $script:Types
        $r.Resolved       | Should -BeTrue
        $r.Category       | Should -Be 'GOV'
        $r.ApprovalStatus | Should -Be 'Draft'
    }

    It 'refuses an illegal category and code pairing' {
        $r = Resolve-GovIdentity -Name 'ORG-GOV-TCL-001 - Thing.docx' -Registry $script:Registry -ArtifactTypes $script:Types
        $r.Resolved | Should -BeFalse
        $r.Reason   | Should -BeLike '*pairing*'
    }

    It 'refuses an unknown artifact code' {
        $r = Resolve-GovIdentity -Name 'ORG-GOV-ZZZ-001 - Thing.docx' -Registry $script:Registry -ArtifactTypes $script:Types
        $r.Resolved | Should -BeFalse
        $r.Reason   | Should -BeLike '*not a known artifact code*'
    }

    It 'refuses an ID that is absent from the registry' {
        $r = Resolve-GovIdentity -Name 'ORG-GOV-DS-999 - Thing.docx' -Registry $script:Registry -ArtifactTypes $script:Types
        $r.Resolved | Should -BeFalse
        $r.Reason   | Should -BeLike '*not in the ID Registry*'
    }

    It 'reports no ID when the filename carries none' {
        $r = Resolve-GovIdentity -Name 'random notes.docx' -Registry $script:Registry -ArtifactTypes $script:Types
        $r.Resolved | Should -BeFalse
        $r.Reason   | Should -BeLike '*no Document ID*'
    }

    It 'rejects a four-digit sequence and reports no Document ID found' {
        $r = Resolve-GovIdentity -Name 'ORG-GOV-DS-0165 - Thing.docx' -Registry $script:Registry -ArtifactTypes $script:Types
        $r.Resolved | Should -BeFalse
        $r.Reason   | Should -BeLike '*no Document ID*'
    }
}
