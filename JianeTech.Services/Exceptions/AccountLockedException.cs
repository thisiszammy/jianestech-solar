namespace JianeTech.Services.Exceptions
{
    /// <summary>
    /// Raised when the account exists but is locked out. Derives from the service-scoped
    /// exception so a controller that only catches the base type still returns a 400,
    /// while one that catches this first can render the dedicated lock-out state.
    /// </summary>
    public class AccountLockedException : AuthenticationValidationException
    {
        public AccountLockedException(string message) : base(message) { }
    }
}
