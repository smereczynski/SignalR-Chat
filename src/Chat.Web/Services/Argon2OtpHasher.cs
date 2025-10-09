using System;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Chat.Web.Options;
using Microsoft.Extensions.Options;
using Isopoh.Cryptography.Argon2;

namespace Chat.Web.Services
{
    /// <summary>
    /// Argon2id-based OTP hasher with pepper + per-code salt and versioned format.
    /// Format: OtpHash:v2:argon2id:m={kb},t={it},p={par}:{saltB64}:{hashB64}
    /// </summary>
    public class Argon2OtpHasher : IOtpHasher
    {
        private readonly OtpOptions _options;
        private readonly byte[] _pepperBytes;

        public Argon2OtpHasher(IOptions<OtpOptions> options)
        {
            _options = options.Value;
            _pepperBytes = string.IsNullOrWhiteSpace(_options.Pepper)
                ? Array.Empty<byte>()
                : Convert.FromBase64String(_options.Pepper);
        }

        public string Hash(string userName, string code)
        {
            var salt = RandomNumberGenerator.GetBytes(16);
            var preimage = BuildPreimage(userName, code, salt);
            var cfg = new Argon2Config
            {
                Type = Argon2Type.HybridAddressing,
                TimeCost = Math.Max(1, _options.Iterations),
                MemoryCost = Math.Max(8 * 1024, _options.MemoryKB), // min 8MB
                Lanes = Math.Max(1, _options.Parallelism),
                Threads = Math.Max(1, _options.Parallelism),
                Password = preimage,
                Salt = salt,
                HashLength = Math.Max(16, _options.OutputLength)
            };
            var encoded = Argon2.Hash(cfg); // PHC-style encoded hash containing params and salt
            var saltB64 = Convert.ToBase64String(salt);
            return $"OtpHash:v2:argon2id:m={cfg.MemoryCost},t={cfg.TimeCost},p={cfg.Lanes}:{saltB64}:{encoded}";
        }

        public VerificationResult Verify(string userName, string code, string stored)
        {
            if (string.IsNullOrWhiteSpace(stored)) return new VerificationResult(false, false);
            if (!stored.StartsWith("OtpHash:", StringComparison.Ordinal)) return new VerificationResult(false, false);

            // Parse format
            // OtpHash:v2:argon2id:m=..,t=..,p=..:salt:hash
            var parts = stored.Split(':');
            if (parts.Length != 6) return new VerificationResult(false, false);
            var version = parts[1];
            var algo = parts[2];
            if (!string.Equals(version, "v2", StringComparison.Ordinal) || !string.Equals(algo, "argon2id", StringComparison.Ordinal))
                return new VerificationResult(false, false);

            // params
            var paramStr = parts[3];
            int m = _options.MemoryKB, t = _options.Iterations, p = _options.Parallelism;
            try
            {
                foreach (var kv in paramStr.Split(','))
                {
                    var kvp = kv.Split('=');
                    if (kvp.Length != 2) continue;
                    if (kvp[0] == "m") m = int.Parse(kvp[1]);
                    else if (kvp[0] == "t") t = int.Parse(kvp[1]);
                    else if (kvp[0] == "p") p = int.Parse(kvp[1]);
                }
            }
            catch { return new VerificationResult(false, false); }

            byte[] salt;
            try
            {
                salt = Convert.FromBase64String(parts[4]);
            }
            catch { return new VerificationResult(false, false); }

            var preimage = BuildPreimage(userName, code, salt);
            // Verify using encoded hash string; recompute with same preimage
            var encoded = parts[5];
            var isMatch = Argon2.Verify(encoded, preimage, Math.Max(1, p));

            // NeedsRehash if current configured params are higher than stored
            var needsRehash = _options.HashingEnabled && (m < _options.MemoryKB || t < _options.Iterations || p < _options.Parallelism);
            return new VerificationResult(isMatch, needsRehash);
        }

        private byte[] BuildPreimage(string userName, string code, byte[] salt)
        {
            var userBytes = Encoding.UTF8.GetBytes(userName ?? string.Empty);
            var codeBytes = Encoding.UTF8.GetBytes(code ?? string.Empty);
            var preimage = new byte[_pepperBytes.Length + userBytes.Length + salt.Length + codeBytes.Length + 2];
            int o = 0;
            Buffer.BlockCopy(_pepperBytes, 0, preimage, o, _pepperBytes.Length); o += _pepperBytes.Length;
            Buffer.BlockCopy(userBytes, 0, preimage, o, userBytes.Length); o += userBytes.Length;
            preimage[o++] = (byte)':';
            Buffer.BlockCopy(salt, 0, preimage, o, salt.Length); o += salt.Length;
            preimage[o++] = (byte)':';
            Buffer.BlockCopy(codeBytes, 0, preimage, o, codeBytes.Length);
            return preimage;
        }
    }
}
