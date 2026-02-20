using Commitcollect.api.Configuration;
using Amazon.CognitoIdentityProvider;
using Amazon.DynamoDBv2;
using Amazon.Extensions.NETCore.Setup;
using Amazon.Lambda.AspNetCoreServer.Hosting;

namespace Commitcollect.api
{
    public class Program
    {
        public static void Main(string[] args)
        {


            var builder = WebApplication.CreateBuilder(args);

          
     

            // Logging first so startup messages appear in CloudWatch on cold start

            // Local dev secrets (ignored in Lambda unless you somehow ship them, which you shouldn't)
            builder.Configuration.AddUserSecrets<Program>();

            // Options
            builder.Services.Configure<StravaOptions>(
                builder.Configuration.GetSection("Strava"));

            // MVC
            builder.Services.AddControllers();

            // AWS Lambda hosting + AWS SDK DI
            builder.Services.AddAWSLambdaHosting(LambdaEventSource.HttpApi);
            builder.Services.AddDefaultAWSOptions(builder.Configuration.GetAWSOptions());

            // ✅ Register AWS SDK clients used by your controllers/services
            builder.Services.AddAWSService<IAmazonDynamoDB>();
            builder.Services.AddAWSService<IAmazonCognitoIdentityProvider>();

            Console.WriteLine("BOOT: REGISTERED COGNITO + DDB");

            // FORCE fail if not registered
            if (!builder.Services.Any(sd => sd.ServiceType == typeof(IAmazonCognitoIdentityProvider)))
            {
                throw new Exception("COGNITO NOT REGISTERED IN SERVICE COLLECTION");
            }

            // Other services
            builder.Services.AddHttpClient();

            // Swagger (dev only UI below)
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            var app = builder.Build();

            // Safe DI sanity checks using the real container (no BuildServiceProvider)
            var ddb = app.Services.GetService<IAmazonDynamoDB>();
            Console.WriteLine($"BOOT: Dynamo service resolved = {ddb != null}");

            var cognito = app.Services.GetService<IAmazonCognitoIdentityProvider>();
            Console.WriteLine($"BOOT: Cognito service resolved = {cognito != null}");

            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseHttpsRedirection();
            app.UseAuthorization();

            app.MapControllers();
            app.Run();
        }
    }
}
