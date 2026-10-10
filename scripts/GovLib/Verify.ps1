#Requires -Version 7.0

function Invoke-GovVerify {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $PlanPath,
        [Parameter(Mandatory)] [string] $LibraryRoot
    )

    if (-not (Test-Path -LiteralPath $PlanPath -PathType Leaf)) {
        throw "Plan not found: $PlanPath"
    }

    $rows = @(Import-Csv -LiteralPath $PlanPath)
    if ($rows.Count -eq 0) { throw "Plan is empty: $PlanPath" }

    # Same required-column gate as Apply, so a malformed plan produces an
    # actionable message rather than a raw StrictMode property error.
    $columns = $rows[0].PSObject.Properties.Name
    foreach ($required in $script:GovPlanRequiredColumns) {
        if ($columns -notcontains $required) {
            throw "Plan is missing required column '$required': $PlanPath"
        }
    }

    $inventory = @(Get-GovInventory -LibraryRoot $LibraryRoot)
    $present   = @{}
    foreach ($item in $inventory) { $present[$item.RelativePath.ToLowerInvariant()] = $true }

    $landed  = 0
    $missing = @()

    foreach ($row in $rows) {
        if (-not (Test-GovApproved -Value $row.Approved)) { continue }
        if ([string]::IsNullOrWhiteSpace($row.SuggestedFolder)) {
            $missing += "$($row.Name) (approved row has a blank Suggested Folder)"
            continue
        }

        $name     = if ([string]::IsNullOrWhiteSpace($row.NewName)) { $row.Name } else { $row.NewName }
        $expected = ($row.SuggestedFolder.TrimEnd('/') + '/' + $name)

        if ($present.ContainsKey($expected.ToLowerInvariant())) { $landed++ }
        else { $missing += $expected }

        # Arrival at the destination is not proof the move happened: Apply
        # correctly skips an overwrite, which leaves a file of that name at the
        # destination while the source still sits at the root. A move is only
        # verified when the source is also gone.
        if ($present.ContainsKey($row.RelativePath.ToLowerInvariant())) {
            $missing += "$($row.Name) (still present at source)"
        }
    }

    return [pscustomobject]@{
        Landed  = $landed
        Missing = $missing
        Ok      = ($missing.Count -eq 0)
    }
}
