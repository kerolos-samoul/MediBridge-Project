using MediBridge.Core.Interfaces;
using MediBridge.IntegrationTests.TestHost;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class SecurityReadinessBoundaryTests
{
    [Theory]
    [InlineData(typeof(IAuditLogger))]
    [InlineData(typeof(ICurrentUserContext))]
    [InlineData(typeof(IOwnershipAuthorizationService))]
    public void SecurityReadinessContracts_AreCoreOwnedAndFrameworkNeutral(Type contractType)
    {
        Assert.Equal("MediBridge.Core.Interfaces", contractType.Namespace);

        var referencedAssemblies = contractType.Assembly.GetReferencedAssemblies().Select(reference => reference.Name).ToArray();

        Assert.DoesNotContain("Microsoft.AspNetCore.Http", referencedAssemblies);
        Assert.DoesNotContain("Microsoft.AspNetCore.Mvc", referencedAssemblies);
        Assert.DoesNotContain("Microsoft.EntityFrameworkCore", referencedAssemblies);
    }

    [Fact]
    public void CurrentUserAndOwnershipAbstractions_AreRegisteredForApiRequests()
    {
        using var factory = new WebAppFactory();
        using var scope = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Production"))
            .Services
            .CreateScope();

        var currentUser = scope.ServiceProvider.GetRequiredService<ICurrentUserContext>();
        var ownership = scope.ServiceProvider.GetRequiredService<IOwnershipAuthorizationService>();
        var auditLogger = scope.ServiceProvider.GetRequiredService<IAuditLogger>();

        Assert.False(currentUser.IsAuthenticated);
        Assert.Null(currentUser.UserId);
        Assert.False(ownership.IsOwnerOrAdmin(new OwnershipRequirement("Wallet", "Doctor", "doctor-1"), currentUser));
        Assert.NotNull(auditLogger);
    }
}
