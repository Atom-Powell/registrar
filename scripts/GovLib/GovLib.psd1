@{
    RootModule        = 'GovLib.psm1'
    ModuleVersion     = '1.0.0'
    GUID              = 'b3f1c2d4-5a6e-4f70-8b91-2c3d4e5f6a7b'
    Author            = 'ORG Registry Owner'
    Description       = 'Governance library normalization helpers for The Registrar.'
    PowerShellVersion = '7.0'
    FunctionsToExport = @(
        'Import-GovBaseline',
        'Test-GovProtectedPath', 'Get-GovProtectionReason',
        'Test-GovPathUnderRoot', 'Test-GovPathLength',
        'Import-GovRegistry',
        'Import-GovArtifactTypes', 'Get-GovDocumentIdFromName', 'Resolve-GovIdentity',
        'Get-GovInventory', 'Join-GovSharePointExport',
        'New-GovPlan', 'Get-GovPlacementTarget', 'Get-GovSourceStamp',
        'New-GovJournal', 'Add-GovJournalEntry', 'Read-GovJournal',
        'Invoke-GovApply', 'Test-GovApproved', 'Get-GovApprovalTokens',
        'Invoke-GovUndo',
        'Invoke-GovVerify',
        'New-GovReport',
        'New-GovRunContext', 'Import-GovRegistryContext',
        'Test-GovConflictCopy', 'Test-GovSyncQuiet'
    )
}
