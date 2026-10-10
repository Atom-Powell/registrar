using Registrar.Core;

namespace Registrar.Tests.Core;

public class HashingTests
{
    [Fact]
    public void Sha256Hex_returns_known_lowercase_hex_for_empty_string()
    {
        // Well-known SHA-256 of the empty string.
        Assert.Equal(
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            Hashing.Sha256Hex(string.Empty));
    }

    [Fact]
    public void Sha256Hex_is_stable_for_same_input()
    {
        Assert.Equal(Hashing.Sha256Hex("abc"), Hashing.Sha256Hex("abc"));
    }
}
