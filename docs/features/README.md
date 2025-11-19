# Features

Learn about SignalR Chat's features and how they work.

## Core Features

### Authentication & Sessions
- **[Authentication](authentication.md)** - Dual authentication: Microsoft Entra ID (SSO) + OTP fallback with Argon2id hashing
- **[Entra ID Setup](../development/entra-id-multi-tenant-setup.md)** - Multi-tenant Entra ID configuration guide
- **[Sessions](sessions.md)** - Session management and security headers

### Real-time Communication
- **[Real-time Messaging](real-time-messaging.md)** - SignalR implementation details
- **[Presence Tracking](presence.md)** - Online/offline status and visibility
- **[Read Receipts](read-receipts.md)** - Message read status tracking

### User Experience
- **[Localization](localization.md)** - Multi-language support (9 languages)
- **[Notifications](notifications.md)** - Email/SMS notifications for unread messages
- **[Pagination](pagination.md)** - Efficient message loading

### Security & Performance
- **[Rate Limiting](rate-limiting.md)** - Abuse prevention and throttling
- Security headers (CSP, HSTS) - See [Sessions](sessions.md)

## Feature Status

| Feature | Status | Documentation |
|---------|--------|---------------|
| Entra ID Authentication | ✅ Production | [authentication.md](authentication.md), [Entra ID Setup](../development/entra-id-multi-tenant-setup.md) |
| OTP Authentication (Fallback) | ✅ Production | [authentication.md](authentication.md) |
| Real-time Messaging | ✅ Production | [real-time-messaging.md](real-time-messaging.md) |
| Read Receipts | ✅ Production | [read-receipts.md](read-receipts.md) |
| Presence Tracking | ✅ Production | [presence.md](presence.md) |
| Localization (9 languages) | ✅ Production | [localization.md](localization.md) |
| Rate Limiting | ✅ Production | [rate-limiting.md](rate-limiting.md) |
| Email/SMS Notifications | ✅ Production | [notifications.md](notifications.md) |
| Message Pagination | ✅ Production | [pagination.md](pagination.md) |

## Quick Links

- [Architecture Overview](../architecture/overview.md)
- [Getting Started](../getting-started/)
- [Deployment Guide](../deployment/)

[Back to documentation home](../README.md)
