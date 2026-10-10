#Requires -Version 7.0

$script:GovPlanRequiredColumns = @(
    'Name','RelativePath','SourceStamp','SuggestedFolder',
    'ActionRequired','Approved','NewName'
)

function Get-GovSourceStamp {
    <#
      A staleness token that survives Excel. The plan is opened, edited and
      saved in Excel by design, and Excel carries only 15 significant digits:
      an 18-digit tick count comes back rounded, which made every approved row
      look changed. A short hash is text to Excel, so it round-trips intact.

      The leading 's' is deliberate. 16 hex characters are all decimal digits
      often enough to matter across a large library, and Excel would coerce
      that to a number. The prefix makes coercion impossible.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [long] $Ticks,
        [Parameter(Mandatory)] [long] $SizeBytes
    )

    $bytes = [System.Text.Encoding]::UTF8.GetBytes("$Ticks|$SizeBytes")
    $sha   = [System.Security.Cryptography.SHA256]::Create()
    try { $hash = $sha.ComputeHash($bytes) }
    finally { $sha.Dispose() }

    return 's' + (-join ($hash[0..7] | ForEach-Object { $_.ToString('x2') }))
}

$script:GovMoveActions = @('Move to Existing Folder', 'Create Folder and Move')

# The approval workbook is the whole safety architecture of this tool, so the
# accepted set is narrow and explicit. 'N', 'No', 'FALSE', '0' and blank are
# all NOT approved. Apply and Verify both route through Test-GovApproved so
# the two can never drift apart.
$script:GovApprovalTokens = @('Y', 'YES', 'TRUE', 'X', '1')

function Get-GovApprovalTokens {
    <# The literal tokens an operator may type in the Approved column. #>
    [CmdletBinding()]
    param()
    return $script:GovApprovalTokens
}

function Test-GovApproved {
    <# The single affirmative test. Case-insensitive, trimmed, allowlist only. #>
    [CmdletBinding()]
    param([string] $Value)

    if ([string]::IsNullOrWhiteSpace($Value)) { return $false }
    return ($Value.Trim().ToUpperInvariant() -in $script:GovApprovalTokens)
}

function Invoke-GovApply {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $PlanPath,
        [Parameter(Mandatory)] [string] $LibraryRoot,
        [Parameter(Mandatory)] [object] $Baseline,
        [Parameter(Mandatory)] [string] $JournalPath,
        [int] $BatchSize = 25,
        [int] $SettleSeconds = 5,
        [int] $RetryCount = 2,
        [int] $RetryDelayMs = 500,
        [int] $SyncCheckWithinSeconds = 60
    )

    if (-not (Test-Path -LiteralPath $PlanPath -PathType Leaf)) {
        throw "Plan not found: $PlanPath"
    }
    $rows = @(Import-Csv -LiteralPath $PlanPath)
    if ($rows.Count -eq 0) { throw "Plan is empty: $PlanPath" }

    $columns = $rows[0].PSObject.Properties.Name
    foreach ($required in $script:GovPlanRequiredColumns) {
        if ($columns -notcontains $required) {
            throw "Plan is missing required column '$required': $PlanPath"
        }
    }

    $approved = @($rows | Where-Object {
        (Test-GovApproved -Value $_.Approved) -and
        $_.ActionRequired -in $script:GovMoveActions
    })

    foreach ($row in $approved) {
        if ([string]::IsNullOrWhiteSpace($row.SuggestedFolder)) {
            throw "Approved row '$($row.Name)' has a blank Suggested Folder. Resolve it before applying."
        }
    }

    New-GovJournal -Path $JournalPath | Out-Null

    $sync = Test-GovSyncQuiet -LibraryRoot $LibraryRoot -WithinSeconds $SyncCheckWithinSeconds
    if (-not $sync.Quiet) {
        Write-Warning (("Library may be mid-sync. Temp files: {0}. Recently modified: {1}. " +
                       "This check is best-effort and cannot read the sync client's state.") -f
                       $sync.TempFiles.Count, $sync.RecentlyModified.Count)
    }

    $summary = [pscustomobject]@{
        Moved = 0; Skipped = 0; Refused = 0; Messages = @(); JournalPath = $JournalPath
    }

    # SOP order: existing-folder moves first, then create-folder-and-move.
    $ordered = @($approved | Where-Object ActionRequired -eq 'Move to Existing Folder') +
               @($approved | Where-Object ActionRequired -eq 'Create Folder and Move')

    $index = 0
    foreach ($row in $ordered) {
        $index++
        Invoke-GovApplyRow -Row $row -LibraryRoot $LibraryRoot -Baseline $Baseline `
                           -JournalPath $JournalPath -Summary $summary `
                           -RetryCount $RetryCount -RetryDelayMs $RetryDelayMs

        if ($SettleSeconds -gt 0 -and $index % $BatchSize -eq 0) {
            Start-Sleep -Seconds $SettleSeconds
        }
    }

    return $summary
}

