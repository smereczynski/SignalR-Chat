## 1. Threat Model & Goals

What we’re protecting against:
- Offline disclosure if a Redis dump / memory snapshot is exfiltrated.
- Log leakage (accidental plaintext OTP logging).
- Insider read access to Redis contents.

What we cannot fully prevent:
- Online brute force via the verification API unless rate limiting is enforced.
- Exhaustive offline brute force of a 6‑digit code (≈ 1,000,000 possibilities) if the hash function is very fast and unkeyed.

Therefore:
1. Eliminate plaintext storage (hash before storing).
2. Add a secret “pepper” (server secret not stored in Redis) to raise the bar for an offline attacker.
3. (Optional) Use a memory‑hard KDF (Argon2id/scrypt) if the OTP throughput is modest, to materially slow offline guessing.
4. Enforce online rate limiting regardless (defense in depth).

---

## 2. Core Concepts

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

- Source from a high-entropy secret (32+ random bytes Base64) via environment variable.
- Rotate by:
  1. Introduce `v3` hasher with new pepper.
  2. Issue only new OTPs hashed with `v3`.
  3. Old OTPs expire naturally (short TTL), so no live migration needed.
- Never log the pepper or concatenated preimage values.

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