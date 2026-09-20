namespace JianeTech.Services.Exceptions
{
    /// <summary>
    /// Mail could not be handed to Brevo — the account is unconfigured, the key was
    /// refused, or the API call failed. Distinct from the validation exceptions because
    /// nothing the caller submitted was wrong: the database write that preceded it stands,
    /// and the right answer is to issue the link again rather than to correct an input.
    /// </summary>
    public class EmailDeliveryException : Exception
    {
        public EmailDeliveryException(string message) : base(message) { }

        public EmailDeliveryException(string message, Exception innerException)
            : base(message, innerException) { }
    }
}
