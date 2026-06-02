using System.Reflection;
using System.Xml.Linq;
using Xunit;

namespace MediBridge.IntegrationTests;

public class Phase3LayeringBoundaryTests
{
    [Fact]
    public void Core_Should_Not_Reference_Persistence_Http_Repository_Or_Api_Assemblies()
    {
        var coreAssembly = typeof(MediBridge.Core.Interfaces.IDomainUnitOfWork).Assembly;
        var forbiddenReferences = coreAssembly.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(name =>
                name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) ||
                name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal) ||
                name.Equals("MediBridge.Repository", StringComparison.Ordinal) ||
                name.Equals("MediBridge.APIs", StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(forbiddenReferences);
    }

    [Fact]
    public void Core_Project_Should_Not_Declare_Forbidden_Package_Or_Project_References()
    {
        var project = XDocument.Load(GetRepositoryPath("MediBridge.Core", "MediBridge.Core.csproj"));
        var forbiddenReferences = project.Descendants()
            .Where(element => element.Name.LocalName is "PackageReference" or "ProjectReference")
            .Select(element => element.Attribute("Include")?.Value ?? string.Empty)
            .Where(include =>
                include.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) ||
                include.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal) ||
                include.Contains("MediBridge.Repository", StringComparison.Ordinal) ||
                include.Contains("MediBridge.APIs", StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(forbiddenReferences);
    }

    [Fact]
    public void Core_Types_Should_Not_Expose_Forbidden_Infrastructure_Types()
    {
        var coreAssembly = typeof(MediBridge.Core.Interfaces.IDomainUnitOfWork).Assembly;
        var violations = coreAssembly.GetTypes()
            .SelectMany(type => GetReferencedPublicTypes(type), (owner, referencedType) => new { owner, referencedType })
            .Where(candidate => IsForbiddenInfrastructureType(candidate.referencedType))
            .Select(candidate => $"{candidate.owner.FullName} -> {candidate.referencedType.FullName}")
            .ToArray();

        Assert.Empty(violations);
    }

    private static IEnumerable<Type> GetReferencedPublicTypes(Type type)
    {
        foreach (var constructor in type.GetConstructors())
        {
            foreach (var parameter in constructor.GetParameters())
            {
                yield return parameter.ParameterType;
            }
        }

        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            yield return property.PropertyType;
        }

        foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
        {
            yield return field.FieldType;
        }

        foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly))
        {
            yield return method.ReturnType;
            foreach (var parameter in method.GetParameters())
            {
                yield return parameter.ParameterType;
            }
        }
    }

    private static bool IsForbiddenInfrastructureType(Type type)
    {
        var typeName = GetNonGenericTypeName(type);
        return typeName.StartsWith("Microsoft.EntityFrameworkCore.", StringComparison.Ordinal) ||
               typeName.StartsWith("Microsoft.AspNetCore.Http.", StringComparison.Ordinal) ||
               typeName.StartsWith("MediBridge.Repository.", StringComparison.Ordinal) ||
               typeName.StartsWith("MediBridge.APIs.", StringComparison.Ordinal);
    }

    private static string GetNonGenericTypeName(Type type)
    {
        if (type.IsGenericType)
        {
            return type.GetGenericTypeDefinition().FullName ?? string.Empty;
        }

        return type.FullName ?? string.Empty;
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
