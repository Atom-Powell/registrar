#Requires -Version 7.0

function New-GovJournal {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [string] $Path)

    $dir = Split-Path -Parent $Path
    if ($dir -and -not (Test-Path -LiteralPath $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }
    if (-not (Test-Path -LiteralPath $Path)) {
        New-Item -ItemType File -Path $Path | Out-Null
    }
    return $Path
}

function Add-GovJournalEntry {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $JournalPath,
        [Parameter(Mandatory)] [string] $Action,
        [Parameter(Mandatory)] [string] $Source,
        [Parameter(Mandatory)] [string] $Destination
    )

    $entry = [pscustomobject]@{
        TimestampUtc = (Get-Date).ToUniversalTime().ToString('o')
        Action       = $Action
        Source       = $Source
        Destination  = $Destination
    }
    $line = $entry | ConvertTo-Json -Compress

    # StreamWriter with AutoFlush guarantees the line reaches disk before the
    # caller performs the filesystem change this entry describes.
    $writer = [System.IO.StreamWriter]::new($JournalPath, $true, [System.Text.UTF8Encoding]::new($false))
    try {
        $writer.AutoFlush = $true
        $writer.WriteLine($line)
    }
    finally { $writer.Dispose() }
}

function Read-GovJournal {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [string] $Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Undo journal not found: $Path"
    }
    return @(Get-Content -LiteralPath $Path |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        ForEach-Object { $_ | ConvertFrom-Json })
}
