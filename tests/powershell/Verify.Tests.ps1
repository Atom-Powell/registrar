#Requires -Version 7.0
BeforeAll {
    $script:RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
    Import-Module (Join-Path $script:RepoRoot 'scripts/GovLib/GovLib.psd1') -Force
    Import-Module (Join-Path $PSScriptRoot 'TestHelpers.psm1') -Force

    function Save-VerifyPlan {
        param([object[]] $Rows, [string] $Root)
        $p = Join-Path $Root 'plan.csv'
        $Rows | Export-Csv -LiteralPath $p -NoTypeInformation -Encoding utf8NoBOM
        return $p
    }
    function New-VerifyRow {
        param([string] $Name, [string] $Suggested, [string] $Approved = 'Y', [string] $NewName = '')
        [pscustomobject]@{
            Name = $Name; RelativePath = $Name; SourceStamp = 'sffffffffffffffff'
            SuggestedFolder = $Suggested; ActionRequired = 'Move to Existing Folder'
            Approved = $Approved; NewName = $NewName
        }
    }
}

Describe 'Invoke-GovVerify' {
    It 'reports Ok when every approved row landed' {
        $root = New-TestLibrary -Files @{ 'Controlled Documents/Governance/a.docx' = 'x' }
        $plan = Save-VerifyPlan -Root $root -Rows @(New-VerifyRow -Name 'a.docx' -Suggested 'Controlled Documents/Governance')
        $r = Invoke-GovVerify -PlanPath $plan -LibraryRoot $root
        $r.Ok      | Should -BeTrue
        $r.Landed  | Should -Be 1
        $r.Missing | Should -BeNullOrEmpty
    }

    It 'reports a missing file when an approved row did not land' {
        $root = New-TestLibrary -Files @{ 'a.docx' = 'x' }
        $plan = Save-VerifyPlan -Root $root -Rows @(New-VerifyRow -Name 'a.docx' -Suggested 'Controlled Documents/Governance')
        $r = Invoke-GovVerify -PlanPath $plan -LibraryRoot $root
        $r.Ok      | Should -BeFalse
        $r.Missing | Should -Contain 'Controlled Documents/Governance/a.docx'
    }

    It 'accounts for a renamed destination' {
        $root = New-TestLibrary -Files @{ 'Controlled Documents/Governance/ORG-GOV-DS-016 - a.docx' = 'x' }
        $plan = Save-VerifyPlan -Root $root -Rows @(New-VerifyRow -Name 'a.docx' -Suggested 'Controlled Documents/Governance' -NewName 'ORG-GOV-DS-016 - a.docx')
        (Invoke-GovVerify -PlanPath $plan -LibraryRoot $root).Ok | Should -BeTrue
    }

    It 'ignores rows that were never approved' {
        $root = New-TestLibrary -Files @{ 'a.docx' = 'x' }
        $plan = Save-VerifyPlan -Root $root -Rows @(New-VerifyRow -Name 'a.docx' -Suggested 'Controlled Documents/Governance' -Approved '')
        (Invoke-GovVerify -PlanPath $plan -LibraryRoot $root).Ok | Should -BeTrue
    }

    It 'reports an approved row with a blank SuggestedFolder as missing' {
        $root = New-TestLibrary -Files @{ 'a.docx' = 'x' }
        $plan = Save-VerifyPlan -Root $root -Rows @(New-VerifyRow -Name 'a.docx' -Suggested '')
        $r = Invoke-GovVerify -PlanPath $plan -LibraryRoot $root
        $r.Ok     | Should -BeFalse
        $r.Missing | Should -Contain 'a.docx (approved row has a blank Suggested Folder)'
    }

    It 'still ignores an unapproved row with a blank SuggestedFolder' {
        $root = New-TestLibrary -Files @{ 'a.docx' = 'x' }
        $plan = Save-VerifyPlan -Root $root -Rows @(New-VerifyRow -Name 'a.docx' -Suggested '' -Approved '')
        (Invoke-GovVerify -PlanPath $plan -LibraryRoot $root).Ok | Should -BeTrue
    }

    It 'does not count a row marked N, matching what Apply did with it' {
        # Verify used to share Apply's any-non-blank-value rule, so it confirmed
        # the very moves that rule caused. Both now route through Test-GovApproved.
        $root = New-TestLibrary -Files @{ 'a.docx' = 'x' }
        $plan = Save-VerifyPlan -Root $root -Rows @(New-VerifyRow -Name 'a.docx' `
                                                    -Suggested 'Controlled Documents/Governance' -Approved 'N')
        $r = Invoke-GovVerify -PlanPath $plan -LibraryRoot $root
        $r.Ok     | Should -BeTrue
        $r.Landed | Should -Be 0
    }

    It 'reports a row whose destination exists but whose source never left' {
        # The overwrite-skip case: Apply refused to overwrite, so a file of that
        # name sits at the destination while the original is still at the root.
        # Arrival alone used to read as Ok = True, Landed = 1.
        $root = New-TestLibrary -Files @{
            'a.docx'                                 = 'new'
            'Controlled Documents/Governance/a.docx' = 'old'
        }
        $plan = Save-VerifyPlan -Root $root -Rows @(New-VerifyRow -Name 'a.docx' -Suggested 'Controlled Documents/Governance')
        $r = Invoke-GovVerify -PlanPath $plan -LibraryRoot $root
        $r.Ok      | Should -BeFalse
        $r.Missing | Should -Contain 'a.docx (still present at source)'
    }

    It 'throws when the plan is missing a required column' {
        $root = New-TestLibrary -Files @{ 'a.docx' = 'x' }
        $p = Join-Path $root 'bad.csv'
        Set-Content -LiteralPath $p -Value "Name,Approved`na.docx,Y" -Encoding utf8NoBOM
        { Invoke-GovVerify -PlanPath $p -LibraryRoot $root } | Should -Throw '*missing required column*'
    }
}
