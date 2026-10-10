using System.Text.Json.Nodes;
using Registrar.Core.Config;

namespace Registrar.Core.Governor;

// The AGR schema travels alongside the config bundle rather than inside it: required
// fields, the outputFileName pattern, and the blockType enum are schema facts, not
// config facts, and folding them into ConfigBundle would blur that line.
//
// SourceHashes is its own map rather than a reuse of ConfigBundle.Hashes: that map
// deliberately excludes qa-checklist.json (see the comment in ConfigLoader.cs) and never
// contained agr.schema.json, so correction.md, which is generated entirely from
// qa-checklist.json, would otherwise publish a provenance table that omits its only
// source. ConfigBundle.Hashes itself must not change: it feeds the validation report's
// recorded config set.
public sealed record KnowledgeSource(
    ConfigBundle Config,
    JsonNode AgrSchema,
    IReadOnlyDictionary<string, string> SourceHashes);
