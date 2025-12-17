using System.Collections.Generic;

namespace Chat.Web.Models
{
    /// <summary>
    /// Lightweight user profile used for authentication, presence display and message attribution.
    /// Extended with fixed channel membership and basic contact details (email/mobile) to support notifications.
    /// Supports dual authentication: Entra ID (enterprise) and OTP (guest/fallback).
    /// </summary>
    public class ApplicationUser
    {
        public string UserName { get; set; }
        public string FullName { get; set; }
        public string Avatar { get; set; }

        /// <summary>
        /// Preferred language code for translations (ISO 639-1, e.g., "en", "pl").
        /// Can also be a culture string (e.g., "pl-PL") and will be normalized by the translation pipeline.
        /// </summary>
        public string PreferredLanguage { get; set; }
        
        /// <summary>
        /// Whether this user is allowed to sign in. Defaults to true.
        /// </summary>
        public bool Enabled { get; set; } = true;
        
        /// <summary>
        /// Email address for notification / identity enrichment.
        /// </summary>
        public string Email { get; set; }
        
        /// <summary>
        /// Mobile number (E.164 formatting recommended) for SMS notifications and OTP delivery.
        /// </summary>
        public string MobileNumber { get; set; }
        
        /// <summary>
        /// User Principal Name from Entra ID (e.g., alice@contoso.com).
        /// Used for Entra ID authentication. Null for OTP-only users.
        /// </summary>
        public string Upn { get; set; }
        
        /// <summary>
        /// Entra ID tenant ID (GUID) for the user's organization.
        /// Used for tenant validation in multi-tenant scenarios. Null for OTP-only users.
        /// </summary>
        public string TenantId { get; set; }
        
        /// <summary>
        /// Display name from Entra ID token (may differ from FullName).
        /// Populated during Entra ID authentication.
        /// </summary>
        public string DisplayName { get; set; }
        
        /// <summary>
        /// Country from Entra ID token (ISO 3166-1 alpha-2 code, e.g., "US", "PL").
        /// Populated during Entra ID authentication. Null for OTP-only users.
        /// </summary>
        public string Country { get; set; }
        
        /// <summary>
        /// State/Region from Entra ID token (e.g., "California", "Mazowieckie").
        /// Populated during Entra ID authentication. Null for OTP-only users.
        /// </summary>
        public string Region { get; set; }
        
        /// <summary>
        /// Fixed list of room names this user is allowed to join. Enforced server-side.
        /// </summary>
        public ICollection<string> FixedRooms { get; set; } = new List<string>();

        /// <summary>
        /// Preferred starting room. If user has more than one FixedRoom this selects which to auto-join.
        /// If null/empty and only one FixedRoom exists that one is auto-selected; otherwise first FixedRoom alphabetically.
        /// </summary>
        public string DefaultRoom { get; set; }

        public ICollection<Room> Rooms { get; set; }
        public ICollection<Message> Messages { get; set; }
    }
}
