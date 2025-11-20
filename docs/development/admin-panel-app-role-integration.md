# Admin Panel Integration with App Role Authorization

This document explains how to integrate the Admin Panel into the main SignalR Chat application using Entra ID App Role-based authorization (`Admin.ReadWrite` role).

---

## Overview

The Admin Panel provides administrative capabilities for managing users and rooms in SignalR Chat. Access is controlled via the `Admin.ReadWrite` app role assigned in the Entra ID app registration.

**⚠️ Important Security Constraint**: Admin access is **restricted to home tenant users only**. External tenant users (multi-tenant SSO users) cannot be granted admin privileges, even if assigned the `Admin.ReadWrite` role.

### Key Concepts

- **App Role Authorization**: Users must be assigned the `Admin.ReadWrite` role in Entra ID to access admin features
- **Home Tenant Only**: Admin access is restricted to users from the home tenant (application owner's tenant)
- **Integrated Architecture**: Admin panel is part of the main Chat.Web application (not a separate deployment)
- **UI Visibility**: Only home tenant users with the `Admin.ReadWrite` role see the admin "cog" icon in the chat interface
- **Entra ID Only**: Admin panel requires Entra ID authentication - **no OTP fallback**
- **Claims-Based Access Control**: Authorization checks the `roles` claim in the ID token
- **Tenant Validation**: Token validation enforces home tenant ID check for admin role claims

---

## Architecture

### Authentication & Authorization Flow

```
Home tenant user with Admin.ReadWrite role
  → Signs in with Microsoft Entra ID
  → Token issued with claims:
      - preferred_username (UPN)
      - tid (Home Tenant ID)  ← Must match home tenant
      - roles: ["Admin.ReadWrite"]  ← App role claim
  → Application validates:
      ✓ Token signature and expiration
      ✓ User UPN exists in database
      ✓ Token contains "Admin.ReadWrite" role
      ✓ Tenant ID matches home tenant  ← Critical check
  → Admin access granted ✅
  → Cog icon visible in UI
```

```
External tenant user with Admin.ReadWrite role
  → Signs in with Microsoft Entra ID
  → Token issued with claims:
      - preferred_username (UPN)
      - tid (External Tenant ID)  ← NOT home tenant
      - roles: ["Admin.ReadWrite"]  ← Role assigned but will be ignored
  → Application validates:
      ✓ Token signature and expiration
      ✓ User UPN exists in database
      ✓ Token contains "Admin.ReadWrite" role
      ✗ Tenant ID does NOT match home tenant  ← Access denied
      → Admin role claim removed from principal
  → Chat access granted ✅
  → Admin access denied ❌
  → No cog icon visible
```

```
User without Admin.ReadWrite role
  → Signs in with Microsoft Entra ID
  → Token issued with claims:
      - preferred_username (UPN)
      - tid (Tenant ID)
      - roles: []  ← No admin role
  → Application validates:
      ✓ Token signature and expiration
      ✓ User UPN exists in database
      ✗ No "Admin.ReadWrite" role in token
  → Chat access granted ✅
  → Admin access denied ❌
  → No cog icon visible
```

### Integration Points

| Component | Purpose | Authorization |
|-----------|---------|--------------|
| **Admin Pages** | `/Admin/Users`, `/Admin/Rooms`, `/Admin/Users/AssignRooms` | Requires `Admin.ReadWrite` role **+ home tenant** |
| **Admin UI (Cog Icon)** | Navigation link in chat interface | Only visible if user has `Admin.ReadWrite` role **+ home tenant** |
| **Admin API Endpoints** | `/api/admin/*` (optional REST endpoints) | Requires `Admin.ReadWrite` role **+ home tenant** |
| **Chat Pages** | `/`, `/chat`, `/login` | No admin role required (any tenant) |

---

## Part 1: Entra ID App Registration Setup

### Step 1: Define App Role in App Registration

1. **Navigate to Azure Portal**
   ```
   https://portal.azure.com
   → Microsoft Entra ID
   → App registrations
   → [Your SignalR Chat App]
   → App roles
   ```

2. **Create Admin.ReadWrite Role**
   
   Click **Create app role** and configure:
   
   | Field | Value |
   |-------|-------|
   | **Display name** | `Admin.ReadWrite` |
   | **Allowed member types** | **Users/Groups** |
   | **Value** | `Admin.ReadWrite` |
   | **Description** | `Administrators who can manage users and rooms` |
   | **Do you want to enable this app role?** | ✅ Yes |
   
   Click **Apply**.

3. **Verify App Role Created**
   
   The role should appear in the App roles list:
   ```
   Display name: Admin.ReadWrite
   Value: Admin.ReadWrite
   Allowed member types: Users/Groups
   Enabled: Yes
   ```

### Step 2: Configure Token Claims (App Roles)

App roles are automatically included in ID tokens when a user is assigned to the role. No additional token configuration is required.

**Token claim structure**:
```json
{
  "roles": ["Admin.ReadWrite"],
  "preferred_username": "admin@contoso.com",
  "tid": "home-tenant-id-guid",
  ...
}
```

⚠️ **Important**: 
- If a user is **not** assigned to any app role, the `roles` claim will be **missing** or **empty** `[]`
- Multiple roles can be assigned: `"roles": ["Admin.ReadWrite", "Other.Role"]`
- Group membership **does not** automatically grant app roles
- **Admin access requires home tenant**: Even if external tenant user has `Admin.ReadWrite` role, access is denied if `tid` doesn't match home tenant ID

### Step 3: Assign Users to Admin.ReadWrite Role

App roles are assigned via **Enterprise Applications**, not App registrations.

**⚠️ CRITICAL**: Only assign users from the **home tenant** (application owner's tenant). External tenant users cannot be granted admin access.

#### Option A: Assign Individual Users

1. **Navigate to Enterprise Application**
   ```
   Azure Portal → Microsoft Entra ID
   → Enterprise applications
   → [Search: Your app name]
   → Users and groups
   ```

2. **Add User Assignment**
   - Click **Add user/group**
   - Select **Users**: Choose admin user(s) **from home tenant only**
   - Select **Select a role**: Choose `Admin.ReadWrite`
   - Click **Assign**

3. **Verify Assignment**
   
   User should appear in the list:
   ```
   Name: Alice Admin
   User Principal Name: alice@hometenant.com  ← Home tenant domain
   Role: Admin.ReadWrite
   ```
   
   ⚠️ **Do NOT assign external tenant users** (e.g., `bob@externaltenant.com`) to admin role

#### Option B: Assign Security Group (Recommended for Multiple Admins)

1. **Create Security Group in Entra ID** (if not exists)
   ```
   Azure Portal → Microsoft Entra ID
   → Groups → New group
   ```
   
   | Field | Value |
   |-------|-------|
   | **Group type** | Security |
   | **Group name** | `SignalR Chat Admins` |
   | **Group description** | `Administrators for SignalR Chat application` |
   | **Members** | Add admin users |

2. **Assign Group to App Role**
   ```
   Enterprise applications → [Your app]
   → Users and groups → Add user/group
   → Select Groups: SignalR Chat Admins
   → Select a role: Admin.ReadWrite
   → Assign
   ```

3. **Benefits of Group-Based Assignment**
   - Centralized admin management
   - Add/remove admins by managing group membership
   - No need to modify app configuration
   - Audit trail in Entra ID

---

## Part 2: Application Implementation

### Step 1: Update EntraId Configuration Options

Add app role validation to `EntraIdAuthorizationOptions`:

**src/Chat.Web/Options/EntraIdOptions.cs** - Add to `EntraIdAuthorizationOptions`:

```csharp
public class EntraIdAuthorizationOptions
{
    /// <summary>
    /// List of allowed tenant domains or tenant IDs for regular user access. Empty list = allow any tenant.
    /// </summary>
    public List<string> AllowedTenants { get; set; } = new();

    /// <summary>
    /// Require user's tenant to be in AllowedTenants list. Default: true.
    /// </summary>
    public bool RequireTenantValidation { get; set; } = true;
    
    /// <summary>
    /// Home tenant ID (GUID). Used to restrict admin access to home tenant users only.
    /// Admin role will be ignored for users from external tenants.
    /// </summary>
    public string HomeTenantId { get; set; } = string.Empty;
    
    /// <summary>
    /// App role value required for admin panel access. Default: "Admin.ReadWrite".
    /// Users with this role can access /Admin/* pages and see the admin cog icon.
    /// IMPORTANT: Admin access is restricted to home tenant users only.
    /// </summary>
    public string AdminRoleValue { get; set; } = "Admin.ReadWrite";
}
```

### Step 2: Add Authorization Policy for Admin Role

**src/Chat.Web/Startup.cs** - In `ConfigureServices`, after authentication setup:

```csharp
// Authorization policies
services.AddAuthorization(options =>
{
    // Default policy: authenticated user with UPN in database
    options.DefaultPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
    
    // Admin policy: requires Admin.ReadWrite app role + home tenant
    options.AddPolicy("RequireAdminRole", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireRole("Admin.ReadWrite"); // Checks roles claim in token
        // Note: Tenant validation happens in OnTokenValidated event
        // External tenant users will have admin role claim removed
    });
});
```

### Step 3: Create Admin Pages

Create admin pages under `src/Chat.Web/Pages/Admin/`:

#### `/Admin/Index.cshtml` - Admin Dashboard

```csharp
@page
@model Chat.Web.Pages.Admin.IndexModel
@{
    ViewData["Title"] = "Admin Panel";
}

<div class="container mt-4">
    <h1>Admin Panel</h1>
    <p>Welcome, <strong>@User.Identity?.Name</strong></p>
    
    <div class="row mt-4">
        <div class="col-md-6">
            <div class="card">
                <div class="card-body">
                    <h5 class="card-title">User Management</h5>
                    <p class="card-text">Create, edit, and manage chat users.</p>
                    <a href="/Admin/Users" class="btn btn-primary">Manage Users</a>
                </div>
            </div>
        </div>
        <div class="col-md-6">
            <div class="card">
                <div class="card-body">
                    <h5 class="card-title">Room Management</h5>
                    <p class="card-text">Create rooms and assign users.</p>
                    <a href="/Admin/Rooms" class="btn btn-primary">Manage Rooms</a>
                </div>
            </div>
        </div>
    </div>
</div>
```

**Index.cshtml.cs**:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Chat.Web.Pages.Admin
{
    [Authorize(Policy = "RequireAdminRole")]
    public class IndexModel : PageModel
    {
        public void OnGet()
        {
        }
    }
}
```

#### `/Admin/Users/Index.cshtml` - User Management

```csharp
@page
@model Chat.Web.Pages.Admin.Users.IndexModel
@{
    ViewData["Title"] = "Manage Users";
}

<div class="container mt-4">
    <h1>Manage Users</h1>
    <a href="/Admin/Users/Create" class="btn btn-success mb-3">Create User</a>
    
    <table class="table table-striped">
        <thead>
            <tr>
                <th>Username</th>
                <th>Full Name</th>
                <th>Email</th>
                <th>UPN</th>
                <th>Enabled</th>
                <th>Actions</th>
            </tr>
        </thead>
        <tbody>
            @foreach (var user in Model.Users)
            {
                <tr>
                    <td>@user.UserName</td>
                    <td>@user.FullName</td>
                    <td>@user.Email</td>
                    <td>@user.Upn</td>
                    <td>@(user.Enabled ? "✅" : "❌")</td>
                    <td>
                        <a href="/Admin/Users/Edit?username=@user.UserName" class="btn btn-sm btn-primary">Edit</a>
                        <a href="/Admin/Users/AssignRooms?username=@user.UserName" class="btn btn-sm btn-secondary">Assign Rooms</a>
                    </td>
                </tr>
            }
        </tbody>
    </table>
</div>
```

**Users/Index.cshtml.cs**:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Chat.Web.Repositories;

namespace Chat.Web.Pages.Admin.Users
{
    [Authorize(Policy = "RequireAdminRole")]
    public class IndexModel : PageModel
    {
        private readonly IUsersRepository _usersRepo;

        public IndexModel(IUsersRepository usersRepo)
        {
            _usersRepo = usersRepo;
        }

        public IEnumerable<ApplicationUser> Users { get; set; } = Enumerable.Empty<ApplicationUser>();

        public void OnGet()
        {
            Users = _usersRepo.GetAll();
        }
    }
}
```

### Step 4: Add Admin Navigation (Cog Icon)

Update the main chat layout to show the admin cog icon only for users with `Admin.ReadWrite` role.

**src/Chat.Web/Pages/Shared/_Layout.cshtml** (or main chat layout):

```html
@* Add to navigation bar *@
<nav class="navbar navbar-expand-lg navbar-light bg-light">
    <div class="container-fluid">
        <a class="navbar-brand" href="/">SignalR Chat</a>
        
        @if (User.Identity?.IsAuthenticated == true)
        {
            <ul class="navbar-nav ms-auto">
                @* Show cog icon only for users with Admin.ReadWrite role *@
                @if (User.IsInRole("Admin.ReadWrite"))
                {
                    <li class="nav-item">
                        <a class="nav-link" href="/Admin" title="Admin Panel">
                            <i class="bi bi-gear-fill"></i> Admin
                        </a>
                    </li>
                }
                
                <li class="nav-item">
                    <span class="nav-link">Hello, @User.Identity.Name</span>
                </li>
                <li class="nav-item">
                    <a class="nav-link" href="/logout">Sign Out</a>
                </li>
            </ul>
        }
    </div>
</nav>
```

**Alternative: JavaScript-based icon rendering** (if using client-side rendering):

```javascript
// Check if user has admin role from server-rendered data attribute
const userRoles = document.body.dataset.userRoles?.split(',') || [];
const isAdmin = userRoles.includes('Admin.ReadWrite');

if (isAdmin) {
    // Show admin cog icon
    document.getElementById('admin-cog').style.display = 'block';
}
```

### Step 5: Add Helper Method for Role Checking

Create a helper extension method for checking admin role:

**src/Chat.Web/Utilities/ClaimsPrincipalExtensions.cs**:

```csharp
using System.Security.Claims;

namespace Chat.Web.Utilities
{
    public static class ClaimsPrincipalExtensions
    {
        /// <summary>
        /// Checks if the user has the Admin.ReadWrite app role.
        /// </summary>
        public static bool IsAdmin(this ClaimsPrincipal user)
        {
            return user?.IsInRole("Admin.ReadWrite") == true;
        }
        
        /// <summary>
        /// Gets all roles assigned to the user.
        /// </summary>
        public static IEnumerable<string> GetRoles(this ClaimsPrincipal user)
        {
            return user?.FindAll(ClaimTypes.Role).Select(c => c.Value) ?? Enumerable.Empty<string>();
        }
    }
}
```

Usage in Razor Pages:

```csharp
@using Chat.Web.Utilities

@if (User.IsAdmin())
{
    <a href="/Admin">Admin Panel</a>
}
```

### Step 6: Protect Admin API Endpoints (Optional)

If you add REST API endpoints for admin operations:

**src/Chat.Web/Controllers/AdminController.cs**:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Chat.Web.Repositories;

namespace Chat.Web.Controllers
{
    [Authorize(Policy = "RequireAdminRole")]
    [Route("api/admin")]
    [ApiController]
    public class AdminController : ControllerBase
    {
        private readonly IUsersRepository _usersRepo;

        public AdminController(IUsersRepository usersRepo)
        {
            _usersRepo = usersRepo;
        }

        [HttpGet("users")]
        public IActionResult GetUsers()
        {
            var users = _usersRepo.GetAll();
            return Ok(users);
        }

        [HttpPost("users")]
        public IActionResult CreateUser([FromBody] CreateUserRequest request)
        {
            // Validate and create user
            var user = new ApplicationUser
            {
                UserName = request.UserName,
                Email = request.Email,
                FullName = request.FullName,
                Upn = request.Upn,
                Enabled = true
            };
            
            _usersRepo.Upsert(user);
            return Ok(new { message = "User created successfully" });
        }
    }
    
    public record CreateUserRequest(string UserName, string Email, string FullName, string Upn);
}
```

---

## Part 3: Configuration & Deployment

### Configuration Structure

```json
{
  "EntraId": {
    "Instance": "https://login.microsoftonline.com/",
    "TenantId": "organizations",  // Multi-tenant for regular users
    "ClientId": "12345678-1234-1234-1234-123456789abc",
    "ClientSecret": "${ENTRA_CLIENT_SECRET}",
    "CallbackPath": "/signin-oidc",
    "SignedOutCallbackPath": "/signout-oidc",
    
    "Authorization": {
      "AllowedTenants": [
        "contoso.onmicrosoft.com"
      ],
      "RequireTenantValidation": true,
      "HomeTenantId": "87654321-4321-4321-4321-cba987654321",  // Home tenant ID for admin access
      "AdminRoleValue": "Admin.ReadWrite"
    },
    
    "Fallback": {
      "EnableOtp": true,
      "OtpForUnauthorizedUsers": false
    }
  }
}
```

### Azure App Service Configuration

```bash
az webapp config appsettings set \
  --name signalrchat-prod-plc \
  --resource-group rg-signalrchat-prod-plc \
  --settings \
    "EntraId__ClientId=12345678-1234-1234-1234-123456789abc" \
    "EntraId__TenantId=organizations" \
    "EntraId__Authorization__AdminRoleValue=Admin.ReadWrite" \
    "EntraId__Authorization__RequireTenantValidation=true" \
    "EntraId__Fallback__EnableOtp=true"
```

### Required Redirect URIs

Add admin panel redirect URIs to app registration:

| Environment | Redirect URI |
|-------------|-------------|
| Development | `https://localhost:5099/signin-oidc` |
| Staging | `https://signalrchat-staging-plc.azurewebsites.net/signin-oidc` |
| Production | `https://signalrchat-prod-plc.azurewebsites.net/signin-oidc` |

**Note**: Admin panel uses the same redirect URI as the main app (integrated deployment).

---

## Token Claims Reference

### ID Token with Admin.ReadWrite Role

```json
{
  "aud": "12345678-1234-1234-1234-123456789abc",
  "iss": "https://login.microsoftonline.com/{tenant-id}/v2.0",
  "iat": 1700000000,
  "exp": 1700003600,
  "name": "Alice Admin",
  "preferred_username": "alice@contoso.com",
  "email": "alice@contoso.com",
  "oid": "user-object-id-guid",
  "tid": "tenant-id-guid",
  "roles": [
    "Admin.ReadWrite"
  ],
  "ver": "2.0"
}
```

### ID Token without Admin Role (Regular User)

```json
{
  "aud": "12345678-1234-1234-1234-123456789abc",
  "iss": "https://login.microsoftonline.com/{tenant-id}/v2.0",
  "iat": 1700000000,
  "exp": 1700003600,
  "name": "Bob User",
  "preferred_username": "bob@contoso.com",
  "email": "bob@contoso.com",
  "oid": "user-object-id-guid",
  "tid": "tenant-id-guid",
  "ver": "2.0"
}
```

**Note**: `roles` claim is **missing** or empty for users without app role assignments.

### Key Claims for Authorization

| Claim | Description | Validation |
|-------|-------------|-----------|
| `roles` | App roles assigned to user | Must contain `"Admin.ReadWrite"` for admin access |
| `preferred_username` | User Principal Name (UPN) | Must exist in application database |
| `tid` | Tenant ID | Optional: Can be in `AllowedTenants` list |

---

## Security Considerations

### App Role vs Group Authorization

| Aspect | App Roles (✅ Recommended) | Group Claims |
|--------|---------------------------|--------------|
| **Management** | Managed in Enterprise Applications | Managed in Entra ID Groups |
| **Token Size** | Minimal (only role names) | Can cause token bloat with many groups |
| **Overage Handling** | No overage issues | Requires Graph API calls for large groups |
| **Scope** | Application-specific | Organization-wide |
| **Assignment** | Users/Groups → App Role | Users → Group → App needs group claim |
| **Multi-Tenant** | Can enforce home tenant restriction | Difficult to restrict by tenant |
| **Best For** | Application-level permissions (admins) | Cross-app organizational roles |

### Why App Roles for Admin Panel?

1. **✅ Token Simplicity**: `roles` claim contains only assigned roles (e.g., `["Admin.ReadWrite"]`)
2. **✅ No Overage**: App roles never trigger group overage scenarios
3. **✅ Application-Scoped**: Permissions specific to SignalR Chat application
4. **✅ Declarative**: Role defined in app registration manifest
5. **✅ Auditable**: Role assignments visible in Enterprise Applications
6. **✅ No Additional Permissions**: Doesn't require `GroupMember.Read.All` API permission

### Admin Role Security

- **Principle of Least Privilege**: Only assign `Admin.ReadWrite` to trusted administrators
- **Home Tenant Only**: Admin access restricted to home tenant users (application owner's tenant)
- **Regular Audits**: Periodically review app role assignments in Enterprise Applications
- **Logging**: Log all admin actions (user creation, room management, etc.)
- **No OTP Fallback**: Admin panel **requires** Entra ID authentication (no OTP bypass)
- **UI Hiding**: Regular users have **no indication** that admin features exist (no UI hints)
- **Tenant Validation**: Token validation removes admin role claim from external tenant users

### Defense in Depth

1. **Token Validation**: Verify `roles` claim contains `Admin.ReadWrite`
2. **Tenant Validation**: Verify token `tid` claim matches home tenant ID
3. **Role Claim Removal**: Remove admin role from external tenant users in token validation
4. **Authorization Policy**: `[Authorize(Policy = "RequireAdminRole")]` on all admin pages
5. **UI Access Control**: Hide admin cog icon for non-admin users
6. **API Protection**: Apply `[Authorize(Policy = "RequireAdminRole")]` to admin API endpoints
7. **Logging & Monitoring**: Track admin access and actions, log tenant validation failures

---

## Testing

### Unit Tests: Authorization Policy

```csharp
[Fact]
public void RequireAdminRole_Policy_RequiresRoleClaim()
{
    var options = new AuthorizationOptions();
    options.AddPolicy("RequireAdminRole", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireRole("Admin.ReadWrite");
    });

    var policy = options.GetPolicy("RequireAdminRole");
    
    Assert.NotNull(policy);
    Assert.Contains(policy.Requirements, r => r is RolesAuthorizationRequirement);
}
```

### Integration Tests: Admin Access

```csharp
[Fact]
public async Task Admin_Index_WithoutAdminRole_ReturnsForbidden()
{
    var client = _factory.CreateClient();
    
    // Authenticate as regular user (no Admin.ReadWrite role)
    await AuthenticateAsRegularUser(client);
    
    var response = await client.GetAsync("/Admin");
    
    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
}

[Fact]
public async Task Admin_Index_WithAdminRole_ReturnsSuccess()
{
    var client = _factory.CreateClient();
    
    // Authenticate as admin user (with Admin.ReadWrite role)
    await AuthenticateAsAdmin(client);
    
    var response = await client.GetAsync("/Admin");
    
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
}
```

### Manual Testing

1. **Test as Admin User**:
   - Assign `Admin.ReadWrite` role to test user in Enterprise Applications
   - Sign in to application
   - Verify cog icon visible in navigation
   - Navigate to `/Admin` → should show admin dashboard
   - Create test user/room → should succeed

2. **Test as Regular User**:
   - Sign in without `Admin.ReadWrite` role assignment
   - Verify NO cog icon in navigation
   - Directly navigate to `/Admin` → should return 403 Forbidden
   - Confirm no admin features visible anywhere in UI

---

## Troubleshooting

### Issue: "Access Denied" when accessing /Admin

**Cause**: User does not have `Admin.ReadWrite` app role assigned.

**Solution**:
1. Navigate to Enterprise Applications → [Your app] → Users and groups
2. Find user in list
3. Check **Role** column - should show `Admin.ReadWrite`
4. If role is missing:
   - Click **Add user/group**
   - Select user
   - Select role: `Admin.ReadWrite`
   - Click **Assign**
5. Have user sign out and sign in again to get fresh token with role

### Issue: roles claim is missing from token

**Cause**: App role not configured or not assigned.

**Solution**:
1. **Verify app role exists**:
   - App registration → App roles
   - Confirm `Admin.ReadWrite` role is enabled
   
2. **Verify role assignment**:
   - Enterprise Applications → [Your app] → Users and groups
   - Confirm user/group is assigned to `Admin.ReadWrite` role

3. **Check token**:
   - Use https://jwt.ms to decode ID token
   - Look for `roles` claim: should contain `["Admin.ReadWrite"]`

### Issue: User.IsInRole("Admin.ReadWrite") returns false

**Cause**: Role claim not being read correctly or user not assigned role.

**Solution**:
1. **Verify claim mapping**:
   ```csharp
   // In Startup.cs OnTokenValidated event
   var roles = context.Principal?.FindAll(ClaimTypes.Role)
       .Select(c => c.Value)
       .ToList();
   logger.LogInformation("User roles: {Roles}", string.Join(", ", roles));
   ```

2. **Check claim type**:
   - Entra ID emits role claims as `http://schemas.microsoft.com/ws/2008/06/identity/claims/role`
   - ASP.NET Core maps this to `ClaimTypes.Role` automatically
   - Verify mapping is working: `User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role)`

3. **Fresh token**:
   - Sign out and sign in again to get new token with updated role assignments

### Issue: Cog icon visible but /Admin returns 403

**Cause**: UI check uses different logic than authorization policy.

**Solution**:
- Ensure UI visibility check matches authorization policy:
  ```csharp
  // Both should use same logic
  @if (User.IsInRole("Admin.ReadWrite")) { /* show icon */ }
  
  [Authorize(Policy = "RequireAdminRole")] // Uses same role check
  ```

---

## Migration from Group-Based to App Role-Based

If migrating from the existing admin branch (which uses group-based authorization):

### Step 1: Update Configuration

**Old (Group-Based)**:
```json
"Authorization": {
  "ChatAdminGroupObjectId": "62dc8cc5-fd83-45ba-b40f-64449d26e0de"
}
```

**New (App Role-Based)**:
```json
"EntraId": {
  "Authorization": {
    "AdminRoleValue": "Admin.ReadWrite"
  }
}
```

### Step 2: Update Authorization Policy

**Old (Group-Based)**:
```csharp
services.AddAuthorization(options =>
{
    var policy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .RequireClaim("groups", chatAdminGroupId)
        .Build();
    options.DefaultPolicy = policy;
});
```

**New (App Role-Based)**:
```csharp
services.AddAuthorization(options =>
{
    options.AddPolicy("RequireAdminRole", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireRole("Admin.ReadWrite");
    });
});
```

### Step 3: Migrate Admin Assignments

1. **Export current group members**:
   ```powershell
   # Get members of ChatAdmin group
   Get-AzureADGroupMember -ObjectId "62dc8cc5-fd83-45ba-b40f-64449d26e0de"
   ```

2. **Assign to app role**:
   - Option A: Assign individual users to `Admin.ReadWrite` role
   - Option B: Assign entire group to `Admin.ReadWrite` role (recommended)

3. **Verify migration**:
   - Each admin user should have `Admin.ReadWrite` in their token
   - Test admin access for all migrated users

### Step 4: Remove Group Claims Configuration

**Old**: App registration → Token configuration → Group claims → Remove

**New**: App roles automatically included in token (no configuration needed)

---

## References

- [Microsoft Entra ID App Roles](https://learn.microsoft.com/entra/identity-platform/howto-add-app-roles-in-apps)
- [App Roles vs Groups](https://learn.microsoft.com/entra/identity-platform/application-model#app-roles)
- [Role-Based Authorization in ASP.NET Core](https://learn.microsoft.com/aspnet/core/security/authorization/roles)
- [Claims-Based Authorization](https://learn.microsoft.com/aspnet/core/security/authorization/claims)
- [ID Token Claims Reference](https://learn.microsoft.com/entra/identity-platform/id-token-claims-reference#roles)

---

### Summary

### Admin User Setup (Per User)
1. ☐ Create `Admin.ReadWrite` app role in app registration
2. ☐ Assign **home tenant user** to `Admin.ReadWrite` role in Enterprise Applications
3. ☐ **Do NOT assign external tenant users** (they will be denied even with role)
4. ☐ User signs out and signs in to get fresh token
5. ☐ Verify `roles` claim contains `["Admin.ReadWrite"]` AND `tid` matches home tenant ID
6. ☐ Verify cog icon visible in chat UI
7. ☐ Verify `/Admin` page accessible

### Application Setup (One-Time)
1. ☐ Define `Admin.ReadWrite` app role in app registration
2. ☐ Update `EntraIdAuthorizationOptions` with `AdminRoleValue` and `HomeTenantId` properties
3. ☐ Add tenant validation in `OnTokenValidated` event to remove admin role from external tenant users
4. ☐ Add `RequireAdminRole` authorization policy in `Startup.cs`
5. ☐ Create admin pages under `/Admin/*` with `[Authorize(Policy = "RequireAdminRole")]`
6. ☐ Add conditional cog icon rendering in main layout (only if `User.IsInRole("Admin.ReadWrite")`)
7. ☐ Deploy and test with home tenant admin, external tenant user, and non-admin users

**Result**: Home tenant users with `Admin.ReadWrite` role can access admin features and see the cog icon. External tenant users and regular users have no indication that admin features exist.
