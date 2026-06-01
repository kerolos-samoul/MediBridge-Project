using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace MediBridge.ContractTests.TestHost;

public sealed class ForceHttpsStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                context.Request.Scheme = "https";
                await nextMiddleware();
            });

            next(app);
        };
    }
}