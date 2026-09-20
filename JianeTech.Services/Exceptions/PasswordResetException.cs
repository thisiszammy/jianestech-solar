namespace JianeTech.Services.Exceptions
{
    /// <summary>
    /// The service-scoped exception for <c>PasswordResetService</c>. The API controller
    /// maps it to a 400 carrying <c>ex.Message</c>.
    /// </summary>
    public class PasswordResetException : Exception
    {
        public PasswordResetException(string message) : base(message) { }
    }
}
