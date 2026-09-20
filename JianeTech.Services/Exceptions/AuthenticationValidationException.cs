namespace JianeTech.Services.Exceptions
{
    /// <summary>
    /// The service-scoped exception for <c>AuthenticationService</c>. The API controller
    /// maps it — and everything deriving from it — to a 400 carrying <c>ex.Message</c>,
    /// so the message must always be safe to show a signed-out visitor.
    /// </summary>
    public class AuthenticationValidationException : Exception
    {
        /// <summary>
        /// Sign-in attempts left before the account locks, when the caller is allowed to
        /// know. Null whenever surfacing it would confirm that a username exists.
        /// </summary>
        public int? RemainingAttempts { get; }

        public AuthenticationValidationException(string message) : base(message) { }

        public AuthenticationValidationException(string message, int remainingAttempts) : base(message)
        {
            RemainingAttempts = remainingAttempts;
        }
    }
}
