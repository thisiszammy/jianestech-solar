namespace JianeTech.Services.Exceptions
{
    /// <summary>
    /// The service-scoped exception for <c>UserService</c>. The API controller maps it to
    /// a 400 carrying <c>ex.Message</c>.
    /// </summary>
    public class UserValidationException : Exception
    {
        public UserValidationException(string message) : base(message) { }
    }
}
