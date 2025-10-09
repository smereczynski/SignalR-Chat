using System;
using System.Threading.Tasks;
using Azure.Communication.Email;
using Azure.Communication.Sms;
using Chat.Web.Options;

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

    /// <summary>
    /// Validates configuration and primes Email + SMS clients.
    /// </summary>
    public AcsOtpSender(AcsOptions options)
        {
            _options = options;
            if (string.IsNullOrWhiteSpace(options?.ConnectionString))
                throw new InvalidOperationException("ACS ConnectionString is required when using AcsOtpSender.");
            _emailClient = new EmailClient(options.ConnectionString);
            _smsClient = new SmsClient(options.ConnectionString);
        }

    /// <inheritdoc />
    public async Task SendAsync(string userName, string destination, string code)
        {
            if (string.IsNullOrWhiteSpace(destination))
                throw new ArgumentException("Destination is required for ACS OTP sending", nameof(destination));

            if (IsEmail(destination))
            {
                if (string.IsNullOrWhiteSpace(_options.EmailFrom))
                    throw new InvalidOperationException("AcsOptions.EmailFrom is required to send email OTP.");

                var subject = "Your verification code";
                var body = $"Your verification code is: {code}";
                // Do not block on provider end-to-end completion on the first request; start send and return
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(10));
                await _emailClient.SendAsync(Azure.WaitUntil.Started, _options.EmailFrom, destination, subject, body, cancellationToken: cts.Token);
            }
            else
            {
                if (string.IsNullOrWhiteSpace(_options.SmsFrom))
                    throw new InvalidOperationException("AcsOptions.SmsFrom is required to send SMS OTP.");

                var body = $"Code: {code}";
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(10));
                await _smsClient.SendAsync(from: _options.SmsFrom, to: destination, message: body, cancellationToken: cts.Token);
            }
        }

        private static bool IsEmail(string s)
        {
            return s.Contains("@");
        }
    }
}
