#Requires -Version 7.0
Set-StrictMode -Version Latest

function New-TestRoot {
    $path = Join-Path ([System.IO.Path]::GetTempPath()) ("govlib-" + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $path -Force | Out-Null
    return $path
}

function New-TestBaseline {
    param([string] $OverrideJson)
    $root = New-TestRoot
    $path = Join-Path $root 'library-baseline.json'
    Set-Content -LiteralPath $path -Value $OverrideJson -Encoding utf8NoBOM
    return $path
}

function New-TestLibrary {
    <#
      Builds a synthetic library. Files is a hashtable of relative path to content.
      Returns the library root path.
    #>
    param([hashtable] $Files = @{})
    $root = New-TestRoot
    foreach ($rel in $Files.Keys) {
        $full = Join-Path $root $rel
        $dir = Split-Path -Parent $full
        if (-not (Test-Path -LiteralPath $dir)) {
            New-Item -ItemType Directory -Path $dir -Force | Out-Null
        }
        Set-Content -LiteralPath $full -Value $Files[$rel] -Encoding utf8NoBOM
    }
    return $root
}

function Get-TreeSnapshot {
    <# Relative path plus length for every file, sorted. Used to prove read-only behavior. #>
    param([string] $Root)
    Get-ChildItem -LiteralPath $Root -Recurse -File |
        ForEach-Object { '{0}|{1}' -f $_.FullName.Substring($Root.Length).TrimStart('\'), $_.Length } |
        Sort-Object
}

Export-ModuleMember -Function New-TestRoot, New-TestBaseline, New-TestLibrary, Get-TreeSnapshot
