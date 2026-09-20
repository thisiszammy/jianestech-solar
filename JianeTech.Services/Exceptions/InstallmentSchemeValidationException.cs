namespace JianeTech.Services.Exceptions
{
    /// <summary>
    /// The service-scoped exception for <c>InstallmentSchemeService</c>. The API controller
    /// maps it to a 400 carrying <c>ex.Message</c>.
    /// </summary>
    public class InstallmentSchemeValidationException : Exception
    {
        public InstallmentSchemeValidationException(string message) : base(message) { }
    }
}
