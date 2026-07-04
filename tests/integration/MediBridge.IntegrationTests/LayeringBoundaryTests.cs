using System.Linq;
using System.Reflection;
using MediBridge.APIs.Controllers;
using MediBridge.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace MediBridge.IntegrationTests;

public class LayeringBoundaryTests
{
    [Fact]
    public void Controllers_Should_DependOnlyOn_ServiceInterfaces()
    {
        var asm = typeof(MediBridge.APIs.Controllers.WeatherForecastController).Assembly;
        var controllerTypes = asm.GetTypes().Where(t => t.IsClass && t.Name.EndsWith("Controller")).ToArray();

        foreach (var ctrl in controllerTypes)
        {
            var ctor = ctrl.GetConstructors().FirstOrDefault();
            Assert.NotNull(ctor);
            foreach (var param in ctor!.GetParameters())
            {
                var ns = param.ParameterType.Namespace ?? string.Empty;
                Assert.True(param.ParameterType.IsInterface, $"Controller {ctrl.Name} depends on concrete type {param.ParameterType.FullName}");
                Assert.True(
                    ns == "MediBridge.Services.Interfaces" || param.ParameterType == typeof(ICurrentUserContext),
                    $"Controller {ctrl.Name} has disallowed interface dependency {param.ParameterType.FullName}");
            }

            foreach (var field in ctrl.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
            {
                if (field.Name.Contains("BackingField", StringComparison.Ordinal))
                {
                    continue;
                }

                Assert.True(field.FieldType.IsInterface, $"Controller {ctrl.Name} has concrete field dependency {field.FieldType.FullName}");
                Assert.True(
                    field.FieldType.Namespace == "MediBridge.Services.Interfaces" || field.FieldType == typeof(ICurrentUserContext),
                    $"Controller {ctrl.Name} has disallowed interface field {field.FieldType.FullName}");
            }

            foreach (var action in ctrl.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                         .Where(method => method.GetCustomAttributes<HttpMethodAttribute>().Any()))
            {
                var bodyText = action.GetMethodBody()?.LocalVariables
                    .Select(local => local.LocalType.FullName ?? string.Empty)
                    .ToArray() ?? Array.Empty<string>();

                Assert.DoesNotContain(bodyText, name => name.StartsWith("MediBridge.Repository", StringComparison.Ordinal));
                Assert.DoesNotContain(bodyText, name => name.Contains("IServiceProvider", StringComparison.Ordinal));
            }
        }
    }

    [Theory]
    [InlineData(typeof(AuthController), typeof(MediBridge.Services.Interfaces.IAuthService))]
    [InlineData(typeof(AdminAccountsController), typeof(MediBridge.Services.Interfaces.IAdminAccountService))]
    public void IdentityControllers_Should_DeclareOnlyServiceInterfaceConstructorDependencies(Type controllerType, Type expectedServiceInterface)
    {
        var constructors = controllerType.GetConstructors();
        var constructor = Assert.Single(constructors);
        var parameter = Assert.Single(constructor.GetParameters());

        Assert.Equal(expectedServiceInterface, parameter.ParameterType);
        Assert.Equal("MediBridge.Services.Interfaces", parameter.ParameterType.Namespace);
        Assert.True(parameter.ParameterType.IsInterface);
    }
}
