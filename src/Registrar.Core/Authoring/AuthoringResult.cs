using Registrar.Core.Model;

namespace Registrar.Core.Authoring;

// A field the technician has not filled in yet. Deliberately not a Check: that type
// carries checkId, ruleSource, and errorCategory, none of which apply to an unfinished
// form, and forcing it would blur a governance violation into a blank field.
//
// JsonPath is a JSON Pointer into the AGR, chosen to match the validation report's paths
// wherever the report has one to match (most of them). Some gaps, such as a missing
// sourceWorkflowContext, name the field directly even though no validation layer reports
// at that exact path (the schema layer would report a missing required property at "/",
// not "/sourceWorkflowContext"); that is still the correct pointer to the missing thing
// and what a GUI needs to focus a field, but it is not evidence of a universal
// correspondence with the validation report.
public sealed record AuthoringGap(string JsonPath, string Message);

public sealed record AuthoringResult(AgrDocument? Document, IReadOnlyList<AuthoringGap> Gaps);
