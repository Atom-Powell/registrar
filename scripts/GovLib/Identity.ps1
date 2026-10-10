#Requires -Version 7.0

# Matches the Document ID scheme ORG-[CATEGORY]-[ARTIFACT CODE]-[NNN].
$script:GovIdPattern = '(?<![A-Z0-9])ORG-[A-Z]{3}-[A-Z]{2,4}-\d{3}(?!\d)'

function Import-GovArtifactTypes {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [string] $Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Artifact types config not found: $Path"
    }
    $json = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    $map = @{}
    foreach ($type in $json.artifactTypes) {
        $map[$type.code.ToUpperInvariant()] = $type.category
    }
    if ($map.Count -eq 0) {
        throw "Artifact types config contained no artifactTypes entries: $Path"
    }
    return $map
}

function Get-GovDocumentIdFromName {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [string] $Name)

    $m = [regex]::Match($Name.ToUpperInvariant(), $script:GovIdPattern)
    if ($m.Success) { return $m.Value }
    return $null
}

function Resolve-GovIdentity {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string]    $Name,
        [Parameter(Mandatory)] [hashtable] $Registry,
        [Parameter(Mandatory)] [hashtable] $ArtifactTypes
    )

    $result = [pscustomobject]@{
        Resolved       = $false
        DocumentId     = $null
        Category       = $null
        ApprovalStatus = $null
        Reason         = $null
    }

    $id = Get-GovDocumentIdFromName -Name $Name
    if (-not $id) {
        $result.Reason = 'Filename carries no Document ID.'
        return $result
    }
    $result.DocumentId = $id

    $parts    = $id -split '-'
    $category = $parts[1]
    $code     = $parts[2]

    if (-not $ArtifactTypes.ContainsKey($code)) {
        $result.Reason = "'$code' is not a known artifact code."
        return $result
    }
    if ($ArtifactTypes[$code] -ne $category) {
        $result.Reason = "Illegal category and code pairing: '$code' belongs to category '$($ArtifactTypes[$code])', not '$category'."
        return $result
    }
    if (-not $Registry.ContainsKey($id)) {
        $result.Reason = "'$id' is not in the ID Registry export."
        return $result
    }

    $result.Category       = $category
    $result.ApprovalStatus = $Registry[$id].ApprovalStatus
    $result.Resolved       = $true
    return $result
}
