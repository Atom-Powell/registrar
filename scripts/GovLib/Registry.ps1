#Requires -Version 7.0

$script:GovRegistryRequired = @('documentid','artifacttype','classification','approvalstatus','version')

function Import-GovRegistry {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [string] $Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "ID Registry export not found: $Path"
    }

    $rows = @(Import-Csv -LiteralPath $Path)
    if ($rows.Count -eq 0) {
        $raw = (Get-Content -LiteralPath $Path -Raw)
        if ([string]::IsNullOrWhiteSpace($raw)) {
            throw "ID Registry export is empty (no header row): $Path"
        }
    }

    $header = @()
    if ($rows.Count -gt 0) {
        $header = $rows[0].PSObject.Properties.Name
    }
    else {
        $header = (Get-Content -LiteralPath $Path -TotalCount 1) -split ','
    }
    $lookup = @{}
    foreach ($name in $header) { $lookup[$name.Trim().ToLowerInvariant()] = $name }

    foreach ($required in $script:GovRegistryRequired) {
        if (-not $lookup.ContainsKey($required)) {
            throw "ID Registry export is missing the required column '$required': $Path"
        }
    }

    $result = @{}
    foreach ($row in $rows) {
        # Check for missing trailing columns (short row)
        foreach ($required in $script:GovRegistryRequired) {
            if ($null -eq $row.($lookup[$required])) {
                throw "ID Registry export has a malformed row with fewer columns than the header: $Path"
            }
        }

        $id = ([string]$row.($lookup['documentid'])).Trim()
        if ([string]::IsNullOrEmpty($id)) {
            throw "ID Registry export has a row with a blank Document ID: $Path"
        }
        if ($result.ContainsKey($id)) {
            throw "ID Registry export has a duplicate Document ID '$id': $Path"
        }
        $result[$id] = [pscustomobject]@{
            DocumentId     = $id
            ArtifactType   = ([string]$row.($lookup['artifacttype'])).Trim()
            Classification = ([string]$row.($lookup['classification'])).Trim()
            ApprovalStatus = ([string]$row.($lookup['approvalstatus'])).Trim()
            Version        = ([string]$row.($lookup['version'])).Trim()
        }
    }
    return $result
}
