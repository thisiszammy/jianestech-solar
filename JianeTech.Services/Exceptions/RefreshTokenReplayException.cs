namespace JianeTech.Services.Exceptions
{
    /// <summary>
    /// A refresh token was presented whose row is already consumed. That means either a
    /// stolen cookie or a client replaying a rotation, and both are handled the same way:
    /// every live grant for <see cref="UserId"/> is revoked before this propagates.
    /// </summary>
    public class RefreshTokenReplayException : AuthenticationValidationException
    {
        public Guid UserId { get; }

        public RefreshTokenReplayException(string message, Guid userId) : base(message)
        {
            UserId = userId;
        }
    }
}
