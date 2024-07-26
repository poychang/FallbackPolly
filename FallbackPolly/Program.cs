using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Polly;
using System.Net;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Configuration.AddCommandLine(args);
builder.Logging.SetMinimumLevel(LogLevel.Debug);
builder.Services.AddHttpClient();
builder.Services.AddHostedService<PolicyWorker>();

using IHost host = builder.Build();

await host.RunAsync();

// Application code should start here.

public class PolicyWorker(IHttpClientFactory factory, ILogger<PolicyWorker> logger) : IHostedService
{
    // REF: Retry and fallback policies in C# with Polly
    // https://blog.duijzer.com/posts/polly-refit/
    /* Retry Flowchart
     **********************************
     *                                 
     *                   ┌┬─────────┬┐ 
     *                   ││ calling ││ 
     *        ┌─────────►├│ origin  ││ 
     *        │          ││ url     ││ 
     *        │          └┴────┬────┴┘ 
     *     no, retry           │       
     *        │                ▼       
     *        *                *       
     *       * *              * *      
     *      *   *            *   *     
     *     *     *          *     *    
     *    * more  *   yes  *       *   
     *   * then 3  *◄─────* timeout *  
     *    * time? *        *   ?   *   
     *     *     *          *     *    
     *      *   *            *   *     
     *       * *              * *      
     *        *                *       
     *        │                │       
     *    yes, fallback        │ no    
     *        │                │       
     *        ▼                ▼       
     *  ┌┬─────────┬┐    ┌┬─────────┬┐ 
     *  ││ calling ││    ││ output  ││ 
     *  ││ fallback│┼───►├│ response││ 
     *  ││ url     ││    ││         ││ 
     *  └┴─────────┴┘    └┴─────────┴┘ 
     *                                 
     * ********************************/

    private readonly HttpClient _httpClient = factory.CreateClient();
    private readonly ILogger<PolicyWorker> _logger = logger;

    private async Task DoRetryAsync()
    {
        var fallbackUrl = "http://blog.poychang.net/apps/mock-json-data/json-1.json";

        var retryPolicy = Policy<string>
            .Handle<HttpRequestException>(ex => ex.StatusCode != HttpStatusCode.OK)
            .RetryAsync(3, async (exception, retryCount) => await Task.Delay(500));

        var fallbackPolicy = Policy<string>
            .Handle<Exception>()
            .FallbackAsync(async (cancellationToken) =>
            {
                _logger.LogDebug("##info Calling fallback url.");
                return await _httpClient.GetAsync(fallbackUrl).Result.Content.ReadAsStringAsync();
            });

        var result = await fallbackPolicy
            .WrapAsync(retryPolicy)
            .ExecuteAsync(async () =>
            {
                _logger.LogDebug("Calling not exist url. Here will fail.");
                return await _httpClient.GetAsync("https://not-exist-url/").Result.Content.ReadAsStringAsync();
            });

        Console.WriteLine(result);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        DoRetryAsync().GetAwaiter().GetResult();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

