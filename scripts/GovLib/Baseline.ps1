#Requires -Version 7.0

$script:GovValidStatus = @('governed', 'unconfirmed', 'notDefined', 'blocked')

# Translation of the config's disposition action vocabulary into the
# ActionRequired values the plan workbook uses. Config decides, code renders.
$script:GovDispositionActions = @{
    'leaveInPlace'   = 'Leave in Place'
    'escalate'       = 'Escalate'
    'reviewRequired' = 'Review Required'
}

function Import-GovBaseline {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [string] $Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Baseline config not found: $Path"
    }

    try {
        $baseline = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    }
    catch {
        throw "Baseline config is not valid JSON: $Path. $($_.Exception.Message)"
    }

    foreach ($section in @('baselineVersion','nestingOrder','folderTree','placementRules',
                           'dispositions','fileNaming','identification','doNotTouch')) {
        if (-not $baseline.PSObject.Properties.Name.Contains($section)) {
            throw "Baseline config is missing required section '$section': $Path"
        }
    }

    foreach ($entry in $baseline.folderTree) {
        Assert-GovStatus -Value $entry.status -Context "folderTree entry '$($entry.path)'"
    }
    foreach ($mapping in $baseline.placementRules.mappings) {
        Assert-GovStatus -Value $mapping.status -Context "placementRules mapping"
    }
    foreach ($property in $baseline.dispositions.PSObject.Properties) {
        $entry   = $property.Value
        $context = "dispositions entry '$($property.Name)'"
        $names   = @($entry.PSObject.Properties.Name)

        if ($names -notcontains 'status') { throw "Missing 'status' in $context." }
        Assert-GovStatus -Value $entry.status -Context $context

        if ($names -notcontains 'action') { throw "Missing 'action' in $context." }
        Assert-GovDispositionAction -Value $entry.action -Context $context
    }

    Assert-GovStatus -Value $baseline.fileNaming.status -Context 'fileNaming'
    Assert-GovStatus -Value $baseline.identification.status -Context 'identification'

    return $baseline
}

function Assert-GovStatus {
    param([string] $Value, [string] $Context)
    if ($Value -notin $script:GovValidStatus) {
        throw "Unknown status '$Value' in $Context. Valid values: $($script:GovValidStatus -join ', ')."
    }
}

function Assert-GovDispositionAction {
    param([string] $Value, [string] $Context)
    if (-not $script:GovDispositionActions.ContainsKey($Value)) {
        throw "Unknown action '$Value' in $Context. Valid values: $($script:GovDispositionActions.Keys -join ', ')."
    }
}
