using brevo_csharp.Api;
using brevo_csharp.Model;
using JianeTech.Services.Exceptions;
using JianeTech.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using BrevoConfiguration = brevo_csharp.Client.Configuration;

// brevo_csharp.Model ships a CRM entity called Task, which otherwise wins the unqualified
// name over System.Threading.Tasks.Task in every signature in this file.
using Task = System.Threading.Tasks.Task;

namespace JianeTech.Services.Services
{
    /// <summary>
    /// Transactional mail through Brevo's HTTP API. The bodies are not built here: both
    /// mails are Brevo templates (see <c>docs/email/</c>), and this only supplies the
    /// parameters they interpolate. Rewording a mail is a Brevo edit, not a deployment.
    /// </summary>
    /// <remarks>
    /// The only service in the project that calls a third-party API, so per §E of
    /// JianeTech.Services/CLAUDE.md it is also the only one that injects a logger and emits
    /// <c>[ServiceCaller]</c> lines.
    ///
    /// Configuration is read once but validated at <i>send</i> time rather than in the
    /// constructor. A console with no Brevo account yet should still start, still sign
    /// people in and still create accounts — only the mail should fail, and it should fail
    /// with a sentence naming the key that is missing.
    /// </remarks>
    public class BrevoEmailService : IEmailService
    {
        /// <summary>
        /// Matches the sample in the template's own header comment ("22 Sep 2026, 4:12 pm").
        /// The meridiem is lowercased because .NET renders it uppercase and the template
        /// sets it in running text rather than as a figure.
        /// </summary>
        private const string ExpiryFormat = "d MMM yyyy, h:mm tt";

        private readonly string? _apiKey;
        private readonly string? _senderName;
        private readonly string? _senderEmail;
        private readonly long? _activationTemplateId;
        private readonly long? _passwordResetTemplateId;
        private readonly string _consoleBaseUrl;
        private readonly string _helpUrl;
        private readonly string _privacyUrl;

        private readonly ILogger<BrevoEmailService> _logger;

        public BrevoEmailService(IConfiguration configuration, ILogger<BrevoEmailService> logger)
        {
            _logger = logger;

            _apiKey = Trimmed(configuration["Brevo:ApiKey"]);
            _senderName = Trimmed(configuration["Brevo:SenderName"]);
            _senderEmail = Trimmed(configuration["Brevo:SenderEmail"]);
            _activationTemplateId = ParseTemplateId(configuration["Brevo:ActivationTemplateId"]);
            _passwordResetTemplateId = ParseTemplateId(configuration["Brevo:PasswordResetTemplateId"]);

            // Trailing slash trimmed once here so every link below is built the same way.
            _consoleBaseUrl = (Trimmed(configuration["App:ConsoleBaseUrl"]) ?? string.Empty).TrimEnd('/');
            _helpUrl = Trimmed(configuration["App:HelpUrl"]) ?? string.Empty;
            _privacyUrl = Trimmed(configuration["App:PrivacyUrl"]) ?? string.Empty;
        }

        public Task SendActivationEmailAsync(
            string toEmail,
            string firstName,
            string fullName,
            string username,
            Guid activationToken,
            DateTime expiresOn,
            CancellationToken cancellationToken)
        {
            var parameters = BuildParameters(
                firstName,
                fullName,
                username,
                "ACTIVATION_URL",
                BuildLink("activate", activationToken),
                expiresOn);

            return SendTemplateAsync(
                nameof(SendActivationEmailAsync),
                toEmail,
                fullName,
                _activationTemplateId,
                "Brevo:ActivationTemplateId",
                parameters,
                cancellationToken);
        }

        public Task SendPasswordResetEmailAsync(
            string toEmail,
            string firstName,
            string fullName,
            string username,
            Guid resetToken,
            DateTime expiresOn,
            CancellationToken cancellationToken)
        {
            var parameters = BuildParameters(
                firstName,
                fullName,
                username,
                "RESET_URL",
                BuildLink("reset-password", resetToken),
                expiresOn);

            return SendTemplateAsync(
                nameof(SendPasswordResetEmailAsync),
                toEmail,
                fullName,
                _passwordResetTemplateId,
                "Brevo:PasswordResetTemplateId",
                parameters,
                cancellationToken);
        }

