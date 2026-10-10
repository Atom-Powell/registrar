#Requires -Version 7.0

function Get-GovDispositionAction {
    <#
      Resolves baseline.dispositions.<Key>.action to an ActionRequired value.
      Falls back to Default when the key or its action is absent, so a baseline
      that predates a disposition key still plans.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [object] $Baseline,
        [Parameter(Mandatory)] [string] $Key,
        [string] $Default = 'Review Required'
    )

    if (@($Baseline.PSObject.Properties.Name) -notcontains 'dispositions') { return $Default }
    $dispositions = $Baseline.dispositions
    if (-not $dispositions) { return $Default }
    if (@($dispositions.PSObject.Properties.Name) -notcontains $Key) { return $Default }

    $entry = $dispositions.$Key
    if (-not $entry -or @($entry.PSObject.Properties.Name) -notcontains 'action') { return $Default }

    $action = [string]$entry.action
    if ([string]::IsNullOrWhiteSpace($action)) { return $Default }
    if (-not $script:GovDispositionActions.ContainsKey($action)) {
        # Import-GovBaseline validates this, so reaching here means the baseline
        # was built in memory rather than loaded. Fail loudly either way.
        throw "Unknown disposition action '$action' for dispositions.$Key. Valid values: $($script:GovDispositionActions.Keys -join ', ')."
    }
    return $script:GovDispositionActions[$action]
}

function Get-GovPlacementTarget {
    <#
      Returns a PSCustomObject with TargetPath, Status, RuleSource for a
      resolved identity.

      Selection is by specificity, not by declaration order:
      placementRules.decidingFieldsInPriorityOrder names the fields a mapping
      may constrain, and the mapping constraining the most of them wins. A
      first-match rule would silently depend on how the mappings happen to be
      ordered in config.
    #>
    param(
        [Parameter(Mandatory)] [object] $Baseline,
        [Parameter(Mandatory)] [string] $ApprovalStatus,
        [string] $Category
    )

    $decidingFields = @($Baseline.placementRules.decidingFieldsInPriorityOrder)
    if ($decidingFields.Count -eq 0) { return $null }

    $actual = @{
        'approvalStatus' = $ApprovalStatus
        'idCategory'     = $Category
    }

    $best      = $null
    $bestScore = -1

    foreach ($mapping in $Baseline.placementRules.mappings) {
        $constrained = @($mapping.match.PSObject.Properties.Name)

        # A mapping constrained by a field this code cannot evaluate asks for a
        # decision it cannot make, so it fails closed rather than matching.
        $unknown = @($constrained | Where-Object { $_ -notin $decidingFields -or -not $actual.ContainsKey($_) })
        if ($unknown.Count -gt 0) { continue }

        $score   = 0
        $matched = $true
        foreach ($field in $decidingFields) {
            if ($constrained -notcontains $field) { continue }
            if ($mapping.match.$field -ne $actual[$field]) { $matched = $false; break }
            $score++
        }
        if (-not $matched) { continue }

        if ($score -gt $bestScore) {
            $best      = $mapping
            $bestScore = $score
        }
    }

    if (-not $best) { return $null }

    return [pscustomobject]@{
        TargetPath = $best.targetPath
        Status     = $best.status
        RuleSource = $best.ruleSource
    }
}

