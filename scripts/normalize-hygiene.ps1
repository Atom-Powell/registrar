<#
.SYNOPSIS
    Replaces every literal non-ASCII character in the given file(s) with its
    unicode escape, so no literal glyph is ever committed.

.DESCRIPTION
    The engine forbids decorative unicode in generated documents (checks
    QA-SF-08 and QA-ENG-05). This repository holds its own source and governed
    config to the same rule: prohibited characters (em/en dashes, smart quotes,
    ellipsis, NBSP, soft hyphen, emoji, and the like) must be written as
    \uXXXX escapes, never pasted as literal glyphs.

    Authoring tools sometimes decode a typed \uXXXX escape back into the literal
    glyph before writing the file, which silently commits the forbidden
    character. SourceHygieneTests fails the build when that happens. This script
    is the one-command fix: it rewrites each supplied file, converting every
    character above U+007F into its \uXXXX escape (or \UXXXXXXXX for
    supplementary-plane code points). Characters that are already plain ASCII,
    including existing \uXXXX escape text, are left untouched, so the script is
    idempotent.

    Files are written back as UTF-8 without a byte order mark.

.PARAMETER Paths
    One or more file paths to normalize.

.EXAMPLE
    ./scripts/normalize-hygiene.ps1 tests/Registrar.Tests/CharacterLintTests.cs

.EXAMPLE
    ./scripts/normalize-hygiene.ps1 fixtures/agr-sample-ds-prohibited-char.json config/style-rules.json
#>
param(
    [Parameter(Mandatory, ValueFromRemainingArguments)]
    [string[]] $Paths
)

$totalEscaped = 0

foreach ($path in $Paths) {
    if (-not (Test-Path -LiteralPath $path)) {
        Write-Warning "not found: $path"
        continue
    }

    $text = [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)
    $builder = [System.Text.StringBuilder]::new()
    $escaped = 0

    foreach ($rune in $text.EnumerateRunes()) {
        $value = $rune.Value
        if ($value -le 0x7F) {
            [void]$builder.Append([char]$value)
        }
        elseif ($value -le 0xFFFF) {
            [void]$builder.AppendFormat('\u{0:x4}', $value)
            $escaped++
        }
        else {
            [void]$builder.AppendFormat('\U{0:X8}', $value)
            $escaped++
        }
    }

    if ($escaped -gt 0) {
        $noBom = [System.Text.UTF8Encoding]::new($false)
        [System.IO.File]::WriteAllText($path, $builder.ToString(), $noBom)
        Write-Host "$path : escaped $escaped non-ASCII character(s)"
        $totalEscaped += $escaped
    }
    else {
        Write-Host "$path : clean"
    }
}

if ($totalEscaped -eq 0) {
    Write-Host "All files already clean; nothing to normalize."
}
