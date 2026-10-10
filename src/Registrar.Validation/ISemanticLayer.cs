using Registrar.Core.Config;
using Registrar.Core.Model;

namespace Registrar.Validation;

public sealed record SemanticContext(
    AgrDocument Agr,
    ConfigBundle Config,
    string Mode,
    IIdRegistry? Registry);

public interface ISemanticLayer
{
    IReadOnlyList<Check> Validate(SemanticContext context);
}
