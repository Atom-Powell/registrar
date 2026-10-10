using Registrar.Core.Model;

namespace Registrar.Validation;

public sealed record ValidationOutcome(ValidationReport Report, PromotionPayload? Payload);
