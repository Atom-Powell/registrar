#Requires -Version 7.0
BeforeAll {
    $script:RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
    Import-Module (Join-Path $script:RepoRoot 'scripts/GovLib/GovLib.psd1') -Force
    Import-Module (Join-Path $PSScriptRoot 'TestHelpers.psm1') -Force

    $script:Baseline = Import-GovBaseline     -Path (Join-Path $script:RepoRoot 'config/library-baseline.json')
    $script:Types    = Import-GovArtifactTypes -Path (Join-Path $script:RepoRoot 'config/artifact-types.json')
    $script:Registry = Import-GovRegistry      -Path (Join-Path $script:RepoRoot 'fixtures/registry-sample.csv')
}

Describe 'New-GovPlan' {
    It 'proposes a target for a root file whose filename carries a registered Draft ID' {
        $root = New-TestLibrary -Files @{ 'ORG-GOV-DS-016 - Thing.docx' = 'x' }
        $plan = New-GovPlan -Inventory (Get-GovInventory -LibraryRoot $root) -Baseline $script:Baseline `
                            -LibraryRoot $root -Registry $script:Registry -ArtifactTypes $script:Types
        $plan[0].SuggestedFolder | Should -Be 'Master Development Workspace'
        $plan[0].ActionRequired  | Should -BeIn @('Move to Existing Folder', 'Create Folder and Move')
        $plan[0].Approved        | Should -BeNullOrEmpty
    }

    It 'applies dispositions.unrecognized to an unidentifiable root file and proposes nothing' {
        # The shipped config sets unrecognized.action to reviewRequired, which
        # is SOP Step 3's human classification. The value is read from config,
        # not hard-coded; the test below flips it to prove that.
        $root = New-TestLibrary -Files @{ 'random notes.docx' = 'x' }
        $plan = New-GovPlan -Inventory (Get-GovInventory -LibraryRoot $root) -Baseline $script:Baseline `
                            -LibraryRoot $root -Registry $script:Registry -ArtifactTypes $script:Types
        $plan[0].ActionRequired  | Should -Be 'Review Required'
        $plan[0].RuleSource      | Should -BeLike 'dispositions.unrecognized*'
        $plan[0].SuggestedFolder | Should -BeNullOrEmpty
    }

    It 'applies dispositions.unrecognized to an ID that is absent from the registry' {
        $root = New-TestLibrary -Files @{ 'ORG-GOV-DS-999 - Ghost.docx' = 'x' }
        $plan = New-GovPlan -Inventory (Get-GovInventory -LibraryRoot $root) -Baseline $script:Baseline `
                            -LibraryRoot $root -Registry $script:Registry -ArtifactTypes $script:Types
        $plan[0].ActionRequired | Should -Be 'Review Required'
        $plan[0].Notes          | Should -BeLike '*not in the ID Registry*'
    }

    It 'follows the configured unrecognized action rather than hard-coding one' {
        $config = $script:Baseline | ConvertTo-Json -Depth 20 | ConvertFrom-Json
        $config.dispositions.unrecognized.action = 'leaveInPlace'
        $root = New-TestLibrary -Files @{ 'random notes.docx' = 'x' }
        $plan = New-GovPlan -Inventory (Get-GovInventory -LibraryRoot $root) -Baseline $config `
                            -LibraryRoot $root -Registry $script:Registry -ArtifactTypes $script:Types
        $plan[0].ActionRequired | Should -Be 'Leave in Place'
    }

    It 'marks a Retired file Leave in Place even though it was identified' {
        $root = New-TestLibrary -Files @{ 'ORG-GOV-DS-017 - Old.docx' = 'x' }
        $plan = New-GovPlan -Inventory (Get-GovInventory -LibraryRoot $root) -Baseline $script:Baseline `
                            -LibraryRoot $root -Registry $script:Registry -ArtifactTypes $script:Types
        $plan[0].ActionRequired | Should -Be 'Leave in Place'
        $plan[0].RuleSource     | Should -BeLike '*notDefined*'
    }

    It 'takes the Retired action from dispositions.retired rather than hard-coding it' {
        $config = $script:Baseline | ConvertTo-Json -Depth 20 | ConvertFrom-Json
        $config.dispositions.retired.action = 'escalate'
        $root = New-TestLibrary -Files @{ 'ORG-GOV-DS-017 - Old.docx' = 'x' }
        $plan = New-GovPlan -Inventory (Get-GovInventory -LibraryRoot $root) -Baseline $config `
                            -LibraryRoot $root -Registry $script:Registry -ArtifactTypes $script:Types
        $plan[0].ActionRequired | Should -Be 'Escalate'
    }

    It 'escalates instead of proposing a destination Apply would refuse' {
        # Approved plus MST maps to Templates/Approved, which doNotTouch also
        # protects. Plan used to propose that move with a cited rule source and
        # Apply refused it every single time.
        $registry = @{
            'ORG-MST-SCL-001' = [pscustomobject]@{
                DocumentId = 'ORG-MST-SCL-001'; ArtifactType = 'SCL'
                Classification = 'Internal Working Document'; ApprovalStatus = 'Approved'; Version = 'v1.0'
            }
        }
        $root = New-TestLibrary -Files @{ 'ORG-MST-SCL-001 - Shared Clause Library.docx' = 'x' }
        $plan = New-GovPlan -Inventory (Get-GovInventory -LibraryRoot $root) -Baseline $script:Baseline `
                            -LibraryRoot $root -Registry $registry -ArtifactTypes $script:Types

        $plan[0].ActionRequired  | Should -Be 'Escalate'
        $plan[0].SuggestedFolder | Should -BeNullOrEmpty
        $plan[0].Notes           | Should -BeLike '*Templates/Approved*'
        $plan[0].Notes           | Should -BeLike '*Controlled artifacts*'
        $plan[0].RuleSource      | Should -BeLike 'doNotTouch on the destination*'
    }

    It 'marks every row Review Required when no registry is supplied' {
        $root = New-TestLibrary -Files @{ 'ORG-GOV-DS-016 - Thing.docx' = 'x' }
        $plan = New-GovPlan -Inventory (Get-GovInventory -LibraryRoot $root) -Baseline $script:Baseline `
                            -LibraryRoot $root
        $plan[0].ActionRequired  | Should -Be 'Review Required'
        $plan[0].SuggestedFolder | Should -BeNullOrEmpty
    }

    It 'excludes nested files from the plan entirely' {
        # Capture with @() at the call site. A bare assignment of no output
        # yields $null, and @($null).Count is 1, which would mask a real bug.
        $root = New-TestLibrary -Files @{ 'Controlled Documents/Governance/in.docx' = 'x' }
        $plan = @(New-GovPlan -Inventory (Get-GovInventory -LibraryRoot $root) -Baseline $script:Baseline `
                              -LibraryRoot $root -Registry $script:Registry -ArtifactTypes $script:Types)
        $plan.Count | Should -Be 0
    }

    It 'sets FolderExists false and Create Folder and Move when the target is absent' {
        $root = New-TestLibrary -Files @{ 'ORG-GOV-DS-016 - Thing.docx' = 'x' }
        $plan = New-GovPlan -Inventory (Get-GovInventory -LibraryRoot $root) -Baseline $script:Baseline `
                            -LibraryRoot $root -Registry $script:Registry -ArtifactTypes $script:Types
        $plan[0].FolderExists   | Should -BeFalse
        $plan[0].ActionRequired | Should -Be 'Create Folder and Move'
    }

    It 'records an Excel-safe source stamp for staleness detection' {
        $root = New-TestLibrary -Files @{ 'a.docx' = 'x' }
        $plan = New-GovPlan -Inventory (Get-GovInventory -LibraryRoot $root) -Baseline $script:Baseline -LibraryRoot $root
        $file = Get-Item -LiteralPath (Join-Path $root 'a.docx')
        $plan[0].SourceStamp | Should -Match '^s[0-9a-f]{16}$'
        $plan[0].SourceStamp | Should -Be (Get-GovSourceStamp -Ticks $file.LastWriteTimeUtc.Ticks -SizeBytes $file.Length)
    }

    It 'marks a root file matching a doNotTouch pattern Leave in Place with the configured reason' {
        $protected = $script:Baseline | ConvertTo-Json -Depth 20 | ConvertFrom-Json
        $protected.doNotTouch += [pscustomobject]@{
            pattern    = 'protected-at-root.docx'
            reason     = 'Test fixture: root-level protection.'
            ruleSource = 'Test fixture'
        }
        $root = New-TestLibrary -Files @{ 'protected-at-root.docx' = 'x' }
        $plan = New-GovPlan -Inventory (Get-GovInventory -LibraryRoot $root) -Baseline $protected `
                            -LibraryRoot $root -Registry $script:Registry -ArtifactTypes $script:Types
        $plan[0].ActionRequired  | Should -Be 'Leave in Place'
        $plan[0].Notes           | Should -Be 'Test fixture: root-level protection.'
        $plan[0].SuggestedFolder | Should -BeNullOrEmpty
    }
}

Describe 'Get-GovPlacementTarget' {
    BeforeAll {
        function New-MixedBaseline {
            <#
              A status-only mapping declared BEFORE a status-plus-category one
              for the same approvalStatus. Under a first-match rule the broad
              mapping wins and the specific one is dead config.
            #>
            param()
            $b = $script:Baseline | ConvertTo-Json -Depth 20 | ConvertFrom-Json
            $b.placementRules.mappings = @(
                [pscustomobject]@{
                    match = [pscustomobject]@{ approvalStatus = 'Approved' }
                    targetPath = 'Catch All'; status = 'governed'; ruleSource = 'broad'
                },
                [pscustomobject]@{
                    match = [pscustomobject]@{ approvalStatus = 'Approved'; idCategory = 'GOV' }
                    targetPath = 'Controlled Documents/Governance'; status = 'governed'; ruleSource = 'specific'
                }
            )
            return $b
        }
    }

    It 'picks the most specific mapping, not the first declared one' {
        $r = Get-GovPlacementTarget -Baseline (New-MixedBaseline) -ApprovalStatus 'Approved' -Category 'GOV'
        $r.TargetPath | Should -Be 'Controlled Documents/Governance'
        $r.RuleSource | Should -Be 'specific'
    }

    It 'falls back to the broader mapping when the specific one does not match' {
        $r = Get-GovPlacementTarget -Baseline (New-MixedBaseline) -ApprovalStatus 'Approved' -Category 'RSK'
        $r.TargetPath | Should -Be 'Catch All'
    }

    It 'returns nothing when no mapping matches' {
        Get-GovPlacementTarget -Baseline $script:Baseline -ApprovalStatus 'Nonsense' -Category 'GOV' |
            Should -BeNullOrEmpty
    }

    It 'ignores a mapping constrained by a field not in decidingFieldsInPriorityOrder' {
        # A mapping asking for a decision this code cannot evaluate must fail
        # closed rather than match on the fields it happens to understand.
        $b = $script:Baseline | ConvertTo-Json -Depth 20 | ConvertFrom-Json
        $b.placementRules.mappings = @(
            [pscustomobject]@{
                match = [pscustomobject]@{ approvalStatus = 'Approved'; clientTier = 'Gold' }
                targetPath = 'Somewhere'; status = 'governed'; ruleSource = 'unevaluable'
            }
        )
        Get-GovPlacementTarget -Baseline $b -ApprovalStatus 'Approved' -Category 'GOV' | Should -BeNullOrEmpty
    }

    It 'still resolves the shipped config the way it always did' {
        (Get-GovPlacementTarget -Baseline $script:Baseline -ApprovalStatus 'Draft' -Category 'GOV').TargetPath |
            Should -Be 'Master Development Workspace'
        (Get-GovPlacementTarget -Baseline $script:Baseline -ApprovalStatus 'Approved' -Category 'RSK').TargetPath |
            Should -Be 'Controlled Documents/Risk'
        (Get-GovPlacementTarget -Baseline $script:Baseline -ApprovalStatus 'Retired' -Category 'GOV').Status |
            Should -Be 'notDefined'
    }
}

Describe 'New-GovPlan tagging' {
    It 'leaves tag columns empty when fileNaming status is notDefined' {
        $root = New-TestLibrary -Files @{ 'ORG-GOV-DS-016 - Thing.docx' = 'x' }
        $plan = New-GovPlan -Inventory (Get-GovInventory -LibraryRoot $root) -Baseline $script:Baseline `
                            -LibraryRoot $root -Registry $script:Registry -ArtifactTypes $script:Types `
                            -TagMode DocumentId
        $plan[0].Prefix  | Should -BeNullOrEmpty
        $plan[0].NewName | Should -BeNullOrEmpty
        $plan[0].Notes   | Should -BeLike '*fileNaming*'
    }

    It 'fills the Document ID prefix when fileNaming is governed' {
        $governed = $script:Baseline | ConvertTo-Json -Depth 20 | ConvertFrom-Json
        $governed.fileNaming.status = 'governed'
        $root = New-TestLibrary -Files @{ 'ORG-GOV-DS-016 - Thing.docx' = 'x' }
        $plan = New-GovPlan -Inventory (Get-GovInventory -LibraryRoot $root) -Baseline $governed `
                            -LibraryRoot $root -Registry $script:Registry -ArtifactTypes $script:Types `
                            -TagMode DocumentId
        $plan[0].NewName | Should -BeLike 'ORG-GOV-DS-016*'
    }

    It 'refuses a rename that would exceed the path length limit' {
        $governed = $script:Baseline | ConvertTo-Json -Depth 20 | ConvertFrom-Json
        $governed.fileNaming.status = 'governed'
        $longName = 'ORG-GOV-DS-016 - ' + ('x' * 200) + '.docx'
        $root = New-TestLibrary -Files @{ $longName = 'x' }
        $plan = New-GovPlan -Inventory (Get-GovInventory -LibraryRoot $root) -Baseline $governed `
                            -LibraryRoot $root -Registry $script:Registry -ArtifactTypes $script:Types `
                            -TagMode Custom -TagValue ('y' * 100) -TagPosition Prefix
        $plan[0].NewName | Should -BeNullOrEmpty
        $plan[0].Notes   | Should -BeLike '*path length*'
    }

    It 'applies the tag only to rows within TagScope' {
        $governed = $script:Baseline | ConvertTo-Json -Depth 20 | ConvertFrom-Json
        $governed.fileNaming.status = 'governed'
        $root = New-TestLibrary -Files @{
            'ORG-GOV-DS-016 - Thing.docx'  = 'x'
            'ORG-GOV-AUP-004 - Other.docx' = 'x'
        }
        $plan = New-GovPlan -Inventory (Get-GovInventory -LibraryRoot $root) -Baseline $governed `
                            -LibraryRoot $root -Registry $script:Registry -ArtifactTypes $script:Types `
                            -TagMode DocumentId -TagScope 'ORG-GOV-DS-016'
        $inScope    = $plan | Where-Object Name -eq 'ORG-GOV-DS-016 - Thing.docx'
        $outOfScope = $plan | Where-Object Name -eq 'ORG-GOV-AUP-004 - Other.docx'
        $inScope.NewName    | Should -BeLike 'ORG-GOV-DS-016*'
        $outOfScope.NewName | Should -BeNullOrEmpty
    }
}
