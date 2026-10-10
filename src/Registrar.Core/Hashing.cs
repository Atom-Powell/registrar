using System.Security.Cryptography;
using System.Text;

namespace Registrar.Core;

public static class Hashing
{
    public static string Sha256Hex(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string Sha256HexOfFile(string path)
        => Sha256Hex(File.ReadAllText(path));

    public static string Sha256Hex(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
