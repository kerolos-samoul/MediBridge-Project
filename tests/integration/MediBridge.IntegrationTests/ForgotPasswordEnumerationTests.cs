using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Repository.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class ForgotPasswordEnumerationTests
{
    [Fact]
    public async Task ForgotPassword_ReturnsSameEnvelopeForKnownAndUnknownContacts()
    {
        await using var factory = new WebAppFactory();
        await factory.InitializeDatabaseAsync();
        using var client = factory.CreateClient();

        var knownEmail = await Phase6IdentityTestHelpers.RegisterDoctorAsync(client);
        using var knownResponse = await client.PostAsJsonAsync("/api/auth/forgot-password", new { Contact = knownEmail });
        using var unknownResponse = await client.PostAsJsonAsync("/api/auth/forgot-password", new { Contact = $"missing-{Guid.NewGuid():N}@example.com" });

        Assert.Equal(HttpStatusCode.Accepted, knownResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, unknownResponse.StatusCode);

        var knownBody = JsonDocument.Parse(await knownResponse.Content.ReadAsStringAsync()).RootElement;
        var unknownBody = JsonDocument.Parse(await unknownResponse.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(knownBody.GetProperty("Code").GetInt32(), unknownBody.GetProperty("Code").GetInt32());
        Assert.Equal(knownBody.GetProperty("Message").GetString(), unknownBody.GetProperty("Message").GetString());
        Assert.Equal(JsonValueKind.Null, knownBody.GetProperty("Data").ValueKind);
        Assert.Equal(JsonValueKind.Null, unknownBody.GetProperty("Data").ValueKind);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediBridgeDbContext>();
        Assert.Equal(1, await db.PasswordResetFlows.CountAsync());
    }
}
