using System.Reflection;
using System.Xml.Linq;
using Hangfire;
using MediBridge.APIs.Controllers;
using MediBridge.Core.Entities.Profiles;
using MediBridge.Services.Services;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace MediBridge.IntegrationTests;

public sealed class Phase9LayeringBoundaryTests
{
    private static readonly Type[] Phase9ControllerTypes =
    [
        typeof(AdminActivityEnforcementController),
        typeof(AdminActivityJobsController)
    ];

    private static readonly Type[] Phase9ServiceTypes =
    [
        typeof(ActivityScoreService),
        typeof(WeeklyEnforcementService),
        typeof(AdminActivityEnforcementService),
        typeof(ActivityEnforcementJobRunTracker)
    ];

    [Fact]
    public void Phase9CoreProjectAndAssembly_DoNotReferenceEfHttpOrHangfire()
    {
        var project = XDocument.Load(GetRepositoryPath("MediBridge.Core", "MediBridge.Core.csproj"));
        var references = project.Descendants()
            .Where(element => element.Name.LocalName is "PackageReference" or "ProjectReference")
            .Select(element => element.Attribute("Include")?.Value ?? string.Empty)
            .ToArray();
        Assert.DoesNotContain(references, reference =>
            reference.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
            || reference.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal)
            || reference.StartsWith("Hangfire", StringComparison.Ordinal));

        var assemblyReferences = typeof(DoctorProfile).Assembly.GetReferencedAssemblies().Select(reference => reference.Name ?? string.Empty).ToArray();
        Assert.DoesNotContain(assemblyReferences, reference =>
            reference.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
            || reference.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal)
            || reference.StartsWith("Hangfire", StringComparison.Ordinal));
    }

    [Fact]
    public void Phase9Services_DoNotReferenceEfCoreOrHangfireInfrastructure()
    {
        var project = XDocument.Load(GetRepositoryPath("MediBridge.Services", "MediBridge.Services.csproj"));
        var projectViolations = project.Descendants()
            .Where(element => element.Name.LocalName is "PackageReference" or "ProjectReference")
            .Select(element => element.Attribute("Include")?.Value ?? string.Empty)
            .Where(reference => reference.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
                || reference.StartsWith("Hangfire", StringComparison.Ordinal)
                || reference.Contains("MediBridge.Repository", StringComparison.Ordinal))
            .ToArray();
        var memberViolations = Phase9ServiceTypes
            .SelectMany(type => GetReferencedTypes(type), (service, referencedType) => new { service, referencedType })
            .Where(candidate => IsForbiddenServiceType(candidate.referencedType))
            .Select(candidate => $"{candidate.service.FullName} -> {candidate.referencedType.FullName}")
            .ToArray();

        Assert.Empty(projectViolations);
        Assert.Empty(memberViolations);
    }

    [Fact]
    public void Phase9Controllers_AreHttpOnlyAndDependOnServiceInterfaces()
    {
        var dependencyViolations = Phase9ControllerTypes
            .SelectMany(type => type.GetConstructors(), (controller, constructor) => new { controller, constructor })
            .SelectMany(candidate => candidate.constructor.GetParameters(), (candidate, parameter) => new { candidate.controller, parameter.ParameterType })
            .Where(candidate => candidate.ParameterType.IsInterface is false || candidate.ParameterType.Namespace != "MediBridge.Services.Interfaces")
            .Select(candidate => $"{candidate.controller.FullName} -> {candidate.ParameterType.FullName}")
            .ToArray();
        var forbiddenTypeReferences = Phase9ControllerTypes
            .SelectMany(type => GetReferencedTypes(type), (controller, referencedType) => new { controller, referencedType })
            .Where(candidate => IsForbiddenControllerType(candidate.referencedType))
            .Select(candidate => $"{candidate.controller.FullName} -> {candidate.referencedType.FullName}")
            .ToArray();

        Assert.Empty(dependencyViolations);
        Assert.Empty(forbiddenTypeReferences);
        Assert.All(Phase9ControllerTypes, type => Assert.NotNull(type.GetCustomAttribute<ApiControllerAttribute>()));
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

        foreach (var field in type.GetFields(flags))
        {
            if (!field.Name.Contains("BackingField", StringComparison.Ordinal))
            {
                yield return field.FieldType;
            }
        }

        foreach (var property in type.GetProperties(flags))
        {
            yield return property.PropertyType;
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

    private static bool IsForbiddenServiceType(Type type)
    {
        var name = GetTypeName(type);
        return name.StartsWith("Microsoft.EntityFrameworkCore.", StringComparison.Ordinal)
            || name.StartsWith("Hangfire.", StringComparison.Ordinal);
    }

    private static bool IsForbiddenControllerType(Type type)
    {
        var name = GetTypeName(type);
        return name.StartsWith("Microsoft.EntityFrameworkCore.", StringComparison.Ordinal)
            || name.StartsWith("Hangfire.", StringComparison.Ordinal)
            || name.Contains("ActivityScoreCalculator", StringComparison.Ordinal)
            || name.Contains("WeeklyEnforcementPolicy", StringComparison.Ordinal);
    }

    private static string GetTypeName(Type type)
    {
        return type.IsGenericType
            ? type.GetGenericTypeDefinition().FullName ?? string.Empty
            : type.FullName ?? string.Empty;
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
