using System.Reflection;
using System.Xml.Linq;
using Xunit;

namespace MediBridge.IntegrationTests;

public class Phase3ServicePersistenceBoundaryTests
{
    [Fact]
    public void Services_Project_Should_Not_Reference_EfCore_Or_Repository_Implementations()
    {
        var project = XDocument.Load(GetRepositoryPath("MediBridge.Services", "MediBridge.Services.csproj"));
        var forbiddenReferences = project.Descendants()
            .Where(element => element.Name.LocalName is "PackageReference" or "ProjectReference")
            .Select(element => element.Attribute("Include")?.Value ?? string.Empty)
            .Where(include =>
                include.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) ||
                include.Contains("MediBridge.Repository", StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(forbiddenReferences);
    }

    [Fact]
    public void Services_Assembly_Should_Not_Reference_EfCore_Or_Repository_Assemblies()
    {
        var serviceAssembly = typeof(MediBridge.Services.Interfaces.IAuthService).Assembly;
        var forbiddenReferences = serviceAssembly.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(name =>
                name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) ||
                name.Equals("MediBridge.Repository", StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(forbiddenReferences);
    }

    [Fact]
    public void Service_Types_Should_Not_Depend_On_DbContext_EfCore_Or_Concrete_Repositories()
    {
        var serviceAssembly = typeof(MediBridge.Services.Interfaces.IAuthService).Assembly;
        var violations = serviceAssembly.GetTypes()
            .Where(type => type.Namespace?.StartsWith("MediBridge.Services", StringComparison.Ordinal) == true)
            .SelectMany(type => GetReferencedTypes(type), (owner, referencedType) => new { owner, referencedType })
            .Where(candidate => IsPersistenceImplementation(candidate.referencedType))
            .Select(candidate => $"{candidate.owner.FullName} -> {candidate.referencedType.FullName}")
            .ToArray();

        Assert.Empty(violations);
    }

    private static IEnumerable<Type> GetReferencedTypes(Type type)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

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

    private static bool IsPersistenceImplementation(Type type)
    {
        var typeName = GetNonGenericTypeName(type);
        return typeName.Equals("MediBridge.Repository.Data.MediBridgeDbContext", StringComparison.Ordinal) ||
               typeName.StartsWith("Microsoft.EntityFrameworkCore.", StringComparison.Ordinal) ||
               typeName.StartsWith("MediBridge.Repository.Repositories.", StringComparison.Ordinal);
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
