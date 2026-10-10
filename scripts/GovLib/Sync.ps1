#Requires -Version 7.0

# Conservative patterns. A false positive only causes a row to be skipped and
# flagged for a human, which is the safe direction.
$script:GovConflictPatterns = @(
    '\(\d+\)\.[^.]+$',                       # Report (1).docx
    "conflicted copy",                       # Jane's conflicted copy 2026-07-14
    '-(DESKTOP|LAPTOP)-[A-Z0-9]{5,}\.[^.]+$',# Report-DESKTOP-A1B2C3D.docx
    ' - Copy\.[^.]+$'                        # Report - Copy.docx
)

$script:GovTempPatterns = @('~$*', '*.tmp', '*.partial', '.~lock*')

function Test-GovConflictCopy {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [string] $Name)

    foreach ($pattern in $script:GovConflictPatterns) {
        if ([regex]::IsMatch($Name, $pattern, [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)) {
            return $true
        }
    }
    return $false
}

function Test-GovSyncQuiet {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $LibraryRoot,
        [int] $WithinSeconds = 60
    )

    $temp = @()
    foreach ($pattern in $script:GovTempPatterns) {
        $temp += @(Get-ChildItem -LiteralPath $LibraryRoot -Recurse -File -Force -Filter $pattern -ErrorAction SilentlyContinue |
                   ForEach-Object { $_.Name })
    }

    $recent = @()
    if ($WithinSeconds -gt 0) {
        $cutoff = (Get-Date).ToUniversalTime().AddSeconds(-$WithinSeconds)
        $recent = @(Get-ChildItem -LiteralPath $LibraryRoot -Recurse -File -Force |
                    Where-Object { $_.LastWriteTimeUtc -gt $cutoff } |
                    ForEach-Object { $_.Name })
    }

    return [pscustomobject]@{
        Quiet            = (($temp.Count -eq 0) -and ($recent.Count -eq 0))
        TempFiles        = $temp
        RecentlyModified = $recent
    }
}
