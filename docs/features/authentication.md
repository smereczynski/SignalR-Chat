## 1. Threat Model & Goals

What we're protecting against:
- Offline disclosure if a Redis dump / memory snapshot is exfiltrated.
- Log leakage (accidental plaintext OTP logging).
- Insider read access to Redis contents.

What we cannot fully prevent:
- Online brute force via the verification API unless rate limiting is enforced.
- Exhaustive offline brute force of a 6‑digit code (≈ 1,000,000 possibilities) if the hash function is very fast and unkeyed.

Therefore:
1. ✅ **IMPLEMENTED**: Eliminate plaintext storage (hash before storing) - Argon2id with pepper + salt
2. ✅ **IMPLEMENTED**: Add a secret "pepper" (server secret not stored in Redis) to raise the bar for an offline attacker
3. ✅ **IMPLEMENTED**: Use memory‑hard KDF (Argon2id) to materially slow offline guessing
4. ✅ **IMPLEMENTED**: Enforce online rate limiting (OTP attempt rate limiting in AuthController)

---

## 2. Implementation Status

### ✅ COMPLETED (v0.9.4)

**Argon2id OTP Hashing** (Issue #26 - Closed)
- **Implementation**: `Argon2OtpHasher` using Isopoh.Cryptography.Argon2
- **Format**: `OtpHash:v2:argon2id:m=65536,t=4,p=4:{saltB64}:{hashB64}`
- **Parameters**:
  - Memory: 64 MB (65536 KB)
  - Iterations: 4
  - Parallelism: 4 threads
  - Output: 32 bytes
- **Pepper**: Environment variable `Otp__Pepper` (Base64, 32+ bytes)
- **Salt**: Random 16 bytes per OTP
- **Verification**: Constant-time comparison via Argon2.Verify()
- **Fallback**: Supports legacy plaintext OTP verification (when `Otp__AllowPlaintext=true`)

**OTP Attempt Rate Limiting** (Issue #26 - Closed)
- **Implementation**: Redis-backed counter `otp_attempts:{user}`
- **Threshold**: Configurable via `Otp__MaxAttempts` (default: 5)
- **TTL**: Synchronized with OTP lifetime (300 seconds)
- **Metrics**: `chat.otp.verifications.ratelimited` tracks blocked attempts
- **Logging**: Structured logging with sanitized usernames
- **Fail-open**: Safe fallback on Redis errors

**Cryptographically Secure RNG** (Issue #62 - Open, but needs verification)
- ⚠️ **STATUS**: Needs verification - check if `RandomNumberGenerator.GetInt32()` is used instead of `new Random()`
- **Expected**: `RandomNumberGenerator.GetInt32(100000, 1000000)` for OTP generation
- **Risk**: If still using `new Random()`, OTP codes are predictable

---

## 3. Security Features Matrix

| Feature | Status | Location | Notes |
|---------|--------|----------|-------|
| Argon2id Hashing | ✅ Implemented | `Argon2OtpHasher.cs` | v2 format with pepper + salt |
| Pepper Management | ✅ Implemented | `OtpOptions` | Loaded from `Otp__Pepper` env var |
| Random Salt | ✅ Implemented | `Argon2OtpHasher.Hash()` | 16 bytes per OTP |
| Constant-Time Compare | ✅ Implemented | `Argon2.Verify()` | Built into Isopoh library |
| Rate Limiting | ✅ Implemented | `AuthController.Verify()` | Per-user attempt counter |
| Attempt Lockout | ✅ Implemented | Redis counter + TTL | 5 attempts / 5 minutes |
| Secure RNG | ⚠️ **VERIFY** | `AuthController.Start()` | **Issue #62** - Check implementation |
| Logging Hygiene | ✅ Implemented | All auth code | No OTP/codes logged |
| Structured Logging | ⚠️ Partial | AuthController | **Issue #34** - Some string interpolation remains |

---

## 4. Recommended Approach (IMPLEMENTED)

✅ **Current Implementation: Option B (Argon2id)**

**Completed**:

| Concept | Purpose |
|---------|---------|
| Salt | Per-code random value to prevent identical OTPs hashing to same stored value. |
| Pepper | Server‑held secret (env var) not stored with the hash; makes offline cracking require pepper disclosure. |
| Version Tag | Supports future parameter rotation (e.g., `v1`, `v2`). |
| Constant-Time Compare | Avoid subtle timing differences on verification (`CryptographicOperations.FixedTimeEquals`). |
| TTL Retention | Keep only until expiry; never “extend” on failed attempts to avoid adversary probing timing. |

---

## 3. Strategy Options

### Option A: HMAC-SHA256 (Peppered, Fast)
Store: `OtpHash:v1:{saltBase64}:{hmacBase64}`
- Compute: `HMAC_SHA256(pepper, userName || ':' || salt || ':' || code)`
- Pros: Very fast, negligible CPU cost.
- Cons: If Redis dump + pepper leaked, offline brute force trivial (1M trials).
- Suitable when: Strong online throttling + very high OTP request rate (needs low overhead).

### Option B: Argon2id (Salt + Pepper) Moderate Parameters
Store: `OtpHash:v2:argon2id:{params}:{saltBase64}:{hashBase64}`
- Compute: Argon2id over `pepper || userName || salt || code`.
- Suggested starting parameters (tune to latency budget):
  - Memory: 32–64 MB
  - Iterations: 2–3
  - Parallelism: 1–2
  - Output length: 32 bytes
- Pros: Greatly slows offline brute force (turns 1M search into meaningfully expensive task).
- Cons: Higher CPU + memory; need to benchmark to avoid latency spikes under load.

### Option C: Two-Level (Fast Filter + Slow KDF)
- Store both a fast HMAC and (only if needed) a slower KDF; on verify, first check HMAC, then confirm with Argon2.
- Overkill for a simple chat OTP flow; usually not needed unless scale is massive and security bar is high.

### Option D: BCrypt / PBKDF2 (Salt + Pepper)
- Acceptable but offers less resistance to GPU cracking than Argon2id/scrypt.
- Use only if Argon2id library adoption is a problem.

---

## 4. Recommended Approach (Balanced)

Adopt **Option B (Argon2id)** plus:
- Global pepper: environment variable `Otp__Pepper` (not checked into any config).
- Random 16‑byte salt per OTP.
- Version tag `v2` (reserve `v1` if you ever stored plaintext or HMAC previously).
- TTL unchanged (still the authoritative expiration gate).
- Implement a fallback pathway for future rotation (`IOtpHasher` interface with `Hash()` and `Verify()` returning a `VerificationResult` that includes `IsMatch` and `NeedsRehash`).

If performance testing shows Argon2id cost is too high for peak OTP throughput, downgrade to Option A (peppered HMAC) but **tighten rate limiting** (e.g., 5 attempts per user per 5 minutes, exponential backoff).

---

## 5. Data Format

Example stored Redis value (string):
```
OtpHash:v2:argon2id:m=65536,t=3,p=1:BASE64URL(salt):BASE64URL(hash)
```
- `m` = memory KB
- `t` = iterations
- `p` = parallelism
- Use URL-safe Base64 (or standard Base64) consistently.
- Keep prefix to quickly detect wrong format or future migration needs.

---

## 6. Verification Flow

1. Fetch stored value.
2. Parse & validate structure.
3. Derive hash with extracted salt and parameters using current pepper.
4. Constant-time compare.
5. If mismatch → generic “invalid code” (never disclose expiry vs wrong).
6. On success → remove key immediately (mitigate replay).
7. If algorithm/params outdated (e.g., you raise `m` next month) → optionally reissue code rather than rehash (simpler for OTP since it’s short-lived).

---

## 7. Rate Limiting & Lockout (Critical)

Because OTP entropy is low:
- Per-user attempt counter in Redis: `INCR otp_attempts:{user}` with same TTL as code; if >N (e.g., 5) then block verification until TTL expires or enforce backoff.
- Optionally track IP dimension: `otp_attempts_ip:{ip}`.
- Return generic failure while logging categories (never “too many attempts” to avoid signal disparity if you prefer stealth; or do explicit UI messaging if UX > secrecy).

---

## 8. Pepper Management

### 8.1 Storage Options

The pepper is loaded via environment variable `Otp__Pepper` with the following priority:

**Priority Order:**
1. **Environment variable** `Otp__Pepper` (highest priority) ✅ **RECOMMENDED**
2. Configuration section `"Otp:Pepper"` in appsettings.json (fallback)
3. Empty/null (falls back to empty byte array - **INSECURE**, do not use in production)

**Implementation (Startup.cs):**
```csharp
services.Configure<OtpOptions>(Configuration.GetSection("Otp"));
services.PostConfigure<OtpOptions>(opts =>
{
    var envPepper = Environment.GetEnvironmentVariable("Otp__Pepper");
    if (!string.IsNullOrWhiteSpace(envPepper)) opts.Pepper = envPepper;
});
```

### 8.2 Storage Locations by Environment

#### Local Development
Store in `.env.local` (loaded by VS Code task "Run Chat (Azure local env)"):
```bash
# .env.local (NOT committed to source control)
Otp__Pepper=gT8x9vP2mK5nL7wQ1rY4uE6sA3bV0cH9dJ2fZ8iM1oT=
```

**Important:** Add `.env.local` to `.gitignore` to prevent accidental commits.

#### Azure App Service (Basic)
Set in **Configuration → Application Settings** in Azure Portal:
```
Name:  Otp__Pepper
Value: gT8x9vP2mK5nL7wQ1rY4uE6sA3bV0cH9dJ2fZ8iM1oT=
```

**Pros:** Simple, no additional Azure resources needed
**Cons:** Visible to anyone with App Service access, stored in plain text in ARM

#### Azure Key Vault (Production Recommended) ✅
Store pepper in Key Vault and reference via App Service configuration:

1. **Create Key Vault secret:**
   ```bash
   az keyvault secret set \
     --vault-name "your-vault-name" \
     --name "OtpPepper" \
     --value "gT8x9vP2mK5nL7wQ1rY4uE6sA3bV0cH9dJ2fZ8iM1oT="
   ```

2. **Configure App Service to reference it:**
   ```
   Name:  Otp__Pepper
   Value: @Microsoft.KeyVault(SecretUri=https://your-vault.vault.azure.net/secrets/OtpPepper/)
   ```

3. **Grant App Service access:**
   ```bash
   # Enable managed identity on App Service first
   az webapp identity assign --name your-app --resource-group your-rg
   
   # Grant Key Vault access
   az keyvault set-policy \
     --name your-vault-name \
     --object-id <managed-identity-object-id> \
     --secret-permissions get list
   ```

**Pros:**
- Centralized secret management
- Audit trail for secret access
- Automatic rotation support
- Role-based access control (RBAC)
- Secrets never visible in portal after creation

**Cons:** Requires additional Azure resource (Key Vault)

### 8.3 Generating a Secure Pepper

**Generate 32 random bytes as Base64:**
```bash
# Using OpenSSL (macOS/Linux)
openssl rand -base64 32

# Using PowerShell (Windows)
[Convert]::ToBase64String((1..32 | ForEach-Object { Get-Random -Minimum 0 -Maximum 256 }))

# Using .NET (any platform)
dotnet run --project tools/GeneratePepper/
```

**Expected output format:**
```
gT8x9vP2mK5nL7wQ1rY4uE6sA3bV0cH9dJ2fZ8iM1oT=
```

**Requirements:**
- Minimum 32 bytes (256 bits) of entropy
- Base64-encoded (no line breaks)
- Generated using cryptographically secure RNG
- Never derived from user input or predictable values

### 8.4 Pepper Rotation Strategy

**When to rotate:**
- Suspected compromise (Redis dump leaked + pepper exposed)
- Scheduled rotation policy (e.g., annually)
- Security incident response
- Key/secret rotation compliance requirements

**Rotation process (Zero-downtime):**

1. **Generate new pepper:**
   ```bash
   NEW_PEPPER=$(openssl rand -base64 32)
   echo "New pepper: $NEW_PEPPER"
   ```

2. **Introduce `v3` hasher with new pepper:**
   ```csharp
   // OtpOptions.cs - add support for versioned peppers
   public string PepperV2 { get; set; } // Current production pepper
   public string PepperV3 { get; set; } // New pepper for rotation
   
   // Argon2OtpHasher.cs - detect version and use appropriate pepper
   public string Hash(string userName, string code)
   {
       var pepper = _options.PepperV3 ?? _options.Pepper; // Use v3 if available
       // ... hash with new pepper
       return $"OtpHash:v3:argon2id:..."; // Update version tag
   }
   
   public VerificationResult Verify(string userName, string code, string stored)
   {
       if (stored.StartsWith("OtpHash:v3:"))
           return VerifyWithPepper(_options.PepperV3, userName, code, stored);
       else if (stored.StartsWith("OtpHash:v2:"))
           return VerifyWithPepper(_options.Pepper, userName, code, stored);
       // ... legacy handlers
   }
   ```

3. **Deploy with both peppers available:**
   ```bash
   # Azure Key Vault
   az keyvault secret set --vault-name your-vault --name "OtpPepperV2" --value "$OLD_PEPPER"
   az keyvault secret set --vault-name your-vault --name "OtpPepperV3" --value "$NEW_PEPPER"
   
   # App Service configuration
   Otp__Pepper=@Microsoft.KeyVault(SecretUri=.../OtpPepperV2/)
   Otp__PepperV3=@Microsoft.KeyVault(SecretUri=.../OtpPepperV3/)
   ```

4. **Monitor for natural expiration:**
   - All `v2` OTPs expire within 5 minutes (OTP TTL)
   - No migration needed - OTPs are ephemeral
   - New OTP requests automatically use `v3` pepper

5. **After 10 minutes, remove old pepper:**
   ```bash
   # Remove v2 pepper from configuration
   az webapp config appsettings delete --name your-app --setting-names Otp__Pepper
   
   # Rename v3 to primary
   Otp__Pepper=@Microsoft.KeyVault(SecretUri=.../OtpPepperV3/)
   ```

**Emergency rotation (Breaking glass):**

If pepper is compromised and immediate action required:

```bash
# 1. Generate and deploy new pepper immediately
NEW_PEPPER=$(openssl rand -base64 32)
az keyvault secret set --vault-name your-vault --name "OtpPepper" --value "$NEW_PEPPER"

# 2. Restart app service to reload configuration
az webapp restart --name your-app --resource-group your-rg

# 3. All existing OTPs become unverifiable (expected)
# 4. Users must request new OTP (with new pepper)
# 5. Optional: Revoke all active sessions (see GUIDE-Session-handling.md)
```

**Impact:**
- All in-flight OTPs (≤5 minutes old) become invalid
- Users currently entering OTP codes will fail verification
- Users must request new OTP
- Active sessions remain valid (cookies unaffected)

### 8.5 Security Best Practices

- ✅ **DO** use Azure Key Vault in production
- ✅ **DO** generate pepper with cryptographic RNG (≥32 bytes)
- ✅ **DO** restrict Key Vault access to minimum necessary principals
- ✅ **DO** enable Azure Key Vault audit logging
- ✅ **DO** test pepper rotation in staging environment first
- ❌ **DON'T** commit pepper to source control (any environment)
- ❌ **DON'T** log pepper or concatenated preimage values
- ❌ **DON'T** reuse pepper across environments (dev/staging/prod)
- ❌ **DON'T** derive pepper from predictable sources
- ❌ **DON'T** share pepper via email, Slack, or other insecure channels

### 8.6 Verification

**Check if pepper is configured:**
```bash
# Local development
grep Otp__Pepper .env.local 2>/dev/null || echo "⚠️  NOT CONFIGURED"

# Azure App Service (requires Azure CLI + permissions)
az webapp config appsettings list \
  --name your-app \
  --resource-group your-rg \
  --query "[?name=='Otp__Pepper'].value" -o tsv

# Expected output (Key Vault reference):
# @Microsoft.KeyVault(SecretUri=https://...)
```

**Test pepper loading at runtime:**
```csharp
// Add temporary diagnostic endpoint (remove after verification)
[Authorize(Roles = "Admin")]
[HttpGet("api/auth/pepper-status")]
public IActionResult PepperStatus()
{
    var pepperConfigured = !string.IsNullOrWhiteSpace(_otpOptions.Value?.Pepper);
    var pepperLength = _otpOptions.Value?.Pepper?.Length ?? 0;
    
    return Ok(new {
        configured = pepperConfigured,
        lengthChars = pepperLength,
        lengthBytes = pepperLength > 0 ? Convert.FromBase64String(_otpOptions.Value.Pepper).Length : 0,
        // ⚠️ NEVER return actual pepper value
    });
}
```

### 8.7 Troubleshooting

**Symptom:** OTP verification always fails
- **Cause:** Pepper changed but old OTPs still in Redis
- **Fix:** Wait 5 minutes for TTL expiry, or flush `otp:*` keys

**Symptom:** `FormatException: Invalid Base64 string`
- **Cause:** Pepper contains invalid Base64 characters
- **Fix:** Regenerate pepper using `openssl rand -base64 32`

**Symptom:** All OTPs fail after deployment
- **Cause:** Key Vault reference not resolving (managed identity issue)
- **Fix:** Verify managed identity assigned and Key Vault access policies configured

**Symptom:** `System.ArgumentException: IDX10603: Decryption failed`
- **Cause:** Pepper mismatch between hash time and verify time
- **Fix:** Ensure configuration is stable during restart, check Key Vault connectivity

---

## 9. Libraries & Implementation Notes (.NET)

| Task | Recommendation |
|------|----------------|
| Argon2id | Use a vetted library (e.g., Isopoh.Cryptography.Argon2) until native .NET KDF suits needs. |
| Constant-time compare | `CryptographicOperations.FixedTimeEquals(byte[], byte[])` |
| Random salt | `RandomNumberGenerator.Fill()` 16 bytes |
| Base64 encoding | `Convert.ToBase64String()` (avoid line breaks) |
| Avoid secret copies | Clear byte arrays after use where feasible (`Array.Clear`) |

---

## 10. Pseudocode (Argon2id)

```csharp
public string HashOtp(string userName, string code) {
    var pepper = _options.PepperBytes; // loaded once at startup
    var salt = RandomBytes(16);
    var input = Concat(pepper, Encoding.UTF8.GetBytes(userName), salt, Encoding.UTF8.GetBytes(code));

    var cfg = new Argon2Config {
        Type = Argon2Type.Id,
        TimeCost = 3,
        MemoryCost = 64 * 1024, // KB
        Lanes = 1,
        Threads = 1,
        Password = input,
        Salt = salt,
        HashLength = 32
    };

    var hash = Argon2.Hash(cfg); // returns raw bytes
    return $"OtpHash:v2:argon2id:m={cfg.MemoryCost},t={cfg.TimeCost},p={cfg.Lanes}:{B64(salt)}:{B64(hash)}";
}

public bool VerifyOtp(string userName, string code, string stored) {
    // Parse pieces → extract params, salt, expected hash
    // Recompute with same params + pepper
    // FixedTimeEquals(recomputed, expected)
}
```

(Final code should centralize param parsing and handle malformed values gracefully.)

---

## 11. Logging & Telemetry

Log (Info/Debug) only:
- Attempt count
- Result category (`success`, `invalid`, `expired`, `rate_limited`) — internally
- Duration (ms)
Never log:
- Raw OTP
- Hash
- Salt
- Pepper
- Full concatenated preimage

Tag spans with `otp.outcome` and anonymized attempt counters if you already produce telemetry.

---

## 12. Testing Plan

| Test | Purpose |
|------|---------|
| Hash/Verify success | Round-trip one code. |
| Mismatch detection | Wrong code fails. |
| Tampered stored value | Malformed format returns safe failure (no exception leak). |
| Rate limit enforced | 6th attempt blocked. |
| Concurrency | Multiple simultaneous verifications safe (idempotent remove). |
| Performance | Measure average hash time (goal: < 50ms under typical load for Argon2 parameters). |

---

## 13. Migration Plan (No Legacy Plaintext Yet)

Because current Redis values are plaintext codes (today):
1. Deploy hasher implementation behind feature flag: `Otp__HashingEnabled=true`.
2. On code issuance: if enabled → store hashed; else plaintext (roll back safety).
3. Verification: detect format:  
   - If starts with `OtpHash:` → hashed path.  
   - Else plaintext (legacy) → compare directly (constant-time) and upon success delete.  
4. After a short window (greater than max TTL), enforce hashed only and remove legacy branch.
5. Remove fallback code and flip flag default to `true` in config.

If you prefer not to support legacy at all (since codes are ephemeral): just cut over immediately; any in-flight plaintext codes will silently break (acceptable if deployment coordinated).

---

## 14. Performance Tuning Guidance

- Start with Argon2id ~25–35ms per hash on your production hardware.
- If login storms expected (e.g., 100s per second) and CPU spike occurs, reduce memory cost first, then iterations.
- Monitor p95 verification latency; keep under user experience threshold (e.g., 150ms including network).

---

## 15. Summary Recommendation

Adopt Option B (Argon2id + salt + pepper + versioned format) with moderate parameters and a tight OTP TTL plus rate limiting. Maintain an extensible `IOtpHasher` abstraction for future upgrades. This maximizes defense-in-depth with minimal code complexity increase, and allows seamless rotation without user friction.