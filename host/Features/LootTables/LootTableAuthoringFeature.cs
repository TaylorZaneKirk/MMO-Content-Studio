using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Features.Catalog;
using MMO.ContentStudio.AuthoringHost.Health;
using MMO.ContentStudio.AuthoringHost.Http;
using MMO.ContentStudio.AuthoringHost.Persistence;
using MMO.ContentStudio.AuthoringHost.Services;

namespace MMO.ContentStudio.AuthoringHost.Features.LootTables;

public static class LootTableAuthoringFeature
{
    public static IServiceCollection AddLootTableAuthoring(this IServiceCollection services)
    {
        services.AddSingleton<ILootTableRepository, LootTableRepository>();
        services.AddSingleton<LootTableDefinitionValidator>();
        services.AddSingleton<LootTableExpectedValueCalculator>();
        services.AddSingleton<LootTableAuthoringService>();
        services.AddSingleton<IAuthoringSchemaRequirementProvider, LootTableSchemaRequirements>();
        services.AddSingleton<IAuthoringCatalogSectionProvider, LootTableCatalogSectionProvider>();
        return services;
    }

    public static IEndpointRouteBuilder MapLootTableAuthoring(
        this IEndpointRouteBuilder endpoints)
    {
        var lootTables = endpoints.MapGroup($"{AuthoringApi.RoutePrefix}/loot-tables");

        lootTables.MapGet("/options", async (
            HttpContext context,
            LootTableAuthoringService service,
            CancellationToken cancellationToken) =>
            AuthoringHttpResults.FromOperation(
                context,
                await service.LoadOptionsAsync(cancellationToken)));

        lootTables.MapGet(string.Empty, async (
            HttpContext context,
            string? search,
            LootTableAuthoringService service,
            CancellationToken cancellationToken) =>
            AuthoringHttpResults.FromOperation(
                context,
                await service.ListAsync(search, cancellationToken)));

        lootTables.MapGet("/{lootTableId}", async (
            HttpContext context,
            string lootTableId,
            LootTableAuthoringService service,
            CancellationToken cancellationToken) =>
            AuthoringHttpResults.FromOperation(
                context,
                await service.LoadAsync(lootTableId, cancellationToken)));

        lootTables.MapPost("/{lootTableId}/preview", async (
            HttpContext context,
            string lootTableId,
            LootTablePreviewRequest request,
            LootTableAuthoringService service,
            CancellationToken cancellationToken) =>
            AuthoringHttpResults.FromOperation(
                context,
                await service.PreviewAsync(lootTableId, request, cancellationToken)));

        lootTables.MapPut("/{lootTableId}/draft", async (
            HttpContext context,
            string lootTableId,
            SaveLootTableDraftRequest request,
            LootTableAuthoringService service,
            CancellationToken cancellationToken) =>
            AuthoringHttpResults.FromOperation(
                context,
                await service.SaveDraftAsync(lootTableId, request, cancellationToken)));

        lootTables.MapPost("/{lootTableId}/publish", async (
            HttpContext context,
            string lootTableId,
            LootTablePublicationRequest request,
            LootTableAuthoringService service,
            CancellationToken cancellationToken) =>
            AuthoringHttpResults.FromOperation(
                context,
                await service.PublishAsync(lootTableId, request, cancellationToken)));

        lootTables.MapPost("/{lootTableId}/disable", async (
            HttpContext context,
            string lootTableId,
            LootTablePublicationRequest request,
            LootTableAuthoringService service,
            CancellationToken cancellationToken) =>
            AuthoringHttpResults.FromOperation(
                context,
                await service.DisableAsync(lootTableId, request, cancellationToken)));

        lootTables.MapPost("/{lootTableId}/delete", async (
            HttpContext context,
            string lootTableId,
            DeleteMutationRequest request,
            LootTableAuthoringService service,
            CancellationToken cancellationToken) =>
            AuthoringHttpResults.FromOperation(
                context,
                await service.DeleteAsync(lootTableId, request, cancellationToken)));

        return endpoints;
    }
}
