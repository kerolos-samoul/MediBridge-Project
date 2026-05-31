using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace MediBridge.IntegrationTests.TestHost;

public sealed class WebAppFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Run the test host in Production environment to ensure GlobalExceptionMiddleware
        // returns the non-development safe message during integration tests.
        builder.UseEnvironment("Production");

        // Register test-only controllers (application parts) so test endpoints are
        // discovered by the existing controller routing and run through the normal
        // middleware pipeline.
        builder.ConfigureServices(services =>
        {
            services.AddControllers().PartManager.ApplicationParts.Add(
                new Microsoft.AspNetCore.Mvc.ApplicationParts.AssemblyPart(typeof(TestControllers.RateLimitedTestController).Assembly));
        });
    }
}
