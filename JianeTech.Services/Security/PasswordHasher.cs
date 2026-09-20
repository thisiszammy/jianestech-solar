using System.Security.Cryptography;
using System.Text;

namespace JianeTech.Services.Security
{
    /// <summary>
    /// PBKDF2-SHA256 with a per-user salt. ASP.NET Core Identity is deliberately not in
    /// use here, so this is the only place a password becomes a stored value — the salt
    /// lives in its own <c>Users.Salt</c> column and is required to verify.
    /// </summary>
    public static class PasswordHasher
    {
        private const int SaltBytes = 32;
        private const int HashBytes = 32;
        private const int Iterations = 100_000;
        private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA256;

        public static (string HashBase64, string SaltBase64) Hash(string password)
        {
            ArgumentException.ThrowIfNullOrEmpty(password);

            var salt = RandomNumberGenerator.GetBytes(SaltBytes);
            var hash = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password),
                salt,
                Iterations,
                Algorithm,
                HashBytes);

            return (Convert.ToBase64String(hash), Convert.ToBase64String(salt));
        }

        /// <summary>
        /// A credential nobody holds, for an invited account that has not chosen one yet.
        /// </summary>
        /// <remarks>
        /// <c>dbo.Users.Password</c> is NOT NULL, so an invited row has to carry something,
        /// and that something must not be a value anyone could present. Hashing fresh
        /// cryptographic random means the plaintext is never written down, never sent, and
        /// is discarded before this method returns — so sign-in for such an account fails
        /// the ordinary way, at <see cref="Verify"/>, with no special case anywhere.
        ///
        /// <see cref="Interfaces.IAccountActivationService.ActivateAccountAsync"/> replaces
        /// it with the password the invitee chooses.
        /// </remarks>
        public static (string HashBase64, string SaltBase64) CreateUnusable()
            => Hash(Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)));

        public static bool Verify(string password, string hashBase64, string saltBase64)
        {
            if (string.IsNullOrEmpty(password)
                || string.IsNullOrEmpty(hashBase64)
                || string.IsNullOrEmpty(saltBase64))
            {
                return false;
            }

            byte[] expected;
            byte[] salt;
            try
            {
                expected = Convert.FromBase64String(hashBase64);
                salt = Convert.FromBase64String(saltBase64);
            }
            catch (FormatException)
            {
                // A stored credential that is not valid base64 cannot match anything.
                // Fail closed rather than letting a malformed row throw out of sign-in.
                return false;
            }

            var actual = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password),
                salt,
                Iterations,
                Algorithm,
                expected.Length);

            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
    }
}
