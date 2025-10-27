using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace Chat.Web.Controllers
{
    /// <summary>
    /// Provides localized strings to JavaScript clients based on the current request culture.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class LocalizationController : ControllerBase
    {
        private readonly IStringLocalizer<Resources.SharedResources> _localizer;

        public LocalizationController(IStringLocalizer<Resources.SharedResources> localizer)
        {
            _localizer = localizer;
        }

        /// <summary>
        /// Returns all JavaScript-needed strings for the current culture.
        /// Used by client-side code to populate window.i18n object.
        /// </summary>
        [HttpGet("strings")]
        [ResponseCache(Duration = 3600, VaryByHeader = "Cookie,Accept-Language")]
        public IActionResult GetStrings()
        {
            return Ok(new
            {
                // Application Common
                Loading = _localizer["Loading"].Value,
                Error = _localizer["Error"].Value,
                Retry = _localizer["Retry"].Value,
                Search = _localizer["Search"].Value,
                
                // Chat Interface
                ChatRooms = _localizer["ChatRooms"].Value,
                SelectRoomToJoin = _localizer["SelectRoomToJoin"].Value,
                NoMessages = _localizer["NoMessages"].Value,
                MessageInputPlaceholder = _localizer["MessageInputPlaceholder"].Value,
                MessagesWaitingToSend = _localizer["MessagesWaitingToSend"].Value,
                WhosHere = _localizer["WhosHere"].Value,
                Reconnecting = _localizer["Reconnecting"].Value,
                Disconnected = _localizer["Disconnected"].Value,
                MessageFailed = _localizer["MessageFailed"].Value,
                MessagePending = _localizer["MessagePending"].Value,
                
                // Login / OTP
                User = _localizer["User"].Value,
                LoadingUsers = _localizer["LoadingUsers"].Value,
                SelectUser = _localizer["SelectUser"].Value,
                SendCode = _localizer["SendCode"].Value,
                Code = _localizer["Code"].Value,
                SixDigitCode = _localizer["SixDigitCode"].Value,
                Resend = _localizer["Resend"].Value,
                Verify = _localizer["Verify"].Value,
                SendingCode = _localizer["SendingCode"].Value,
                SentToEmailAndMobile = _localizer["SentToEmailAndMobile"].Value,
                FailedToSend = _localizer["FailedToSend"].Value,
                
                // Date/Time
                Today = _localizer["Today"].Value,
                Yesterday = _localizer["Yesterday"].Value,
                AM = _localizer["AM"].Value,
                PM = _localizer["PM"].Value,
                
                // Error Messages
                ErrorOccurred = _localizer["ErrorOccurred"].Value,
                FailedToLoadUsers = _localizer["FailedToLoadUsers"].Value,
                UserSelectionRequired = _localizer["UserSelectionRequired"].Value,
                PleaseWaitSeconds = _localizer["PleaseWaitSeconds"].Value,
                FailedToSendCode = _localizer["FailedToSendCode"].Value,
                ErrorSendingCode = _localizer["ErrorSendingCode"].Value,
                UserAndCodeRequired = _localizer["UserAndCodeRequired"].Value,
                InvalidVerificationCode = _localizer["InvalidVerificationCode"].Value,
                VerificationFailed = _localizer["VerificationFailed"].Value,
                LoginSuccessful = _localizer["LoginSuccessful"].Value,
                SessionExpired = _localizer["SessionExpired"].Value,
                SendingTooQuickly = _localizer["SendingTooQuickly"].Value
            });
        }
    }
}
