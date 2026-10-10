#Requires -Version 7.0

function ConvertTo-GovNormalPath {
    param([string] $Path)
    return ($Path -replace '\\', '/').TrimStart('/')
}

function Get-GovProtectionReason {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $RelativePath,
        [Parameter(Mandatory)] [object] $Baseline
    )
    $normal = ConvertTo-GovNormalPath $RelativePath
    foreach ($rule in $Baseline.doNotTouch) {
        $pattern = ConvertTo-GovNormalPath $rule.pattern
        if ($normal -like $pattern) { return $rule.reason }
        # A trailing /* must also protect the folder itself.
        if ($pattern.EndsWith('/*') -and $normal -eq $pattern.Substring(0, $pattern.Length - 2)) {
            return $rule.reason
        }
    }
    return $null
}

function Test-GovProtectedPath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $RelativePath,
        [Parameter(Mandatory)] [object] $Baseline
    )
    return $null -ne (Get-GovProtectionReason -RelativePath $RelativePath -Baseline $Baseline)
}

function Test-GovPathUnderRoot {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $Root
    )
    $p = [System.IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
    $r = [System.IO.Path]::GetFullPath($Root).TrimEnd('\', '/')
    if ($p -eq $r) { return $true }
    return $p.StartsWith($r + [System.IO.Path]::DirectorySeparatorChar,
                         [System.StringComparison]::OrdinalIgnoreCase)
}

function Test-GovPathLength {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $Path,
        [int] $MaxLength = 255
    )
    return $Path.Length -le $MaxLength
}
