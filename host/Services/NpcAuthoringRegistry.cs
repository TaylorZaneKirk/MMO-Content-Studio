using MMO.ContentStudio.AuthoringHost.Contracts;

namespace MMO.ContentStudio.AuthoringHost.Services;

public sealed class NpcAuthoringRegistry
{
    public const int MinimumTickIntervalMilliseconds = 600;
    public const int MinimumInteractionRangeTiles = 1;
    public const int InitialFootprintWidthTiles = 1;
    public const int InitialFootprintHeightTiles = 1;
    public const int MaxWanderRadiusTiles = 32;
    public const int MaxNotesLength = 4000;
    public const double DefaultIdleChance = 0.15;
    public const double DefaultVisualRenderScale = 0.25;
    public const string DefaultMovementBehavior = "static";
    public const string DefaultInteraction = "talk";

    private static readonly AuthoringOption[] PublicationStates =
    [
        new("Draft", "Draft"),
        new("Published", "Published"),
        new("Disabled", "Disabled")
    ];

    private static readonly AuthoringOption[] MovementBehaviors =
    [
        new("static", "Static"),
        new("random_wander", "Random Wander")
    ];

    private static readonly AuthoringOption[] InteractionTypes =
    [
        new(DefaultInteraction, "Talk")
    ];

    public NpcAuthoringDefaults Defaults { get; } = new(
        DefaultMovementBehavior,
        0,
        MinimumTickIntervalMilliseconds,
        DefaultIdleChance,
        false,
        MinimumInteractionRangeTiles,
        DefaultInteraction,
        InitialFootprintWidthTiles,
        InitialFootprintHeightTiles,
        DefaultVisualRenderScale);

    public IReadOnlyList<AuthoringOption> LoadPublicationStates() => PublicationStates;

    public IReadOnlyList<AuthoringOption> LoadMovementBehaviors() => MovementBehaviors;

    public IReadOnlyList<AuthoringOption> LoadInteractionTypes() => InteractionTypes;

    public IReadOnlyList<AuthoringOption> LoadDialogueReferences() => [];

    public bool CanValidateDialogueReferences => false;

    public NpcSupportedLimits LoadSupportedLimits() => new(
        MinimumTickIntervalMilliseconds,
        MinimumInteractionRangeTiles,
        InitialFootprintWidthTiles,
        InitialFootprintHeightTiles,
        MaxWanderRadiusTiles);
}
