#Requires -Version 7.0

function Invoke-GovUndo {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $JournalPath,

        # Optional by design. Undo must stay able to run from a journal alone,
        # for instance when the library has been unsynced or relocated. When a
        # root is supplied, every entry is containment-checked against it.
        [string] $LibraryRoot
    )

    $entries = @(Read-GovJournal -Path $JournalPath)
    [array]::Reverse($entries)

    $summary = [pscustomobject]@{ Restored = 0; Flagged = 0; Messages = @() }

    if (-not $LibraryRoot) {
        $summary.Messages += ('NOTE: no LibraryRoot supplied. Containment was not enforced: ' +
                              'entries were reversed exactly as the journal records them.')
    }

    foreach ($entry in $entries) {
        if ($LibraryRoot) {
            $offender = $null
            if (-not (Test-GovPathContained -Path $entry.Source -Root $LibraryRoot)) {
                $offender = "source '$($entry.Source)'"
            }
            elseif (-not (Test-GovPathContained -Path $entry.Destination -Root $LibraryRoot)) {
                $offender = "destination '$($entry.Destination)'"
            }
            if ($offender) {
                $summary.Flagged++
                $summary.Messages += "FLAGGED: journal $offender resolves outside the library root. Not reversing."
                continue
            }
        }

        if (-not (Test-Path -LiteralPath $entry.Destination -PathType Leaf)) {
            $summary.Flagged++
            $summary.Messages += "FLAGGED: '$($entry.Destination)' is not present. Nothing to reverse."
            continue
        }
        if (Test-Path -LiteralPath $entry.Source) {
            $summary.Flagged++
            $summary.Messages += "FLAGGED: original location '$($entry.Source)' is occupied. Never overwriting."
            continue
        }

        $dir = Split-Path -Parent $entry.Source
        if ($dir -and -not (Test-Path -LiteralPath $dir -PathType Container)) {
            New-Item -ItemType Directory -Path $dir -Force | Out-Null
        }

        try {
            Move-Item -LiteralPath $entry.Destination -Destination $entry.Source -ErrorAction Stop
            $summary.Restored++
        }
        catch {
            $summary.Flagged++
            $summary.Messages += "FLAGGED: $($entry.Destination). $($_.Exception.Message)"
        }
    }

    return $summary
}
