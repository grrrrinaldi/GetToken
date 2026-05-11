using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TC.ConnectionBroker.Api;
using TC.ConnectionBroker.Extensions.DependencyInjection;
using TC.DestinationManagement.Queries.API.Client;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (!TryGetEnvironment(args, out var environment))
        {
            Console.Error.WriteLine("Usage: GetToken [prod|stg]");
            Console.Error.WriteLine("  no argument: QA");
            Console.Error.WriteLine("  prod: Production");
            Console.Error.WriteLine("  stg: PreProduction");
            return 1;
        }

        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", environment, EnvironmentVariableTarget.Process);
        Environment.SetEnvironmentVariable("CONNECTIONBROKERENVIRONMENT", environment, EnvironmentVariableTarget.Process);

        var services = new ServiceCollection();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ASPNETCORE_ENVIRONMENT"] = environment,
                ["CONNECTIONBROKERENVIRONMENT"] = environment
            })
            .Build();

        services.AddSingleton<IConfiguration>(configuration);
        services.AddApiConnectionBroker<ApiConnectionBroker>();
        services.AddSingleton<IHttpContextAccessor, DummyHttpContextAccessor>();
        services.AddDestinationQueriesApiClient();

        await using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IDestinationQueriesApiClient>();

        var result = await client.PingAsync();

        if (result.IsFailed)
        {
            Console.Error.WriteLine("Ping failed:");
            foreach (var error in result.Errors)
            {
                Console.Error.WriteLine($"- {error.Message}");
            }

            return 1;
        }

        using var response = result.Value;
        var authHeader = response.RequestMessage?.Headers.Authorization?.ToString();
        var token = response.RequestMessage?.Headers.Authorization?.Parameter;

        if (string.IsNullOrWhiteSpace(token))
        {
            Console.Error.WriteLine("No Authorization header was found on the request.");
            return 1;
        }

        Console.WriteLine(authHeader);
        await CopyToClipboardAsync(token);
        Console.WriteLine("Copied to clipboard.");
        return 0;
    }

    private static bool TryGetEnvironment(string[] args, out string environment)
    {
        environment = "QA";

        if (args.Length == 0)
        {
            return true;
        }

        if (args.Length != 1)
        {
            return false;
        }

        environment = args[0].ToLowerInvariant() switch
        {
            "prod" => "Live",
            "stg" => "Preproduction",
            _ => string.Empty
        };

        return environment.Length > 0;
    }

    private static async Task CopyToClipboardAsync(string text)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c clip",
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        await process.StandardInput.WriteAsync(text);
        process.StandardInput.Close();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"clip.exe exited with code {process.ExitCode}.");
        }
    }

    private sealed class DummyHttpContextAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; } = new DefaultHttpContext();
    }
}
