using System.Security.Cryptography;

namespace JianeTech.Services.Security
{
    /// <summary>
    /// Refresh tokens are stored as the SHA-256 of the Guid handed to the browser. A
    /// leaked database row therefore cannot be replayed as a cookie value, and lookups
    /// stay an exact match on a fixed-width key.
    /// </summary>
    public static class TokenHasher
    {
        public static string Hash(Guid raw)
        {
            Span<byte> rawBytes = stackalloc byte[16];
            if (!raw.TryWriteBytes(rawBytes))
                throw new InvalidOperationException("Failed to serialize token id.");

            Span<byte> hash = stackalloc byte[32];
            SHA256.HashData(rawBytes, hash);

            return Convert.ToHexString(hash);
        }
    }
}
