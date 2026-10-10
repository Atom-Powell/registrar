namespace Registrar.Core.Model;

public sealed record ClauseInsertionAudit(
    string ClauseId, string ExpectedSha, string ActualSha, bool Verified, int TokensResolved);
