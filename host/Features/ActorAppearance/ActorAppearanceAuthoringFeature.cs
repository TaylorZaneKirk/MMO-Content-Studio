using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Http;
using MMO.ContentStudio.AuthoringHost.Services;

namespace MMO.ContentStudio.AuthoringHost.Features.ActorAppearance;

public static class ActorAppearanceAuthoringFeature
{
    public static IServiceCollection AddActorAppearanceAuthoring(this IServiceCollection services)
    {
        services.AddSingleton<ActorAppearanceCatalogService>();
        services.AddSingleton<ActorRigCalibrationAuthoringService>();
        services.AddSingleton<ActorCalibrationFrameResolver>();
        return services;
    }

    public static IEndpointRouteBuilder MapActorAppearanceAuthoring(
        this IEndpointRouteBuilder endpoints)
    {
        var appearance = endpoints.MapGroup($"{AuthoringApi.RoutePrefix}/actor-appearance");

        appearance.MapGet("/calibrations/{calibrationId}", async (
            HttpContext context,
            string calibrationId,
            ActorRigCalibrationAuthoringService service,
            CancellationToken cancellationToken) =>
            AuthoringHttpResults.FromOperation(
                context,
                await service.LoadAsync(calibrationId, cancellationToken)));

        appearance.MapPut("/calibrations/{calibrationId}", async (
            HttpContext context,
            string calibrationId,
            SaveActorCalibrationRequest request,
            ActorRigCalibrationAuthoringService service,
            CancellationToken cancellationToken) =>
            AuthoringHttpResults.FromOperation(
                context,
                await service.SaveAsync(calibrationId, request, cancellationToken)));

        appearance.MapPost("/calibration-frames", (
            HttpContext context,
            CalibrationFrameRequest request,
            ActorCalibrationFrameResolver resolver) =>
            AuthoringHttpResults.FromOperation(context, resolver.Resolve(request)));

        return endpoints;
    }
}
