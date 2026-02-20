using Amazon.Lambda.AspNetCoreServer;
using Microsoft.AspNetCore.Hosting;

namespace Commitcollect.api;

public class LambdaEntryPoint : APIGatewayHttpApiV2ProxyFunction
{
    protected override void Init(IWebHostBuilder builder)
    {
        builder.UseStartup<Startup>();
    }
}
