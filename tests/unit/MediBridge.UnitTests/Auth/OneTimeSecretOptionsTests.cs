using MediBridge.Services.Config;
using Xunit;

namespace MediBridge.UnitTests.Auth;

public sealed class OneTimeSecretOptionsTests
{
    [Fact]
    public void ContactVerificationOptions_RequiresSeparateStrongHashingKey()
    {
        var options = new ContactVerificationOptions
        {
            OneTimeSecretHashingKey = "short"
        };

        Assert.IsType<InvalidOperationException>(Record.Exception(() => options.Validate(jwtSigningKey: "short")));
    }

    [Fact]
    public void ContactVerificationOptions_RejectsJwtSigningKeyReuse()
    {
        var sharedSecret = new string('a', 32);
        var options = new ContactVerificationOptions
        {
            OneTimeSecretHashingKey = sharedSecret
        };

        Assert.IsType<InvalidOperationException>(Record.Exception(() => options.Validate(jwtSigningKey: sharedSecret)));
    }

    [Fact]
    public void PasswordResetOptions_RequiresHttpsResetLinkBaseUri()
    {
        var options = new PasswordResetOptions
        {
            ResetLinkBaseUri = "http://localhost/reset"
        };

        Assert.IsType<InvalidOperationException>(Record.Exception(() => options.Validate()));
    }
}
