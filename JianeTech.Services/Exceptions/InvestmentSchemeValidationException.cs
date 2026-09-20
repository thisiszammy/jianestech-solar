namespace JianeTech.Services.Exceptions
{
    /// <summary>
    /// The service-scoped exception for <c>InvestmentSchemeService</c>. The API controller
    /// maps it to a 400 carrying <c>ex.Message</c>.
    /// </summary>
    public class InvestmentSchemeValidationException : Exception
    {
        public InvestmentSchemeValidationException(string message) : base(message) { }
    }
}
