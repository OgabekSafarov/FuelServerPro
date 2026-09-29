using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;

namespace FuelServerPro
{
    internal static class Program
    {
        public static async Task Main(string[] args)
        {
            Console.Title = "FuelServerPro - TRK API";

            var builder = WebApplication.CreateBuilder(args);
            string serverUrl = Environment.GetEnvironmentVariable("FUEL_SERVER_URL") ?? "http://localhost:8088";
            builder.WebHost.UseUrls(serverUrl);
            builder.Services.AddOpenApi();
            builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
                policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));
            builder.Services.AddSingleton<MultiTrkManager>();

            await using WebApplication app = builder.Build();
            app.UseCors();
            app.MapOpenApi();
            app.MapTrkApi();

            var manager = app.Services.GetRequiredService<MultiTrkManager>();
            app.Lifetime.ApplicationStopping.Register(manager.Stop);

            Logger.Log($"API server ishga tushdi: {serverUrl}/");
            Logger.Log($"Swagger UI: {serverUrl}/swagger");
            await app.RunAsync();
        }
    }
}
