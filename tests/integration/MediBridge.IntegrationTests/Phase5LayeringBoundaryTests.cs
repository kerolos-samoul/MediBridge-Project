using System.Reflection;
using System.Xml.Linq;
using MediBridge.APIs.Controllers;
using MediBridge.Services.Services;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class Phase5LayeringBoundaryTests
{
    private static readonly Type[] Phase5ControllerTypes =
    [
        typeof(CompanyDoctorsController),
        typeof(CompanyCampaignsController),
        typeof(CompanyWalletController)
    ];

    private static readonly Type[] Phase5ServiceTypes =
    [
        typeof(CompanyDoctorSearchService),
        typeof(CampaignWorkflowService),
        typeof(CompanyWalletService)
    ];

    [Fact]
    public void Phase5Controllers_DoNotReferenceEfCoreTypes()
    {
        var violations = Phase5ControllerTypes
            .SelectMany(type => GetReferencedTypes(type), (controller, referencedType) => new { controller, referencedType })
            .Where(candidate => IsEfCoreType(candidate.referencedType))
            .Select(candidate => $"{candidate.controller.FullName} -> {candidate.referencedType.FullName}")
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public void Phase5Controllers_KeepHttpActionsDelegatedToServiceInterfaces()
    {
        var violations = Phase5ControllerTypes
            .SelectMany(type => type.GetConstructors(), (controller, constructor) => new { controller, constructor })
            .SelectMany(candidate => candidate.constructor.GetParameters(), (candidate, parameter) => new
            {
                candidate.controller,
                Dependency = parameter.ParameterType
            })
            .Where(candidate => candidate.Dependency.IsInterface is false || candidate.Dependency.Namespace != "MediBridge.Services.Interfaces")
            .Select(candidate => $"{candidate.controller.FullName} -> {candidate.Dependency.FullName}")
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public void Phase5Services_ProjectAndAssemblyDoNotReferenceEfCoreInfrastructure()
    {
        var project = XDocument.Load(GetRepositoryPath("MediBridge.Services", "MediBridge.Services.csproj"));
        var projectViolations = project.Descendants()
            .Where(element => element.Name.LocalName is "PackageReference" or "ProjectReference")
            .Select(element => element.Attribute("Include")?.Value ?? string.Empty)
            .Where(include => include.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) || include.Contains("MediBridge.Repository", StringComparison.Ordinal))
            .ToArray();
        var assemblyViolations = typeof(CompanyWalletService).Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(name => name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) || name.Equals("MediBridge.Repository", StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(projectViolations);
        Assert.Empty(assemblyViolations);
    }

    [Fact]
    public void Phase5Services_DoNotReferenceEfCoreTypesInMembersOrLocals()
    {
        var violations = Phase5ServiceTypes
            .SelectMany(type => GetReferencedTypes(type), (service, referencedType) => new { service, referencedType })
            .Where(candidate => IsEfCoreType(candidate.referencedType))
            .Select(candidate => $"{candidate.service.FullName} -> {candidate.referencedType.FullName}")
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
            if (!field.Name.Contains("BackingField", StringComparison.Ordinal))
            {
                yield return field.FieldType;
            }
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

    private static bool IsEfCoreType(Type type)
    {
        var typeName = type.IsGenericType
            ? type.GetGenericTypeDefinition().FullName ?? string.Empty
            : type.FullName ?? string.Empty;
        return typeName.StartsWith("Microsoft.EntityFrameworkCore.", StringComparison.Ordinal);
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
