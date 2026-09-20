namespace JianeTech.Services.Exceptions
{
    /// <summary>
    /// The service-scoped exception for <c>AccountActivationService</c>. The API controller
    /// maps it to a 400 carrying <c>ex.Message</c>.
    /// </summary>
    public class AccountActivationException : Exception
    {
        public AccountActivationException(string message) : base(message) { }
    }
}
