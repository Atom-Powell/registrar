#Requires -Version 7.0

function New-GovReport {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $LibraryRoot,
        [Parameter(Mandatory)] [object] $Baseline,
        [hashtable] $Registry,
        [hashtable] $ArtifactTypes,
        [string]    $RepositoryTargetsPath
    )

    $inventory = @(Get-GovInventory -LibraryRoot $LibraryRoot)

    $actualFolders = @(Get-ChildItem -LiteralPath $LibraryRoot -Recurse -Directory -Force |
        ForEach-Object {
            $_.FullName.Substring(([System.IO.Path]::GetFullPath($LibraryRoot).TrimEnd('\','/')).Length).TrimStart('\','/') -replace '\\', '/'
        })

    $baselinePaths = @($Baseline.folderTree | Where-Object { $_.createIfEmpty } | ForEach-Object { $_.path })
    $missingFolders = @($baselinePaths | Where-Object { $_ -notin $actualFolders })

    $allBaselinePaths = @($Baseline.folderTree | ForEach-Object { $_.path })
    $unknownFolders = @($actualFolders | Where-Object {
        $folder = $_
        -not ($allBaselinePaths | Where-Object { $folder -eq $_ -or $folder.StartsWith("$_/", [System.StringComparison]::OrdinalIgnoreCase) })
    })

    $archiveName = $null
    foreach ($candidate in @('Archive', '_Archive')) {
        if ($actualFolders -contains $candidate) { $archiveName = $candidate; break }
    }

    $misplaced   = @()
    $unregistered = @()
    $seenIds     = @{}

    if ($Registry -and $ArtifactTypes) {
        foreach ($item in $inventory) {
            $identity = Resolve-GovIdentity -Name $item.Name -Registry $Registry -ArtifactTypes $ArtifactTypes
            if (-not $identity.DocumentId) { continue }
            if (-not $identity.Resolved) {
                if ($identity.Reason -like '*not in the ID Registry*') { $unregistered += $identity.DocumentId }
                continue
            }
            $seenIds[$identity.DocumentId] = $true

            $placement = Get-GovPlacementTarget -Baseline $Baseline `
                             -ApprovalStatus $identity.ApprovalStatus -Category $identity.Category
            if (-not $placement -or -not $placement.TargetPath) { continue }
            if (Get-GovProtectionReason -RelativePath $item.RelativePath -Baseline $Baseline) { continue }

            if ($item.FolderPath -ne $placement.TargetPath) {
                $misplaced += [pscustomobject]@{
                    DocumentId = $identity.DocumentId
                    Actual     = $item.FolderPath
                    Expected   = $placement.TargetPath
                    RuleSource = $placement.RuleSource
                }
            }
        }
    }

    $registryRowsWithNoFile = @()
    if ($Registry -and $ArtifactTypes) {
        $registryRowsWithNoFile = @($Registry.Keys | Where-Object { -not $seenIds.ContainsKey($_) } | Sort-Object)
    }

    $divergence = @()
    if ($RepositoryTargetsPath -and (Test-Path -LiteralPath $RepositoryTargetsPath -PathType Leaf)) {
        $targets = (Get-Content -LiteralPath $RepositoryTargetsPath -Raw | ConvertFrom-Json).targetsByCategory
        foreach ($property in $targets.PSObject.Properties) {
            $category = $property.Name
            $mapping = $Baseline.placementRules.mappings | Where-Object {
                $_.match.PSObject.Properties.Name -contains 'idCategory' -and
                $_.match.idCategory -eq $category -and $_.match.approvalStatus -eq 'Approved'
            } | Select-Object -First 1
            if ($mapping -and $mapping.targetPath -ne $property.Value) {
                $divergence += [pscustomobject]@{
                    Category         = $category
                    EngineTarget     = $property.Value
                    BaselineTarget   = $mapping.targetPath
                }
            }
        }
    }

    $markdown = New-GovReportMarkdown -LibraryRoot $LibraryRoot -MissingFolders $missingFolders `
                    -UnknownFolders $unknownFolders -Misplaced $misplaced `
                    -UnregisteredIds $unregistered -RegistryRowsWithNoFile $registryRowsWithNoFile `
                    -TargetDivergence $divergence -ArchiveFolderName $archiveName

    return [pscustomobject]@{
        MissingFolders         = $missingFolders
        UnknownFolders         = $unknownFolders
        Misplaced              = $misplaced
        UnregisteredIds        = @($unregistered | Sort-Object -Unique)
        RegistryRowsWithNoFile = $registryRowsWithNoFile
        TargetDivergence       = $divergence
        ArchiveFolderName      = $archiveName
        Markdown               = $markdown
    }
}

function New-GovReportMarkdown {
    param(
        [string] $LibraryRoot, [string[]] $MissingFolders, [string[]] $UnknownFolders,
        [object[]] $Misplaced, [string[]] $UnregisteredIds, [string[]] $RegistryRowsWithNoFile,
        [object[]] $TargetDivergence, [string] $ArchiveFolderName
    )

    $lines = @()
    $lines += '# Governance Library Drift Report'
    $lines += ''
    $lines += "Library root: $LibraryRoot"
    $lines += ''
    $lines += 'This report is read-only. It proposes no moves.'
    $lines += ''

    $lines += '## Missing baseline folders'
    $lines += ''
    if ($MissingFolders) { $lines += ($MissingFolders | ForEach-Object { "- $_" }) }
    else { $lines += '- None' }
    $lines += ''

    $lines += '## Folders not in the baseline'
    $lines += ''
    if ($UnknownFolders) { $lines += ($UnknownFolders | ForEach-Object { "- $_" }) }
    else { $lines += '- None' }
    $lines += ''

    $lines += '## Files whose folder disagrees with their registry status'
    $lines += ''
    if ($Misplaced) {
        $lines += '| Document ID | Actual | Expected | Rule source |'
        $lines += '|---|---|---|---|'
        $lines += ($Misplaced | ForEach-Object { "| $($_.DocumentId) | $($_.Actual) | $($_.Expected) | $($_.RuleSource) |" })
    }
    else { $lines += '- None' }
    $lines += ''

    $lines += '## Document IDs in filenames that are not in the registry'
    $lines += ''
    if ($UnregisteredIds) { $lines += ($UnregisteredIds | ForEach-Object { "- $_" }) }
    else { $lines += '- None' }
    $lines += ''

    $lines += '## Registry rows with no corresponding file'
    $lines += ''
    if ($RegistryRowsWithNoFile) { $lines += ($RegistryRowsWithNoFile | ForEach-Object { "- $_" }) }
    else { $lines += '- None' }
    $lines += ''

    $lines += '## Divergence against repository-targets.json'
    $lines += ''
    if ($TargetDivergence) {
        $lines += '| Category | Engine target | Baseline target |'
        $lines += '|---|---|---|'
        $lines += ($TargetDivergence | ForEach-Object { "| $($_.Category) | $($_.EngineTarget) | $($_.BaselineTarget) |" })
    }
    else { $lines += '- None' }
    $lines += ''

    $lines += '## Archive folder name'
    $lines += ''
    $lines += if ($ArchiveFolderName) { "Resolved empirically as '$ArchiveFolderName'." }
              else { 'Neither Archive nor _Archive is present.' }
    $lines += ''

    return ($lines -join "`n")
}
