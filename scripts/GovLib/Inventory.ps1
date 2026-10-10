#Requires -Version 7.0

# FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS. Set on cloud-only OneDrive placeholders.
$script:GovRecallAttribute = 0x00400000

function Get-GovInventory {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [string] $LibraryRoot)

    if (-not (Test-Path -LiteralPath $LibraryRoot -PathType Container)) {
        throw "Library root not found: $LibraryRoot"
    }
    $rootFull = [System.IO.Path]::GetFullPath($LibraryRoot).TrimEnd('\', '/')

    # Metadata only. No call here opens file content.
    Get-ChildItem -LiteralPath $rootFull -Recurse -File -Force | ForEach-Object {
        $relative = $_.FullName.Substring($rootFull.Length).TrimStart('\', '/') -replace '\\', '/'
        $folder   = (Split-Path -Parent $relative) -replace '\\', '/'
        [pscustomobject]@{
            Name          = $_.Name
            FolderPath    = $folder
            RelativePath  = $relative
            Modified      = $_.LastWriteTimeUtc.ToString('o')
            ModifiedTicks = $_.LastWriteTimeUtc.Ticks
            ModifiedBy    = ''
            FileType      = $_.Extension.TrimStart('.')
            SizeBytes     = $_.Length
            IsPlaceholder = (([int]$_.Attributes -band $script:GovRecallAttribute) -ne 0)
            Zone          = if ([string]::IsNullOrEmpty($folder)) { 'Root' } else { 'Nested' }
        }
    }
}

function Join-GovSharePointExport {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [object[]] $Inventory,
        [Parameter(Mandatory)] [string]   $ExportPath
    )

    if (-not (Test-Path -LiteralPath $ExportPath -PathType Leaf)) {
        throw "SharePoint export not found: $ExportPath"
    }

    $export = @(Import-Csv -LiteralPath $ExportPath)
    $header = if ($export.Count -gt 0) {
        $export[0].PSObject.Properties.Name
    }
    else {
        (Get-Content -LiteralPath $ExportPath -TotalCount 1) -split ','
    }
    if ($header -notcontains 'Name') {
        throw "SharePoint export is missing the required 'Name' column: $ExportPath"
    }

    $byKey  = @{}
    foreach ($row in $export) {
        $folder = ''
        if ($row.PSObject.Properties.Name -contains 'Folder path') {
            $folder = ([string]$row.'Folder path').Trim().Trim('/')
        }
        $key = if ($folder) { "$folder/$($row.Name)" } else { [string]$row.Name }
        $byKey[$key.ToLowerInvariant()] = $row
    }

    $seen = @{}
    $rows = foreach ($item in $Inventory) {
        $key = $item.RelativePath.ToLowerInvariant()
        if ($byKey.ContainsKey($key)) {
            $seen[$key] = $true
            $match = $byKey[$key]
            if ($match.PSObject.Properties.Name -contains 'Modified By') {
                $item.ModifiedBy = [string]$match.'Modified By'
            }
        }
        $item
    }

    $onlyInExport = foreach ($key in $byKey.Keys) {
        if (-not $seen.ContainsKey($key)) { $byKey[$key].Name }
    }
    $onlyOnDisk = foreach ($item in $Inventory) {
        if (-not $byKey.ContainsKey($item.RelativePath.ToLowerInvariant())) { $item.Name }
    }

    return [pscustomobject]@{
        Rows         = @($rows)
        OnlyInExport = @($onlyInExport)
        OnlyOnDisk   = @($onlyOnDisk)
    }
}