        /// <summary>
        /// The parameter names are the template's contract — see the header comment in
        /// <c>docs/email/brevo-account-activation.html</c>. Optional ones are sent as an
        /// empty string rather than omitted, because the templates test them with an
        /// <c>if</c> tag, where a missing key and an empty one read the same.
        /// </summary>
        private Dictionary<string, object> BuildParameters(
            string firstName,
            string fullName,
            string username,
            string linkParameterName,
            string link,
            DateTime expiresOn)
            => new()
            {
                ["FIRSTNAME"] = firstName ?? string.Empty,
                ["FULLNAME"] = fullName ?? string.Empty,
                ["USERNAME"] = username ?? string.Empty,
                [linkParameterName] = link,
                ["EXPIRES_AT"] = expiresOn.ToString(ExpiryFormat).Replace("AM", "am").Replace("PM", "pm"),
                ["HELP_URL"] = _helpUrl,
                ["PRIVACY_URL"] = _privacyUrl,
            };

        /// <summary>
        /// The address the recipient clicks. <c>t</c> is the raw token Guid — the database
        /// holds only its hash, so this string is the one copy that can open the link.
        /// </summary>
        private string BuildLink(string route, Guid token)
            => $"{_consoleBaseUrl}/{route}?t={token}";

        private async Task SendTemplateAsync(
            string process,
            string toEmail,
            string toName,
            long? templateId,
            string templateIdKey,
            Dictionary<string, object> parameters,
            CancellationToken cancellationToken)
        {
            var logId = Guid.NewGuid();

            RequireConfiguration(templateId, templateIdKey);

            // Deliberately not the parameters bag: it carries the link, and the link is the
            // token. Recipient and template are enough to correlate a send with a bounce.
            var data = new { toEmail, templateId };

            try
            {
                _logger.LogInformation(
                    "[ServiceCaller] {logid}: Method {process} Started. -> {@data}", logId, process, data);

                cancellationToken.ThrowIfCancellationRequested();

                var payload = new SendSmtpEmail(
                    to: new List<SendSmtpEmailTo>
                    {
                        new SendSmtpEmailTo(
                            email: toEmail,
                            name: string.IsNullOrWhiteSpace(toName) ? null : toName),
                    },
                    templateId: templateId,
                    _params: parameters);

                // Only when configured: with a template id and no sender, Brevo falls back
                // to the template's own sender, which is what most accounts actually want.
                if (_senderEmail is { Length: > 0 })
                {
                    payload.Sender = new SendSmtpEmailSender(name: _senderName, email: _senderEmail);
                }

                var response = await BuildApi().SendTransacEmailAsync(payload);

                _logger.LogInformation(
                    "[ServiceCaller] {logid}: Method {process} Finished Successfully. -> {@response}",
                    logId, process, new { response?.MessageId });
            }
            catch (EmailDeliveryException ex)
            {
                _logger.LogWarning(ex, "[ServiceCaller] {logid}: Method {process} failed with expected error", logId, process);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ServiceCaller] {logid}: Method {process} failed with unexpected error", logId, process);

                // Wrapped so callers have one type to catch and one sentence to show. The
                // Brevo exception's own message can echo the request body — which carries
                // the link, and therefore the token — so it stays in the log and out of the
                // response.
                throw new EmailDeliveryException(
                    "The email could not be sent. Check the Brevo settings and try again.", ex);
            }
        }

        private TransactionalEmailsApi BuildApi()
        {
            var configuration = new BrevoConfiguration();
            configuration.AddApiKey("api-key", _apiKey);
            return new TransactionalEmailsApi(configuration);
        }

        /// <summary>
        /// Names the missing key rather than reporting a generic failure. These values come
        /// from .env, and "Brevo__ApiKey is not set" is a sentence someone can act on.
        /// </summary>
        private void RequireConfiguration(long? templateId, string templateIdKey)
        {
            if (string.IsNullOrWhiteSpace(_apiKey))
                throw new EmailDeliveryException(
                    "Email is not configured: set Brevo__ApiKey in .env and restart the API.");

            if (templateId is null)
                throw new EmailDeliveryException(
                    $"Email is not configured: set {templateIdKey.Replace(":", "__")} in .env and restart the API.");

            if (string.IsNullOrWhiteSpace(_consoleBaseUrl))
                throw new EmailDeliveryException(
                    "Email is not configured: set App__ConsoleBaseUrl in .env so the link in the mail points somewhere.");
        }

        private static string? Trimmed(string? value)
            => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static long? ParseTemplateId(string? value)
            => long.TryParse(Trimmed(value), out var id) && id > 0 ? id : null;
    }
}