function New-GovPlan {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [object[]]  $Inventory,
        [Parameter(Mandatory)] [object]    $Baseline,
        [Parameter(Mandatory)] [string]    $LibraryRoot,
        [hashtable] $Registry,
        [hashtable] $ArtifactTypes,
        [ValidateSet('None','DocumentId','Custom')] [string] $TagMode = 'None',
        [string] $TagValue,
        [ValidateSet('Prefix','Suffix')] [string] $TagPosition = 'Prefix',
        [string] $TagScope
    )

    $namingGoverned = ($Baseline.fileNaming.status -eq 'governed')

    foreach ($item in @($Inventory | Where-Object Zone -eq 'Root')) {

        $row = [pscustomobject]@{
            Name                = $item.Name
            FolderPath          = $item.FolderPath
            RelativePath        = $item.RelativePath
            Modified            = $item.Modified
            SourceStamp         = (Get-GovSourceStamp -Ticks $item.ModifiedTicks -SizeBytes $item.SizeBytes)
            ModifiedBy          = $item.ModifiedBy
            FileType            = $item.FileType
            SuggestedFolder     = ''
            FolderExists        = $false
            ActionRequired      = 'Review Required'
            RuleSource          = ''
            Notes               = ''
            Approved            = ''
            Prefix              = ''
            Suffix              = ''
            NewName             = ''
        }

        $reason = Get-GovProtectionReason -RelativePath $item.RelativePath -Baseline $Baseline
        if ($reason) {
            $row.ActionRequired = 'Leave in Place'
            $row.RuleSource     = 'doNotTouch'
            $row.Notes          = $reason
            $row
            continue
        }

        $identity = $null
        if ($Registry -and $ArtifactTypes) {
            $identity = Resolve-GovIdentity -Name $item.Name -Registry $Registry -ArtifactTypes $ArtifactTypes
        }

        if (-not $identity -or -not $identity.Resolved) {
            $row.Notes = if ($identity) { $identity.Reason } else { 'No ID Registry export supplied.' }
            $row.RuleSource = 'identification status blocked; human classification required (SOP Step 3)'
            if ($identity) {
                # An identity attempt that failed is the unrecognized case:
                # no Document ID in the name, an illegal ID, or an ID the
                # registry does not carry. With no registry supplied at all
                # there is no basis for that judgment, so the default stands.
                $row.ActionRequired = Get-GovDispositionAction -Baseline $Baseline -Key 'unrecognized'
                $row.RuleSource     = "dispositions.unrecognized; $($row.RuleSource)"
            }
            $row = Set-GovTagColumns -Row $row -TagMode $TagMode -TagValue $TagValue `
                                     -TagPosition $TagPosition -TagScope $TagScope `
                                     -NamingGoverned $namingGoverned -LibraryRoot $LibraryRoot -DocumentId $null
            $row
            continue
        }

        $placement = Get-GovPlacementTarget -Baseline $Baseline -ApprovalStatus $identity.ApprovalStatus -Category $identity.Category

        if (-not $placement -or $placement.Status -eq 'notDefined' -or -not $placement.TargetPath) {
            # Retired is the live case: its mapping carries a null target and
            # status notDefined, and dispositions.retired says what to do about
            # it. A file with no matching mapping at all is a different thing
            # the config does not speak to, so it keeps the conservative default.
            $row.ActionRequired = if ($placement -and $placement.Status -eq 'notDefined') {
                Get-GovDispositionAction -Baseline $Baseline -Key 'retired' -Default 'Leave in Place'
            } else { 'Leave in Place' }
            $row.RuleSource     = "placement status notDefined for approvalStatus '$($identity.ApprovalStatus)'"
            $row.Notes          = 'No governed target folder. Leave in place, log, escalate to Registry Owner.'
            $row
            continue
        }

        # Apply checks protection on the destination as well as the source. If
        # Plan checked only the source it would confidently propose a move that
        # Apply refuses every time, with a cited rule source and a human
        # signature on it. Escalate instead of proposing a contradiction.
        $targetRelative      = ($placement.TargetPath.TrimEnd('/') + '/' + $item.Name)
        $destinationReason   = Get-GovProtectionReason -RelativePath $targetRelative -Baseline $Baseline
        if ($destinationReason) {
            $row.ActionRequired = 'Escalate'
            $row.RuleSource     = "doNotTouch on the destination; $($placement.RuleSource)"
            $row.Notes          = ("Governed destination '$($placement.TargetPath)' is protected, " +
                                   "so no move can be proposed. $destinationReason")
            $row
            continue
        }

        $targetFull       = Join-Path $LibraryRoot ($placement.TargetPath -replace '/', '\')
        $row.SuggestedFolder = $placement.TargetPath
        $row.FolderExists    = Test-Path -LiteralPath $targetFull -PathType Container
        $row.ActionRequired  = if ($row.FolderExists) { 'Move to Existing Folder' } else { 'Create Folder and Move' }
        $row.RuleSource      = $placement.RuleSource
        if ($placement.Status -eq 'unconfirmed') {
            $row.Notes = 'Target path status is unconfirmed. Verify against the live library before approving.'
        }

        $row = Set-GovTagColumns -Row $row -TagMode $TagMode -TagValue $TagValue `
                                 -TagPosition $TagPosition -TagScope $TagScope `
                                 -NamingGoverned $namingGoverned -LibraryRoot $LibraryRoot `
                                 -DocumentId $identity.DocumentId
        $row
    }
}

function Set-GovTagColumns {
    param(
        [Parameter(Mandatory)] [object] $Row,
        [string] $TagMode,
        [string] $TagValue,
        [string] $TagPosition,
        [string] $TagScope,
        [bool]   $NamingGoverned,
        [string] $LibraryRoot,
        [string] $DocumentId
    )

    if ($TagMode -eq 'None') { return $Row }

    if (-not $NamingGoverned) {
        $Row.Notes = (@($Row.Notes, 'Tagging skipped: baseline fileNaming status is notDefined.') |
                      Where-Object { $_ }) -join ' '
        return $Row
    }

    if ($TagScope -and -not $Row.RelativePath.StartsWith($TagScope, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $Row
    }

    $tag = if ($TagMode -eq 'DocumentId') { $DocumentId } else { $TagValue }
    if (-not $tag) {
        $Row.Notes = (@($Row.Notes, 'Tagging skipped: no tag value available for this row.') |
                      Where-Object { $_ }) -join ' '
        return $Row
    }

    $stem      = [System.IO.Path]::GetFileNameWithoutExtension($Row.Name)
    $extension = [System.IO.Path]::GetExtension($Row.Name)
    $newName   = if ($TagPosition -eq 'Prefix') { "$tag - $stem$extension" } else { "$stem - $tag$extension" }

    $folder      = if ($Row.SuggestedFolder) { $Row.SuggestedFolder } else { $Row.FolderPath }
    $projected   = Join-Path (Join-Path $LibraryRoot ($folder -replace '/', '\')) $newName

    if (-not (Test-GovPathLength -Path $projected)) {
        $Row.Notes = (@($Row.Notes, "Rename refused: projected path length $($projected.Length) exceeds the limit.") |
                      Where-Object { $_ }) -join ' '
        return $Row
    }

    if ($TagPosition -eq 'Prefix') { $Row.Prefix = $tag } else { $Row.Suffix = $tag }
    $Row.NewName = $newName
    return $Row
}
