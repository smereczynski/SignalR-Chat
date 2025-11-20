# ADR: Switch from User Dropdown to SSO-First + Email OTP Fallback Login

**Status:** Accepted  
**Date:** 2025-11-20  

## Context
The original login flow used a dropdown to select a fixed user for OTP authentication. This approach was simple for demos but not scalable, secure, or user-friendly for real deployments. The project now supports Microsoft Entra ID (SSO) and needs a modern, production-ready login experience.

## Decision
- The login page now presents SSO (Microsoft/Entra ID) as the primary sign-in method.
- If SSO is unavailable or fails, users can enter their email address to receive a one-time password (OTP) for fallback authentication.
- The user dropdown and `/api/auth/users` endpoint are removed.
- All i18n keys and frontend logic are updated to use email-based input and validation.
- Tests and resource files are aligned with the new keys (`emailRequired`, `emailAndCodeRequired`).

## Consequences
- No user enumeration is possible via the login page or API.
- The UX is more familiar and secure for real users.
- The codebase is cleaner, with no dead dropdown logic or unused endpoints.
- Documentation and tests are updated for the new flow.

## Alternatives Considered
- Keeping the dropdown for demo/testing: rejected due to security and UX concerns.
- Supporting both dropdown and email input: rejected for simplicity and maintainability.

## Migration Notes
- All references to user selection, dropdowns, and related i18n keys are removed.
- The `/api/auth/users` endpoint is deleted.
- Tests are updated to match the new i18n keys and logic.
