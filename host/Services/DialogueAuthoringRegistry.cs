using MMO.ContentStudio.AuthoringHost.Contracts;

namespace MMO.ContentStudio.AuthoringHost.Services;

public sealed class DialogueAuthoringRegistry
{
    public const int CurrentSchemaVersion = 1;
    public const int MaxIdentifierLength = 100;
    public const int MaxDisplayNameLength = 100;
    public const int MaxTextLength = 4000;
    public const int MaxNotesLength = 4000;
    public const int MaxNodes = 512;
    public const int MaxChoicesPerNode = 32;
    public const int MaxOrderValue = 10000;
    public const int MaxPlaythroughSteps = 128;
    public const string DefaultEntryId = "default";
    public const string DefaultStartNodeId = "start";
    public const string SpeakerTextNodeType = "speaker_text";
    public const string PlayerChoiceNodeType = "player_choice";
    public const string EndNodeType = "end";

    private static readonly AuthoringOption[] PublicationStates =
    [
        new("Draft", "Draft"),
        new("Published", "Published"),
        new("Disabled", "Disabled")
    ];

    private static readonly AuthoringOption[] NodeTypes =
    [
        new(SpeakerTextNodeType, "Speaker Text"),
        new(PlayerChoiceNodeType, "Player Choice"),
        new(EndNodeType, "End")
    ];

    public DialogueAuthoringDefaults Defaults { get; } = new(
        CurrentSchemaVersion,
        DefaultEntryId,
        DefaultStartNodeId,
        SpeakerTextNodeType,
        true,
        0,
        0);

    public IReadOnlyList<AuthoringOption> LoadPublicationStates() => PublicationStates;

    public IReadOnlyList<AuthoringOption> LoadNodeTypes() => NodeTypes;

    public IReadOnlyList<AuthoringOption> LoadConditionTypes() => [];

    public IReadOnlyList<AuthoringOption> LoadEffectTypes() => [];

    public DialogueSupportedLimits LoadSupportedLimits() => new(
        MaxIdentifierLength,
        MaxDisplayNameLength,
        MaxTextLength,
        MaxNotesLength,
        MaxNodes,
        MaxChoicesPerNode,
        MaxPlaythroughSteps);

    public DialogueOperationCapabilities LoadCapabilities() => new(
        true,
        false,
        false,
        false,
        false,
        false,
        false,
        false);
}
