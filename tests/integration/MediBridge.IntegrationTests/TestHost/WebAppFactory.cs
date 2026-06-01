using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace MediBridge.IntegrationTests.TestHost;

public sealed class WebAppFactory : ConfiguredWebAppFactory
{
    protected override void ConfigureWebHostCore(IWebHostBuilder builder)
    {
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
