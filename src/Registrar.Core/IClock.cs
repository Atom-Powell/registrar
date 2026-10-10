namespace Registrar.Core;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
