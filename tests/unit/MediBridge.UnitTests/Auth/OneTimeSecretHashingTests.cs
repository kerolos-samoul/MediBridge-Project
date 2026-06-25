using MediBridge.Services.Services;
using Xunit;

namespace MediBridge.UnitTests.Auth;

public sealed class OneTimeSecretHashingTests
{
    [Fact]
    public void CreateNumericCode_ReturnsRequestedNumberOfDigits()
    {
        var service = CreateService();

        var code = service.CreateNumericCode(6);

        Assert.Equal(6, code.Length);
        Assert.All(code, digit => Assert.True(char.IsDigit(digit)));
    }

    [Fact]
    public void VerifyOneTimeSecret_UsesDestinationBoundHmac()
    {
        var service = CreateService();
        var destination = "doctor@example.com";
        var code = "123456";

        var hash = service.HashOneTimeSecret(destination, code);

        Assert.NotEqual(code, hash);
        Assert.DoesNotContain(destination, hash, StringComparison.OrdinalIgnoreCase);
        Assert.True(service.VerifyOneTimeSecret(destination, code, hash));
        Assert.False(service.VerifyOneTimeSecret(destination, "000000", hash));
        Assert.False(service.VerifyOneTimeSecret("other@example.com", code, hash));
    }

    private static AuthTokenService CreateService()
        => new(
            "issuer",
            "audience",
            "jwt-signing-key-for-unit-tests-32-bytes",
            "one-time-secret-hashing-key-for-unit-tests",
            accessTokenMinutes: 60,
            refreshTokenDays: 7);
}
