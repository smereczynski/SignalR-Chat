using System;
using System.Text;
using Chat.Web.Options;
using Chat.Web.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace Chat.Tests;

public class OtpHasherTests
{
    private static IOptions<OtpOptions> Options(bool hashingEnabled = true, int mKb = 8*1024, int t = 2, int p = 1, int outLen = 16)
    {
        var pepper = Convert.ToBase64String(Encoding.UTF8.GetBytes("test-pepper-32bytes-minimum-entropy!!"));
        return Microsoft.Extensions.Options.Options.Create(new OtpOptions
        {
            Pepper = pepper,
            HashingEnabled = hashingEnabled,
            MemoryKB = mKb,
            Iterations = t,
            Parallelism = p,
            OutputLength = outLen
        });
    }

    [Fact]
    public void HashAndVerify_Match()
    {
        var hasher = new Argon2OtpHasher(Options());
        var user = "alice";
        var code = "123456";
        var stored = hasher.Hash(user, code);
        Assert.StartsWith("OtpHash:v2:argon2id:", stored);
        var result = hasher.Verify(user, code, stored);
        Assert.True(result.IsMatch);
        Assert.False(result.NeedsRehash);
    }

    [Fact]
    public void Verify_Mismatch_Fails()
    {
        var hasher = new Argon2OtpHasher(Options());
        var user = "bob";
        var stored = hasher.Hash(user, "111111");
        var result = hasher.Verify(user, "222222", stored);
        Assert.False(result.IsMatch);
    }

    [Fact]
    public void Verify_Indicates_NeedsRehash_When_Params_Increase()
    {
        var low = Options(mKb: 8*1024, t: 1, p: 1);
        var hasherLow = new Argon2OtpHasher(low);
        var user = "carol";
        var code = "654321";
        var stored = hasherLow.Hash(user, code);

        var higher = Options(mKb: 16*1024, t: 2, p: 1);
        var hasherHigh = new Argon2OtpHasher(higher);
        var result = hasherHigh.Verify(user, code, stored);
        Assert.True(result.IsMatch);
        Assert.True(result.NeedsRehash);
    }
}
