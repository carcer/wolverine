using System.ComponentModel.DataAnnotations;
using Alba;
using IntegrationTests;
using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Frames;
using Marten;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Wolverine.Runtime;
using WolverineWebApi;

namespace Wolverine.Http.Tests.Bugs;

public class Requried_with_HttpPolicy
{
    [Fact]
    public async Task try_endpoint_hit()
    {
        var builder = WebApplication.CreateBuilder();

        builder.Host.UseWolverine(opts =>
        {
            opts.Discovery.DisableConventionalDiscovery();
            opts.Discovery.IgnoreAssembly(typeof(OpenApiEndpoints).Assembly);
            opts.Discovery.IncludeAssembly(GetType().Assembly);

            opts.Services.AddMarten(Servers.PostgresConnectionString);
        });
        
        builder.Services.AddWolverineHttp();

        using var host = await AlbaHost.For(builder, app =>
        {
            app.MapWolverineEndpoints(x => x.AddPolicy<ThingPolicy>());
        });
    }
}

public record Something(string Vision);

public class SomethingEndpoint
{
    [Tags("Thingy")]
    [ProducesResponseType(204)]
    [WolverinePost("/api/thingy")]
    public static  async Task<IResult> Post(Something c, [Required] Thingy? thingy, CancellationToken ct)
    {
        if (thingy is null) return Results.NotFound();

        return Results.NoContent();
    }
}

public class ThingPolicy : IHttpPolicy
{
    public void Apply(IReadOnlyList<HttpChain> chains, GenerationRules rules, IServiceContainer container)
    {
        foreach (var chain in chains)
        {
            var serviceDependencies = chain.ServiceDependencies(container, Type.EmptyTypes).ToArray();
            if (serviceDependencies.Contains(typeof(Thingy)))
            {
                chain.Middleware.Insert(0, new MethodCall(typeof(ThingyMiddleware), nameof(ThingyMiddleware.LoadAsync)));
            }
        }
    }
}

public static class ThingyMiddleware
{
    public static Thingy LoadAsync()
    {
        return new Thingy();
    }
}

public class Thingy;