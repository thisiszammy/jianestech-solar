namespace JianeTech.Services.Security
{
    /// <summary>
    /// The one definition of the password policy. The sign-up form hint and any
    /// client-side strength meter mirror these numbers — change them together.
    /// </summary>
    public static class PasswordRules
    {
        public const int MinimumLength = 8;

        public const int MaximumLength = 128;

        public static string Describe()
            => $"Password must be at least {MinimumLength} characters.";
    }
}
