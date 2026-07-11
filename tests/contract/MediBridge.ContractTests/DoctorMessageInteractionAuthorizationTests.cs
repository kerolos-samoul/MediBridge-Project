using System.Reflection;
using MediBridge.APIs.Contracts;
using MediBridge.APIs.Controllers;
using MediBridge.APIs.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace MediBridge.ContractTests;

public sealed class DoctorMessageInteractionAuthorizationTests
{
    [Fact]
    public void DoctorMessagesController_RequiresPhase7DoctorMessagesPolicy()
    {
        var authorize = typeof(DoctorMessagesController).GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);
        Assert.Equal(AuthorizationPolicies.Phase7DoctorMessagesRead, authorize.Policy);
    }

    [Theory]
    [InlineData(nameof(DoctorMessagesController.MarkRead))]
    [InlineData(nameof(DoctorMessagesController.Interact))]
    public void Phase8Endpoints_DocumentUnauthorizedAndForbiddenEnvelopes(string methodName)
    {
        var method = typeof(DoctorMessagesController).GetMethod(methodName)!;
        var responseTypes = method
            .GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Select(attribute => (attribute.StatusCode, attribute.Type))
            .ToDictionary(pair => pair.StatusCode, pair => pair.Type);

        Assert.Equal(typeof(ApiEnvelope<object>), responseTypes[401]);
        Assert.Equal(typeof(ApiEnvelope<object>), responseTypes[403]);
    }
}
