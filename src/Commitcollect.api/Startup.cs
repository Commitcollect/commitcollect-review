using Amazon.CognitoIdentityProvider;
using Amazon.DynamoDBv2;
using Amazon.Extensions.NETCore.Setup;
using Amazon.Lambda;
using Commitcollect.api.Configuration;
using Commitcollect.api.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;


namespace Commitcollect.api;

public class Startup
{
    public Startup(IConfiguration configuration)
    {
        Configuration = configuration;
    }

    public IConfiguration Configuration { get; }

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddControllers();

        // Options
        services.Configure<StravaOptions>(Configuration.GetSection("Strava"));
        services.Configure<OAuthOptions>(Configuration.GetSection("OAuth"));
        services.Configure<AppOptions>(Configuration.GetSection("Frontend"));

        // AWS SDK (uses Lambda role creds automatically)
        services.AddDefaultAWSOptions(Configuration.GetAWSOptions());
        services.AddAWSService<IAmazonLambda>();
        services.AddAWSService<IAmazonDynamoDB>();
        services.AddAWSService<IAmazonCognitoIdentityProvider>();


        // Audit event service 
        services.AddSingleton<AuditEventService>();

        // Session resolver (BFF session model)
        services.AddScoped<ISessionResolver, SessionResolver>();

        services.AddCors(options =>
        {
            options.AddPolicy("Frontend", builder =>
            {
                builder
                    .WithOrigins("https://app.commitcollect.com")
                    .AllowCredentials()
                    .AllowAnyHeader()
                    .AllowAnyMethod();
            });
        });


        services.AddHttpClient();

        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();
    }

    public void Configure(IApplicationBuilder app, IHostEnvironment env)
    {
        if (env.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseRouting();

        app.UseCors("Frontend");   // MUST be between Routing and Endpoints

        app.UseAuthorization();

        app.UseEndpoints(endpoints =>
        {
            endpoints.MapControllers();
        });
    }



}
