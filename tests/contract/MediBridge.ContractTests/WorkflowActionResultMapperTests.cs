using MediBridge.APIs.Contracts;
using MediBridge.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace MediBridge.ContractTests;

public sealed class WorkflowActionResultMapperTests
{
    [Theory]
    [MemberData(nameof(WorkflowExceptionCases))]
    public void ToActionResult_ReturnsDirectObjectResultWithStandardEnvelope(Exception exception, int expectedStatusCode, string expectedMessage)
    {
        var result = WorkflowActionResultMapper.ToActionResult(exception);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(expectedStatusCode, objectResult.StatusCode);
        var envelope = Assert.IsType<ApiEnvelope<object?>>(objectResult.Value);
        Assert.Equal(expectedStatusCode, envelope.Code);
        Assert.Equal(expectedMessage, envelope.Message);
        Assert.Null(envelope.Data);
    }

    public static TheoryData<Exception, int, string> WorkflowExceptionCases => new()
    {
        { new WorkflowValidationException(), StatusCodes.Status400BadRequest, "Validation failed." },
        { new WorkflowUnauthorizedException(), StatusCodes.Status401Unauthorized, "Authentication denied." },
        { new WorkflowForbiddenException(), StatusCodes.Status403Forbidden, "Forbidden." },
        { new WorkflowNotFoundException(), StatusCodes.Status404NotFound, "Not found." },
        { new WorkflowConflictException(), StatusCodes.Status409Conflict, "Conflict." }
    };
}
