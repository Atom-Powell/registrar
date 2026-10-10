#Requires -Version 7.0

function New-GovRunContext {
    <#
      Operator-workflow logic shared by every caller (CLI today, a GUI later):
      resolving defaults, validating the library root, and refusing an
      OutputRoot that would sync run artifacts into the library itself.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [ValidateSet('Inventory','Plan','Apply','Verify','Report','Undo')]
        [string] $Mode,

        [string] $LibraryRoot,

        [Parameter(Mandatory)] [string] $RepoRoot,

        [string] $OutputRoot,
        [string] $BaselinePath,
        [string] $ArtifactTypesPath,
        [string] $RepositoryTargetsPath
    )

    if ($Mode -ne 'Undo') {
        if (-not $LibraryRoot) {
            throw "LibraryRoot is required for mode '$Mode'."
        }
        if (-not (Test-Path -LiteralPath $LibraryRoot -PathType Container)) {
            throw "Library root not found: $LibraryRoot"
        }
    }

    if (-not $OutputRoot) { $OutputRoot = Join-Path $RepoRoot 'gov-runs' }

    if ($LibraryRoot -and (Test-GovPathUnderRoot -Path $OutputRoot -Root $LibraryRoot)) {
        throw "OutputRoot '$OutputRoot' is inside the library root. Run artifacts must not sync into SharePoint. Choose a location outside the library."
    }

    if (-not $BaselinePath) { $BaselinePath = Join-Path $RepoRoot 'config/library-baseline.json' }
    if (-not $ArtifactTypesPath) { $ArtifactTypesPath = Join-Path $RepoRoot 'config/artifact-types.json' }
    if (-not $RepositoryTargetsPath) { $RepositoryTargetsPath = Join-Path $RepoRoot 'config/repository-targets.json' }

    $stamp  = (Get-Date).ToUniversalTime().ToString('yyyyMMdd-HHmmss')
    $runDir = Join-Path (Join-Path $OutputRoot 'runs') $stamp
    New-Item -ItemType Directory -Path $runDir -Force | Out-Null

    $defaultJournalPath = Join-Path $runDir "undo-manifest-$stamp.jsonl"

    return [pscustomobject]@{
        LibraryRoot           = $LibraryRoot
        OutputRoot            = $OutputRoot
        RunDir                = $runDir
        BaselinePath          = $BaselinePath
        ArtifactTypesPath     = $ArtifactTypesPath
        RepositoryTargetsPath = $RepositoryTargetsPath
        DefaultJournalPath    = $defaultJournalPath
        Stamp                 = $stamp
    }
}

function Import-GovRegistryContext {
    <#
      Both-or-neither config load: the ID Registry and the artifact type
      map are only meaningful together (New-GovReport and New-GovPlan both
      require both or neither). Centralizing the gate here means a caller
      (CLI today, a GUI later) cannot supply one without the other by
      omission.
    #>
    [CmdletBinding()]
    param(
        [string] $RegistryPath,
        [Parameter(Mandatory)] [string] $ArtifactTypesPath
    )

    if (-not $RegistryPath) {
        return [pscustomobject]@{ Registry = $null; ArtifactTypes = $null }
    }

    $registry      = Import-GovRegistry -Path $RegistryPath
    $artifactTypes = Import-GovArtifactTypes -Path $ArtifactTypesPath

    return [pscustomobject]@{ Registry = $registry; ArtifactTypes = $artifactTypes }
}
