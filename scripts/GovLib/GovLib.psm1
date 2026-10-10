#Requires -Version 7.0
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

foreach ($file in Get-ChildItem -Path $PSScriptRoot -Filter '*.ps1' -File | Sort-Object Name) {
    . $file.FullName
}
