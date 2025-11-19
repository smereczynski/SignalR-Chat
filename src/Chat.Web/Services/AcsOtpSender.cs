using System;
using System.Threading.Tasks;
using Azure.Communication.Email;
using Azure.Communication.Sms;
using Chat.Web.Options;
using Chat.Web.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Localization;

namespace Chat.Web.Services
{
    /// <summary>
    /// Azure Communication Services sender: routes to Email if destination contains '@', otherwise SMS.
    /// Requires connection string + channel-specific from addresses in options.
    /// </summary>
    public class AcsOtpSender : IOtpSender
    {
        private readonly AcsOptions _options;
    private readonly EmailClient _emailClient;
    private readonly SmsClient _smsClient;
    private readonly ILogger<AcsOtpSender> _logger;
        private readonly IStringLocalizer<Resources.SharedResources> _localizer;
        private static DateTimeOffset _cooldownUntil = DateTimeOffset.MinValue;
        private static readonly object _gate = new object();
        private const int CooldownSeconds = 15; // brief cooldown after repeated failures

    /// <summary>
    /// Validates configuration and primes Email + SMS clients.
    /// </summary>
    public AcsOtpSender(AcsOptions options, ILogger<AcsOtpSender> logger, IStringLocalizer<Resources.SharedResources> localizer)
        {
            _options = options;
            if (string.IsNullOrWhiteSpace(options?.ConnectionString))
                throw new InvalidOperationException("ACS ConnectionString is required when using AcsOtpSender.");
            _emailClient = new EmailClient(options.ConnectionString);
            _smsClient = new SmsClient(options.ConnectionString);
            _logger = logger;
            _localizer = localizer;
        }

    /// <inheritdoc />
    public async Task SendAsync(string userName, string destination, string code)
        {
            if (string.IsNullOrWhiteSpace(destination))
                throw new ArgumentException("Destination is required for ACS OTP sending", nameof(destination));

            // Lightweight cooldown to reduce pressure during persistent outages.
            var now = DateTimeOffset.UtcNow;
            if (now < _cooldownUntil)
            {
                _logger?.LogWarning("Skipping ACS send for {User} due to cooldown until {Until}", SanitizeForLog(userName), _cooldownUntil);
                return;
            }

            // Detect if this is a chat notification payload rather than an OTP numeric code.
            var isNotification = !string.IsNullOrWhiteSpace(code) && code.StartsWith("New message in #", StringComparison.OrdinalIgnoreCase);

            if (IsEmail(destination))
            {
                if (string.IsNullOrWhiteSpace(_options.EmailFrom))
                    throw new InvalidOperationException("AcsOptions.EmailFrom is required to send email OTP.");

                var subject = isNotification ? _localizer["EmailSubjectNewMessage"].Value : _localizer["EmailSubjectVerificationCode"].Value;
                var body = isNotification ? code : _localizer["EmailBodyVerificationCode", code].Value;
                // Do not block on provider end-to-end completion; start send and return, with retries
                try
                {
                    await RetryHelper.ExecuteAsync(
                    ct => _emailClient.SendAsync(Azure.WaitUntil.Started, _options.EmailFrom, destination, subject, body, cancellationToken: ct),
                    Transient.IsNetworkTransient,
                    _logger,
                    "acs.email.send",
                    maxAttempts: 3,
                    baseDelayMs: 200,
                    perAttemptTimeoutMs: 5000);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "ACS email send failed for {User}", SanitizeForLog(userName));
                    ArmCooldown();
                    throw;
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(_options.SmsFrom))
                    throw new InvalidOperationException("AcsOptions.SmsFrom is required to send SMS OTP.");

                var body = isNotification ? code : _localizer["SmsBodyVerificationCode", code].Value;
                try
                {
                    await RetryHelper.ExecuteAsync(
                    ct => _smsClient.SendAsync(
                        from: _options.SmsFrom, 
                        to: destination, 
                        message: body, 
                        options: new SmsSendOptions(enableDeliveryReport: true) { Tag = "SMS Sent" },
                        cancellationToken: ct),
                    Transient.IsNetworkTransient,
                    _logger,
                    "acs.sms.send",
                    maxAttempts: 3,
                    baseDelayMs: 200,
                    perAttemptTimeoutMs: 5000);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "ACS SMS send failed for {User}", SanitizeForLog(userName));
                    ArmCooldown();
                    throw;
                }
            }
        }

        private static bool IsEmail(string s)
        {
            return s.Contains("@");
        }

        /// <summary>
        /// Sanitizes user input for safe logging by removing all ASCII control characters
        /// (including newlines, carriage returns, tabs, etc) to prevent log forging attacks.
        /// </summary>
        private static string SanitizeForLog(string input)
        {
            if (string.IsNullOrEmpty(input))
                return "[empty]";
            // Remove all control chars (ASCII and Unicode), and trim spaces.
            var sb = new System.Text.StringBuilder(input.Length);
            foreach (var c in input)
            {
                if (!char.IsControl(c))
                    sb.Append(c);
            }
            // Optionally, truncate to a safe length (e.g., 100 chars).
            var sanitized = sb.ToString().Trim();
            if (sanitized.Length > 100) sanitized = sanitized.Substring(0, 100) + "...";
            // Delimit with square brackets so log entries are visually isolated.
            return "[" + sanitized + "]";
        }

        private static void ArmCooldown()
        {
            var until = DateTimeOffset.UtcNow.AddSeconds(CooldownSeconds);
            lock (_gate)
            {
                if (until > _cooldownUntil)
                {
                    _cooldownUntil = until;
                }
            }
        }
    }
}
