using MMO.ContentStudio.AuthoringHost.Contracts;

namespace MMO.ContentStudio.AuthoringHost.Services;

public sealed class QuestAuthoringRegistry
{
    public const int CurrentSchemaVersion = 1;
    public const int MaxIdentifierLength = 100;
    public const int MaxDisplayNameLength = 100;
    public const int MaxSteps = 128;
    public const int MaxTransitions = 256;
    public const int MaxOrderValue = 10000;

    private static readonly AuthoringOption[] PublicationStates =
    [
        new("Draft", "Draft"),
        new("Published", "Published"),
        new("Disabled", "Disabled")
    ];

    private static readonly AuthoringOption[] QuestStatuses =
    [
        new("not_started", "Not Started"),
        new("active", "Active"),
        new("completed", "Completed")
    ];

    public QuestAuthoringDefaults Defaults { get; } = new(
        CurrentSchemaVersion,
        "first",
        "accept");

    public IReadOnlyList<AuthoringOption> LoadPublicationStates() => PublicationStates;

    public IReadOnlyList<AuthoringOption> LoadQuestStatuses() => QuestStatuses;

    public QuestSupportedLimits LoadSupportedLimits() => new(
        MaxIdentifierLength,
        MaxDisplayNameLength,
        MaxSteps,
        MaxTransitions);

    public QuestOperationCapabilities LoadCapabilities() => new(
        true,
        false,
        false,
        false,
        false,
        false);
}