function Test-GovPathContained {
    <#
      Test-GovPathUnderRoot, made total. GetFullPath throws on a path holding
      characters the filesystem rejects, and a path we cannot resolve is a path
      we cannot prove is inside the root, so it fails closed.
    #>
    param([string] $Path, [string] $Root)
    try { return (Test-GovPathUnderRoot -Path $Path -Root $Root) }
    catch { return $false }
}

function Invoke-GovApplyRow {
    param(
        [object] $Row, [string] $LibraryRoot, [object] $Baseline,
        [string] $JournalPath, [object] $Summary,
        [int] $RetryCount = 2,
        [int] $RetryDelayMs = 500
    )

    $reason = Get-GovProtectionReason -RelativePath $Row.RelativePath -Baseline $Baseline
    if ($reason) {
        $Summary.Refused++
        $Summary.Messages += "REFUSED $($Row.Name): protected path. $reason"
        return
    }

    if (Test-GovConflictCopy -Name $Row.Name) {
        $Summary.Skipped++
        $Summary.Messages += "SKIPPED $($Row.Name): looks like a sync conflict copy. Resolve it by hand."
        return
    }

    $source = Join-Path $LibraryRoot ($Row.RelativePath -replace '/', '\')

    # Containment, checked before anything else touches the filesystem. A
    # human-edited CSV cell holding '../Outside' resolves to a path outside the
    # synced root, and moving a file out of a synced OneDrive root is, to
    # SharePoint, a deletion. This tool has no delete path and must not acquire
    # one by way of a typo.
    if (-not (Test-GovPathContained -Path $source -Root $LibraryRoot)) {
        $Summary.Refused++
        $Summary.Messages += "REFUSED $($Row.Name): source path '$source' resolves outside the library root."
        return
    }

    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        $Summary.Skipped++
        $Summary.Messages += "SKIPPED $($Row.Name): source no longer exists."
        return
    }

    $file         = Get-Item -LiteralPath $source
    $currentStamp = Get-GovSourceStamp -Ticks $file.LastWriteTimeUtc.Ticks -SizeBytes $file.Length
    if (([string]$Row.SourceStamp).Trim() -ne $currentStamp) {
        $Summary.Skipped++
        $Summary.Messages += "SKIPPED $($Row.Name): source changed after planning."
        return
    }

    $targetName = if ([string]::IsNullOrWhiteSpace($Row.NewName)) { $Row.Name } else { $Row.NewName }
    $targetDir  = Join-Path $LibraryRoot ($Row.SuggestedFolder -replace '/', '\')
    $target     = Join-Path $targetDir $targetName

    if (-not (Test-GovPathContained -Path $target -Root $LibraryRoot)) {
        $Summary.Refused++
        $Summary.Messages += "REFUSED $($Row.Name): destination path '$target' resolves outside the library root."
        return
    }

    $targetRelative = ($Row.SuggestedFolder.TrimEnd('/') + '/' + $targetName)
    $targetReason = Get-GovProtectionReason -RelativePath $targetRelative -Baseline $Baseline
    if ($targetReason) {
        $Summary.Refused++
        $Summary.Messages += "REFUSED $($Row.Name): destination is protected. $targetReason"
        return
    }

    if (-not (Test-GovPathLength -Path $target)) {
        $Summary.Skipped++
        $Summary.Messages += "SKIPPED $($Row.Name): destination path length $($target.Length) exceeds the limit."
        return
    }

    if (Test-Path -LiteralPath $target) {
        $Summary.Skipped++
        $Summary.Messages += "SKIPPED $($Row.Name): destination already exists. Never overwriting."
        return
    }

    if (-not (Test-Path -LiteralPath $targetDir -PathType Container)) {
        New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
    }

    $action = if ($targetName -ne $Row.Name) { 'move+rename' } else { 'move' }

    # Journal first, then act. A kill between these leaves an entry with no move,
    # which undo tolerates (it flags a missing source) and which never loses a file.
    Add-GovJournalEntry -JournalPath $JournalPath -Action $action -Source $source -Destination $target

    $attempt = 0
    while ($true) {
        try {
            Move-Item -LiteralPath $source -Destination $target -ErrorAction Stop
            $Summary.Moved++
            break
        }
        catch {
            $attempt++
            if ($attempt -gt $RetryCount) {
                $Summary.Skipped++
                $Summary.Messages += "SKIPPED $($Row.Name) after $attempt attempts: $($_.Exception.Message)"
                break
            }
            if ($RetryDelayMs -gt 0) { Start-Sleep -Milliseconds $RetryDelayMs }
        }
    }
}
