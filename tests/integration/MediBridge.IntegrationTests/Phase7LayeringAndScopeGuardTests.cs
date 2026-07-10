using System.Reflection;
using MediBridge.APIs.Controllers;
using MediBridge.APIs.Extensions;
using MediBridge.Core.Entities.Messaging;
using MediBridge.Core.Interfaces;
using MediBridge.Core.Interfaces.Messaging;
using MediBridge.Core.Interfaces.Time;
using MediBridge.IntegrationTests.TestHost;
using MediBridge.Services.Services;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class Phase7LayeringAndScopeGuardTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void Core_has_no_entity_framework_hangfire_or_http_dependency()
    {
        var references = typeof(DoctorAdDelivery).Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToArray();

        Assert.DoesNotContain(references, name => name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
        Assert.DoesNotContain(references, name => name.StartsWith("Hangfire", StringComparison.Ordinal));
        Assert.DoesNotContain(references, name => name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
        AssertNoSourceMarker("MediBridge.Core", "Microsoft.EntityFrameworkCore", "Hangfire", "Microsoft.AspNetCore.Http", "Microsoft.AspNetCore.Mvc");
    }

    [Fact]
    public void Recovery_coordinator_depends_only_on_core_abstractions_and_logging()
    {
        var constructor = Assert.Single(typeof(DeliveryJobRecoveryCoordinator).GetConstructors());

        Assert.Equal(
            [typeof(IDomainUnitOfWork), typeof(IEgyptBusinessClock), typeof(IDeliveryJobEnqueuer), typeof(ILogger<DeliveryJobRecoveryCoordinator>)],
            constructor.GetParameters().Select(parameter => parameter.ParameterType).ToArray());
        AssertNoSourceMarker(
            "MediBridge.Services/Services/DeliveryJobRecoveryCoordinator.cs",
            "Microsoft.EntityFrameworkCore",
            "Hangfire",
            "MediBridge.Repository",
            "DbContext");
    }

    [Fact]
    public void Hangfire_adapter_and_registrar_have_no_repository_or_business_mutation_access()
    {
        var enqueuerParameters = Assert.Single(typeof(HangfireDeliveryJobEnqueuer).GetConstructors())
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();
        Assert.Equal(2, enqueuerParameters.Length);
        Assert.Contains(enqueuerParameters, type => type.Name == "IBackgroundJobClient");
        Assert.Contains(enqueuerParameters, type => type == typeof(IOptions<MediBridge.APIs.Config.DeliveryJobOptions>));

        var registrarParameters = Assert.Single(typeof(RecurringDeliveryJobRegistrar).GetConstructors())
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();
        Assert.Contains(typeof(IEgyptBusinessClock), registrarParameters);
        Assert.DoesNotContain(registrarParameters, type => type.Namespace?.StartsWith("MediBridge.Repository", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(registrarParameters, type => type == typeof(IDomainUnitOfWork));

        AssertNoSourceMarker(
            "MediBridge.APIs/Extensions/HangfireDeliveryJobEnqueuer.cs",
            "MediBridge.Repository",
            "IDomainUnitOfWork",
            "MediBridgeDbContext",
            "Wallet",
            "Ledger");
        AssertNoSourceMarker(
            "MediBridge.APIs/Extensions/RecurringDeliveryJobRegistrar.cs",
            "MediBridge.Repository",
            "IDomainUnitOfWork",
            "MediBridgeDbContext");
    }

    [Fact]
    public void Doctor_controller_is_transport_only_and_startup_invokes_only_recovery_coordination()
    {
        var controllerConstructor = Assert.Single(typeof(DoctorMessagesController).GetConstructors());
        Assert.All(controllerConstructor.GetParameters(), parameter =>
            Assert.True(parameter.ParameterType.IsInterface, $"Unexpected concrete controller dependency {parameter.ParameterType}."));
        Assert.DoesNotContain(controllerConstructor.GetParameters(), parameter =>
            parameter.ParameterType.Namespace?.StartsWith("MediBridge.Repository", StringComparison.Ordinal) == true);

        AssertNoSourceMarker(
            "MediBridge.APIs/Controllers/DoctorMessagesController.cs",
            "MediBridge.Repository",
            "IDomainUnitOfWork",
            "MediBridgeDbContext",
            "DateTime.Now",
            "DateTime.UtcNow");

        var program = ReadSource("MediBridge.APIs/Program.cs");
        Assert.Contains("GetRequiredService<IDeliveryJobRecoveryCoordinator>()", program, StringComparison.Ordinal);
        Assert.Contains(".RecoverAsync(", program, StringComparison.Ordinal);
        Assert.DoesNotContain("GetRequiredService<IDeliveryExpiryService>()", program, StringComparison.Ordinal);
        Assert.DoesNotContain("GetRequiredService<IDailyDeliveryInjectorService>()", program, StringComparison.Ordinal);
        Assert.DoesNotContain("UseHangfireDashboard", program, StringComparison.Ordinal);
    }

    [Fact]
    public void Production_exposes_only_contracted_doctor_message_routes_and_no_future_or_job_control_surface()
    {
        using var factory = new ProductionWebAppFactory();
        var routes = factory.Services.GetRequiredService<IEnumerable<EndpointDataSource>>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText ?? string.Empty)
            .ToArray();

        Assert.Equal(
            [
                "api/doctor/messages/today",
                "api/doctor/messages/{deliveryId}/assets/{fileId}/access",
                "api/doctor/messages/{deliveryId}/interact",
                "api/doctor/messages/{deliveryId}/read"
            ],
            routes.Where(route => route.StartsWith("api/doctor/messages", StringComparison.OrdinalIgnoreCase))
                .OrderBy(route => route, StringComparer.Ordinal)
                .ToArray());

        var forbiddenRouteMarkers = new[]
        {
            "hangfire", "/jobs", "job-control", "charge", "earn",
            "weekly", "activity", "notification", "analytics"
        };
        Assert.DoesNotContain(routes, route => forbiddenRouteMarkers.Any(marker =>
            route.Contains(marker, StringComparison.OrdinalIgnoreCase)));

        var forbiddenServiceMarkers = new[]
        {
            "WeeklyEnforcement", "ActivityScore",
            "Notification", "Analytics"
        };
        var serviceTypeNames = typeof(DeliveryExpiryService).Assembly.GetTypes()
            .Where(type => type.Namespace == "MediBridge.Services.Services")
            .Select(type => type.Name)
            .ToArray();
        Assert.DoesNotContain(serviceTypeNames, name => forbiddenServiceMarkers.Any(marker =>
            name.Contains(marker, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void Repository_is_the_only_entity_framework_domain_boundary()
    {
        AssertNoSourceMarker("MediBridge.Core", "Microsoft.EntityFrameworkCore", "DbContext", "IQueryable<");
        AssertNoSourceMarker("MediBridge.Services", "Microsoft.EntityFrameworkCore", "MediBridgeDbContext", "DbSet<");
        AssertNoSourceMarker("MediBridge.APIs", "MediBridgeDbContext", "DbSet<");

        var repositoryProject = ReadSource("MediBridge.Repository/MediBridge.Repository.csproj");
        Assert.Contains("Microsoft.EntityFrameworkCore.SqlServer", repositoryProject, StringComparison.Ordinal);
    }

    private static void AssertNoSourceMarker(string relativePath, params string[] markers)
    {
        var absolutePath = Path.Combine(RepositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var files = File.Exists(absolutePath)
            ? [absolutePath]
            : Directory.EnumerateFiles(absolutePath, "*.cs", SearchOption.AllDirectories)
                .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                    && !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                .ToArray();
        var hits = files
            .SelectMany(file => File.ReadAllLines(file).Select((line, index) => new { file, line, index }))
            .Where(hit => markers.Any(marker => hit.line.Contains(marker, StringComparison.Ordinal)))
            .Select(hit => $"{Path.GetRelativePath(RepositoryRoot, hit.file)}:{hit.index + 1}:{hit.line.Trim()}")
            .ToArray();

        Assert.True(hits.Length == 0, $"Forbidden boundary references:{Environment.NewLine}{string.Join(Environment.NewLine, hits)}");
    }

    private static string ReadSource(string relativePath)
        => File.ReadAllText(Path.Combine(RepositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MediBridge.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate repository root.");
    }
}
