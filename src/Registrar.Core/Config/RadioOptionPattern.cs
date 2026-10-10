namespace Registrar.Core.Config;

// The radio-option pattern from ORG-MST-WFE-001, bound from the top-level
// "radioOptionPattern" node of style-rules.json (a sibling of "visualIdentity",
// which is what StyleRules maps to). Kept off StyleRules deliberately so that
// record continues to correspond 1:1 with the visualIdentity node.
//
// Two keys in that node are documentary and are NOT bound here: "descriptorLine"
// (prose describing the line, superseded by DescriptorLineFormat) and
// "prohibitedRenderings" (checkboxes/bullets/numbered lists, which the renderer
// makes impossible by construction rather than checking at runtime).
public sealed record RadioOptionPattern
{
    public required string Glyph { get; init; }
    public required string GlyphColor { get; init; }
    public required bool OptionsIndented { get; init; }
    public required int OptionIndentTwips { get; init; }
    public required int MinimumOptions { get; init; }
    public required IReadOnlyList<string> StandardOptionSet { get; init; }

    // Composed by RadioQuestionBlock. Uses {number}/{requirement} rather than
    // bracketed tokens: the QA-ENG-07 residue scan matches the bracketed
    // upper-case token shape and would flag [NUMBER] as unresolved residue.
    public required string DescriptorLineFormat { get; init; }
    public required string RequiredLabel { get; init; }
    public required string OptionalLabel { get; init; }
}
