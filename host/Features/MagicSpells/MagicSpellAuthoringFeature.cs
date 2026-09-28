// Registers the reusable MagicSpell workspace through the existing authoring host.
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Features.Catalog;
using MMO.ContentStudio.AuthoringHost.Health;
using MMO.ContentStudio.AuthoringHost.Http;
using MMO.ContentStudio.AuthoringHost.Persistence;
using MMO.ContentStudio.AuthoringHost.Services;
namespace MMO.ContentStudio.AuthoringHost.Features.MagicSpells;

public static class MagicSpellAuthoringFeature
{
    public static IServiceCollection AddMagicSpellAuthoring(this IServiceCollection services)
    {
        services.AddSingleton<MagicSpellRepository>();
        services.AddSingleton<MagicSpellAuthoringService>();
        services.AddSingleton<IAuthoringSchemaRequirementProvider, MagicSpellSchemaRequirements>();
        services.AddSingleton<IAuthoringCatalogSectionProvider, MagicSpellCatalogSectionProvider>();
        return services;
    }

    public static IEndpointRouteBuilder MapMagicSpellAuthoring(this IEndpointRouteBuilder endpoints)
    {
        var spells = endpoints.MapGroup($"{AuthoringApi.RoutePrefix}/spells");
        spells.MapGet("", async (HttpContext context, string? search, MagicSpellAuthoringService service, CancellationToken ct) =>
            AuthoringHttpResults.FromOperation(context, await service.ListAsync(search, ct)));
        spells.MapGet("/{definitionId}", async (HttpContext context, string definitionId, MagicSpellAuthoringService service, CancellationToken ct) =>
            AuthoringHttpResults.FromOperation(context, await service.LoadAsync(definitionId, ct)));
        spells.MapPost("/{definitionId}/preview", async (HttpContext context, string definitionId, MagicSpellRequest request, MagicSpellAuthoringService service, CancellationToken ct) =>
            AuthoringHttpResults.FromOperation(context, await service.PreviewAsync(definitionId, request, ct)));
        spells.MapPut("/{definitionId}/draft", async (HttpContext context, string definitionId, MagicSpellRequest request, MagicSpellAuthoringService service, CancellationToken ct) =>
            AuthoringHttpResults.FromOperation(context, await service.MutateAsync(definitionId, "save_draft", request, ct)));
        spells.MapPost("/{definitionId}/save-and-publish", async (HttpContext context, string definitionId, MagicSpellRequest request, MagicSpellAuthoringService service, CancellationToken ct) =>
            AuthoringHttpResults.FromOperation(context, await service.MutateAsync(definitionId, "save_and_publish", request, ct)));
        spells.MapPost("/{definitionId}/publish", async (HttpContext context, string definitionId, MagicSpellRequest request, MagicSpellAuthoringService service, CancellationToken ct) =>
            AuthoringHttpResults.FromOperation(context, await service.MutateAsync(definitionId, "publish", request, ct)));
        spells.MapPost("/{definitionId}/disable", async (HttpContext context, string definitionId, MagicSpellRequest request, MagicSpellAuthoringService service, CancellationToken ct) =>
            AuthoringHttpResults.FromOperation(context, await service.MutateAsync(definitionId, "disable", request, ct)));
        spells.MapPost("/{definitionId}/delete", async (HttpContext context, string definitionId, MagicSpellRequest request, MagicSpellAuthoringService service, CancellationToken ct) =>
            AuthoringHttpResults.FromOperation(context, await service.MutateAsync(definitionId, "delete", request, ct)));
        return endpoints;
    }
}
