using System.Reflection;
using MediBridge.APIs.Controllers;
using Xunit;

namespace MediBridge.IntegrationTests.Workflow;

public sealed class WalletCampaignControllerBoundaryTests
{
    private static readonly Type[] WorkflowControllerTypes =
    [
        typeof(CompanyWalletController),
        typeof(CampaignsController),
        typeof(AdminPricingController),
        typeof(AdminCampaignsController)
    ];

    private static readonly string[] ForbiddenSourceMarkers =
    [
        "MediBridgeDbContext",
        "Microsoft.EntityFrameworkCore",
        "MediBridge.Repository"
    ];

    [Fact]
    public void WalletCampaignControllers_Should_Not_Reference_Persistence_Implementations()
    {
        var typeViolations = WorkflowControllerTypes
            .SelectMany(controller => GetReferencedTypes(controller), (controller, referencedType) =>
                new { Controller = controller, ReferencedType = referencedType })
            .Where(candidate => IsForbiddenType(candidate.ReferencedType))
            .Select(candidate => $"{candidate.Controller.FullName} -> {candidate.ReferencedType.FullName}")
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var sourceViolations = WorkflowControllerTypes
            .Select(controller => new
            {
                Controller = controller,
                Source = File.ReadAllText(GetRepositoryPath("MediBridge.APIs", "Controllers", $"{controller.Name}.cs"))
            })
            .SelectMany(
                candidate => ForbiddenSourceMarkers
                    .Where(marker => candidate.Source.Contains(marker, StringComparison.Ordinal))
                    .Select(marker => $"{candidate.Controller.FullName} -> {marker}"))
            .ToArray();

        Assert.Empty(typeViolations.Concat(sourceViolations));
    }

    private static IEnumerable<Type> GetReferencedTypes(Type type)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                                   BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        foreach (var constructor in type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            foreach (var parameter in constructor.GetParameters())
            {
                yield return parameter.ParameterType;
            }
        }

        foreach (var property in type.GetProperties(flags))
        {
            yield return property.PropertyType;
        }

        foreach (var field in type.GetFields(flags))
        {
            yield return field.FieldType;
        }

        foreach (var method in type.GetMethods(flags))
        {
            yield return method.ReturnType;
            foreach (var parameter in method.GetParameters())
            {
                yield return parameter.ParameterType;
            }

            foreach (var local in method.GetMethodBody()?.LocalVariables ?? [])
            {
                yield return local.LocalType;
            }
        }
    }

    private static bool IsForbiddenType(Type type)
    {
        var candidates = type.IsGenericType
            ? type.GetGenericArguments().Append(type.GetGenericTypeDefinition())
            : [type];

        return candidates.Any(candidate =>
        {
            var name = candidate.FullName ?? string.Empty;
            return name.Equals("MediBridge.Repository.Data.MediBridgeDbContext", StringComparison.Ordinal) ||
                   name.StartsWith("Microsoft.EntityFrameworkCore.", StringComparison.Ordinal) ||
                   name.StartsWith("MediBridge.Repository.", StringComparison.Ordinal);
        });
    }

    private static string GetRepositoryPath(params string[] parts)
    {
        var root = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && !File.Exists(Path.Combine(root, "MediBridge.slnx")); i++)
        {
            root = Directory.GetParent(root)?.FullName ?? root;
        }

        return Path.Combine(new[] { root }.Concat(parts).ToArray());
    }
}
