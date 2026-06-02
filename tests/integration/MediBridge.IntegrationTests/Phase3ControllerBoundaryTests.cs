using System.Reflection;
using MediBridge.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace MediBridge.IntegrationTests;

public class Phase3ControllerBoundaryTests
{
    [Fact]
    public void Api_Should_Not_Contain_Phase3_Public_Or_Diagnostic_Controllers()
    {
        var controllerNames = GetControllerTypes()
            .Select(type => type.Name)
            .ToArray();

        Assert.DoesNotContain("Phase3DiagnosticsController", controllerNames);
        Assert.DoesNotContain(controllerNames, name =>
            name.StartsWith("Phase3", StringComparison.Ordinal) ||
            name.Contains("Diagnostics", StringComparison.Ordinal));
    }

    [Fact]
    public void Controllers_Should_Not_Inject_Phase3_Repositories_UnitOfWork_Or_DbContext()
    {
        var violations = GetControllerTypes()
            .SelectMany(type => type.GetConstructors(), (controller, constructor) => new { controller, constructor })
            .SelectMany(candidate => candidate.constructor.GetParameters(), (candidate, parameter) => new
            {
                candidate.controller,
                Dependency = parameter.ParameterType
            })
            .Where(candidate => IsForbiddenControllerDependency(candidate.Dependency))
            .Select(candidate => $"{candidate.controller.FullName} -> {candidate.Dependency.FullName}")
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public void Controllers_Should_Not_Use_Persistence_Types_In_Members_Or_Action_Bodies()
    {
        var violations = GetControllerTypes()
            .SelectMany(type => GetReferencedTypes(type), (controller, referencedType) => new { controller, referencedType })
            .Where(candidate => IsForbiddenControllerDependency(candidate.referencedType))
            .Select(candidate => $"{candidate.controller.FullName} -> {candidate.referencedType.FullName}")
            .ToArray();

        Assert.Empty(violations);
    }

    private static Type[] GetControllerTypes()
    {
        return typeof(MediBridge.APIs.Controllers.AuthController).Assembly.GetTypes()
            .Where(type => type.IsClass && !type.IsAbstract && typeof(ControllerBase).IsAssignableFrom(type))
            .ToArray();
    }

    private static IEnumerable<Type> GetReferencedTypes(Type type)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

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

    private static bool IsForbiddenControllerDependency(Type type)
    {
        if (type == typeof(IDomainUnitOfWork))
        {
            return true;
        }

        var typeName = GetNonGenericTypeName(type);
        return typeName.Equals("MediBridge.Repository.Data.MediBridgeDbContext", StringComparison.Ordinal) ||
               typeName.StartsWith("Microsoft.EntityFrameworkCore.", StringComparison.Ordinal) ||
               typeName.StartsWith("MediBridge.Repository.Repositories.", StringComparison.Ordinal) ||
               (typeName.StartsWith("MediBridge.Core.Interfaces.", StringComparison.Ordinal) &&
                typeName.EndsWith("Repository", StringComparison.Ordinal));
    }

    private static string GetNonGenericTypeName(Type type)
    {
        if (type.IsGenericType)
        {
            return type.GetGenericTypeDefinition().FullName ?? string.Empty;
        }

        return type.FullName ?? string.Empty;
    }
}
