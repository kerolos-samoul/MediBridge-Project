using MediBridge.Services.DTOs.Auth;
using MediBridge.Services.Services;
using MediBridge.Services.Validators.Auth;
using Xunit;

namespace MediBridge.UnitTests;

public sealed class RefreshCredentialLifecycleTests
{
    [Fact]
    public void AuthTokenService_HashesRefreshTokensWithoutReturningPlaintextHash()
    {
        var service = CreateService();

        var (plaintextToken, tokenHash) = service.CreateRefreshToken();

        Assert.False(string.IsNullOrWhiteSpace(plaintextToken));
        Assert.False(string.IsNullOrWhiteSpace(tokenHash));
        Assert.NotEqual(plaintextToken, tokenHash);
        Assert.Equal(tokenHash, service.HashToken(plaintextToken));
    }

    [Fact]
    public void AuthTokenService_CreatesRoleBearingAccessToken()
    {
        var service = CreateService();

        var token = service.CreateAccessToken("user-1", "doctor@example.com", "Doctor");

        Assert.Contains(".", token);
        Assert.Equal(3, token.Split('.').Length);
    }

    [Fact]
    public async Task RefreshRequestValidator_RequiresRefreshToken()
    {
        var validator = new RefreshRequestValidator();

        var result = await validator.ValidateAsync(new RefreshRequestDto());

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task LoginRequestValidator_RequiresUsernameAndPassword()
    {
        var validator = new LoginRequestValidator();

        var result = await validator.ValidateAsync(new LoginRequestDto());

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(LoginRequestDto.Username));
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(LoginRequestDto.Password));
    }

    private static AuthTokenService CreateService()
    {
        return new AuthTokenService(
            "MediBridge.UnitTests",
            "MediBridge.UnitTests.ApiClients",
            "UnitTestSigningKey-ReplaceBeforeProduction-32Chars",
            accessTokenMinutes: 15,
            refreshTokenDays: 7);
    }
}
