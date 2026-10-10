#Requires -Version 7.0
BeforeAll {
    $script:RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
    Import-Module (Join-Path $script:RepoRoot 'scripts/GovLib/GovLib.psd1') -Force
    Import-Module (Join-Path $PSScriptRoot 'TestHelpers.psm1') -Force
}

Describe 'Get-GovInventory' {
    It 'classifies root files as Root and nested files as Nested' {
        $root = New-TestLibrary -Files @{
            'loose.docx'                              = 'a'
            'Controlled Documents/Governance/in.docx' = 'b'
        }
        $inv = Get-GovInventory -LibraryRoot $root
        ($inv | Where-Object Name -eq 'loose.docx').Zone | Should -Be 'Root'
        ($inv | Where-Object Name -eq 'in.docx').Zone    | Should -Be 'Nested'
    }

    It 'records a relative path using forward slashes' {
        $root = New-TestLibrary -Files @{ 'Controlled Documents/Governance/in.docx' = 'b' }
        (Get-GovInventory -LibraryRoot $root)[0].RelativePath |
            Should -Be 'Controlled Documents/Governance/in.docx'
    }

    It 'leaves ModifiedBy empty because NTFS does not track it' {
        $root = New-TestLibrary -Files @{ 'a.docx' = 'a' }
        (Get-GovInventory -LibraryRoot $root)[0].ModifiedBy | Should -BeNullOrEmpty
    }

    It 'never opens file content' {
        # A file held open with no share permissions cannot be read.
        # Inventory must still succeed, proving it only touches metadata.
        $root = New-TestLibrary -Files @{ 'locked.docx' = 'secret' }
        $full = Join-Path $root 'locked.docx'
        $fs = [System.IO.File]::Open($full, 'Open', 'Read', 'None')
        try {
            $inv = Get-GovInventory -LibraryRoot $root
            ($inv | Where-Object Name -eq 'locked.docx') | Should -Not -BeNullOrEmpty
        }
        finally { $fs.Dispose() }
    }

    It 'throws when the library root does not exist' {
        { Get-GovInventory -LibraryRoot 'C:\nope\missing' } | Should -Throw '*not found*'
    }

    It 'records a folder path using forward slashes for a nested file' {
        $root = New-TestLibrary -Files @{ 'Controlled Documents/Governance/in.docx' = 'b' }
        (Get-GovInventory -LibraryRoot $root)[0].FolderPath |
            Should -Be 'Controlled Documents/Governance'
    }
}

Describe 'Join-GovSharePointExport' {
    BeforeAll {
        function New-ExportCsv {
            param([string] $Content)
            $r = New-TestRoot
            $p = Join-Path $r 'export.csv'
            Set-Content -LiteralPath $p -Value $Content -Encoding utf8NoBOM
            return $p
        }
    }

    It 'fills ModifiedBy from the export' {
        $root = New-TestLibrary -Files @{ 'a.docx' = 'a' }
        $inv  = Get-GovInventory -LibraryRoot $root
        $exp  = New-ExportCsv -Content "Name,Folder path,Modified By`na.docx,,Jane Doe"
        $j = Join-GovSharePointExport -Inventory $inv -ExportPath $exp
        ($j.Rows | Where-Object Name -eq 'a.docx').ModifiedBy | Should -Be 'Jane Doe'
    }

    It 'reports rows present in the export but absent on disk' {
        $root = New-TestLibrary -Files @{ 'a.docx' = 'a' }
        $inv  = Get-GovInventory -LibraryRoot $root
        $exp  = New-ExportCsv -Content "Name,Folder path,Modified By`na.docx,,Jane`ncloud.docx,,Bob"
        (Join-GovSharePointExport -Inventory $inv -ExportPath $exp).OnlyInExport |
            Should -Contain 'cloud.docx'
    }

    It 'reports rows present on disk but absent from the export' {
        $root = New-TestLibrary -Files @{ 'a.docx' = 'a'; 'pending.docx' = 'b' }
        $inv  = Get-GovInventory -LibraryRoot $root
        $exp  = New-ExportCsv -Content "Name,Folder path,Modified By`na.docx,,Jane"
        (Join-GovSharePointExport -Inventory $inv -ExportPath $exp).OnlyOnDisk |
            Should -Contain 'pending.docx'
    }

    It 'throws an actionable error when the export is missing the required Name column' {
        $root = New-TestLibrary -Files @{ 'a.docx' = 'a' }
        $inv  = Get-GovInventory -LibraryRoot $root
        $exp  = New-ExportCsv -Content "FileName,Folder path`na.docx,"
        { Join-GovSharePointExport -Inventory $inv -ExportPath $exp } |
            Should -Throw "*missing the required 'Name' column*"
    }
}
