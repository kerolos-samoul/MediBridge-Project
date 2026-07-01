using System.Reflection;
using System.Xml.Linq;
using MediBridge.Services.Interfaces;
using Xunit;

namespace MediBridge.IntegrationTests.Workflow;

public sealed class WalletCampaignServiceBoundaryTests
{
    private static readonly string[] ForbiddenPrefixes =
    [
        "Microsoft.EntityFrameworkCore",
        "MediBridge.Repository",
        "MediBridge.APIs.Controllers"
    ];

    [Fact]
    public void WalletCampaignServices_Should_Not_Reference_EfCore_Or_Api_Controllers()
    {
        var project = XDocument.Load(GetRepositoryPath("MediBridge.Services", "MediBridge.Services.csproj"));
        var projectViolations = project.Descendants()
            .Where(element => element.Name.LocalName is "PackageReference" or "ProjectReference")
            .Select(element => element.Attribute("Include")?.Value ?? string.Empty)
            .Where(IsForbiddenName)
            .Select(reference => $"project reference -> {reference}");

        var serviceAssembly = typeof(ICompanyWalletService).Assembly;
        var assemblyViolations = serviceAssembly.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(IsForbiddenName)
            .Select(reference => $"assembly reference -> {reference}");

        var typeViolations = serviceAssembly.GetTypes()
            .Where(type => type.Namespace?.StartsWith("MediBridge.Services", StringComparison.Ordinal) == true)
            .SelectMany(type => GetReferencedTypes(type), (owner, referencedType) =>
                new { Owner = owner, ReferencedType = referencedType })
            .Where(candidate => IsForbiddenType(candidate.ReferencedType))
            .Select(candidate => $"{candidate.Owner.FullName} -> {candidate.ReferencedType.FullName}");

        var sourceViolations = Directory
            .EnumerateFiles(GetRepositoryPath("MediBridge.Services"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .Select(path => new { Path = path, Source = File.ReadAllText(path) })
            .SelectMany(candidate => ForbiddenPrefixes
                .Where(marker => candidate.Source.Contains(marker, StringComparison.Ordinal))
                .Select(marker => $"{candidate.Path} -> {marker}"));

        Assert.Empty(projectViolations
            .Concat(assemblyViolations)
            .Concat(typeViolations)
            .Concat(sourceViolations)
            .Distinct(StringComparer.Ordinal));
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

        return candidates.Any(candidate => IsForbiddenName(candidate.FullName ?? string.Empty));
    }

    private static bool IsForbiddenName(string name)
        => ForbiddenPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal));

    private static bool IsBuildOutput(string path)
        => path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
           path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);

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
