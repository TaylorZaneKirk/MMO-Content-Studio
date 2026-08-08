using System.Text.RegularExpressions;
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Persistence;

namespace MMO.ContentStudio.AuthoringHost.Services;

public sealed partial class NpcDefinitionValidator
{
    private static readonly HashSet<string> DraftBlockingValidationCodes = new(StringComparer.Ordinal)
    {
        "npc_invalid_definition",
        "npc_invalid_visual_path",
        "npc_invalid_visual_dimensions",
        "npc_invalid_visual_anchor",
        "npc_invalid_visual_render_scale",
        "npc_unsupported_footprint",
        "npc_unsupported_movement_behavior",
        "npc_invalid_wander_radius",
        "npc_invalid_tick_interval",
        "npc_invalid_idle_chance",
        "npc_unsupported_interaction",
        "npc_invalid_interaction_range",
        "npc_invalid_dialogue_reference",
        "npc_invalid_notes"
    };

    private readonly ItemAssetService _assetService;
    private readonly NpcDialogueReferenceProvider _dialogueReferences;
    private readonly CompositeActorVisualValidator? _compositeVisualValidator;

    public NpcDefinitionValidator(
        ItemAssetService assetService,
        NpcDialogueReferenceProvider dialogueReferences)
        : this(assetService, dialogueReferences, null)
    {
    }

    public NpcDefinitionValidator(
        ItemAssetService assetService,
        NpcDialogueReferenceProvider dialogueReferences,
        CompositeActorVisualValidator? compositeVisualValidator)
    {
        _assetService = assetService;
        _dialogueReferences = dialogueReferences;
        _compositeVisualValidator = compositeVisualValidator;
    }

    public async Task<NpcValidationOutcome> ValidateAsync(
        string npcDefinitionId,
        NpcDraft draft,
        NpcDefinitionRecord? existing,
        bool forPublication,
        CancellationToken cancellationToken = default)
    {
        var messages = new List<ApiError>();
        ValidateIdentity(npcDefinitionId, draft, existing, messages);
        ValidateVisuals(draft, messages, forPublication);
        if (draft.VisualMode == "composite_rig" && _compositeVisualValidator is not null)
        {
            await _compositeVisualValidator.ValidateAsync(
                draft.CompositeVisual,
                "npc",
                messages,
                cancellationToken);
        }
        ValidateMovement(draft, messages);
        await ValidateInteractionAsync(draft, messages, forPublication, cancellationToken);
        ValidateNotes(draft, messages);

        if (existing is not null && existing.PublicationState == "Published" && !forPublication)
        {
            messages.Add(new ApiError(
                "save_will_unpublish_npc",
                "Saving this published NPC definition changes its authoring lifecycle state, but runtime export remains a later T5 handoff.",
                ValidationSeverity.Warning,
                "publication_state"));
        }

        var asset = draft.VisualMode == "composite_rig"
            ? new ItemAssetResolution(true, null, null)
            : _assetService.ResolveGameAssetPng(draft.VisualTexturePath, "NPC visual texture");
        var hasErrors = messages.Any(message => message.Severity == ValidationSeverity.Error);
        var hasDraftBlockingErrors = messages.Any(IsDraftBlocking);
        return new NpcValidationOutcome(
            !hasDraftBlockingErrors,
            !hasErrors && asset.Exists,
            messages,
            asset.FilePath);
    }

    public static bool IsDraftBlocking(ApiError message) =>
        message.Severity == ValidationSeverity.Error
        && DraftBlockingValidationCodes.Contains(message.Code);

    public static void ValidateIdentity(
        string npcDefinitionId,
        NpcDraft draft,
        NpcDefinitionRecord? existing,
        ICollection<ApiError> messages)
    {
        if (string.IsNullOrWhiteSpace(npcDefinitionId)
            || npcDefinitionId.Length > 100
            || !StableIdentifierRegex().IsMatch(npcDefinitionId))
        {
            messages.Add(new ApiError(
                "npc_invalid_definition",
                "NPC definition IDs must be 1-100 lowercase letters, numbers, or single underscores between segments.",
                ValidationSeverity.Error,
                "npc_definition_id"));
        }

        if (existing is not null
            && !string.Equals(existing.NpcDefinitionId, npcDefinitionId, StringComparison.Ordinal))
        {
            messages.Add(new ApiError(
                "npc_invalid_definition",
                "NPC definition identity is immutable after creation.",
                ValidationSeverity.Error,
                "npc_definition_id"));
        }

        if (draft.DisplayName.Length is < 1 or > 100 || draft.DisplayName.Any(char.IsControl))
        {
            messages.Add(new ApiError(
                "npc_invalid_definition",
                "Display name must contain 1-100 printable characters.",
                ValidationSeverity.Error,
                "display_name"));
        }
    }

    public void ValidateVisuals(
        NpcDraft draft,
        ICollection<ApiError> messages,
        bool forPublication)
    {
        if (draft.VisualMode == "composite_rig")
        {
            ValidateCompositeVisual(draft.CompositeVisual, messages);
            return;
        }
        var asset = _assetService.ResolveGameAssetPng(draft.VisualTexturePath, "NPC visual texture");
        if (draft.VisualTexturePath.Length == 0)
        {
            messages.Add(new ApiError(
                "npc_invalid_visual_path",
                "NPC visual texture path is required before the draft can be saved.",
                ValidationSeverity.Error,
                "visual_texture_path"));
        }
        else if (!asset.Exists)
        {
            messages.Add(new ApiError(
                "npc_visual_unresolved",
                asset.Message ?? "The NPC visual texture is unavailable.",
                forPublication ? ValidationSeverity.Error : ValidationSeverity.Warning,
                "visual_texture_path",
                "Use a PNG under the configured game_client_assets root, such as res://assets/actors/npcs/Chars_139_200-F2-S.png."));
        }

        ValidateVisualFields(draft, messages);
        if (asset.Exists && asset.FilePath is not null && TryReadPngDimensions(asset.FilePath, out var width, out var height)
            && (draft.SourceWidth != width || draft.SourceHeight != height))
        {
            messages.Add(new ApiError(
                "npc_visual_dimension_mismatch",
                $"NPC source dimensions must match the resolved PNG dimensions ({width}x{height}).",
                forPublication ? ValidationSeverity.Error : ValidationSeverity.Warning,
                "source_width"));
        }
    }

    public static void ValidateVisualFields(
        NpcDraft draft,
        ICollection<ApiError> messages)
    {
        if (!NpcDomainRules.AreSourceDimensionsValid(draft.SourceWidth, draft.SourceHeight))
        {
            messages.Add(new ApiError(
                "npc_invalid_visual_dimensions",
                "NPC source width and height must be positive.",
                ValidationSeverity.Error,
                "source_width"));
        }
        if (!NpcDomainRules.IsFinite(draft.VisualAnchorOffsetX)
            || !NpcDomainRules.IsFinite(draft.VisualAnchorOffsetY))
        {
            messages.Add(new ApiError(
                "npc_invalid_visual_anchor",
                "NPC visual anchor offsets must be finite numbers.",
                ValidationSeverity.Error,
                "visual_anchor_offset_x"));
        }
        if (!NpcDomainRules.IsPositiveFinite(draft.VisualRenderScale))
        {
            messages.Add(new ApiError(
                "npc_invalid_visual_render_scale",
                "NPC visual render scale must be finite and greater than zero.",
                ValidationSeverity.Error,
                "visual_render_scale"));
        }
        if (!NpcDomainRules.IsInitialFootprintSupported(draft.FootprintWidthTiles, draft.FootprintHeightTiles))
        {
            messages.Add(new ApiError(
                "npc_unsupported_footprint",
                "The current MMO Project runtime supports only 1x1 NPC footprints.",
                ValidationSeverity.Error,
                "footprint_width_tiles"));
        }
    }

    private static void ValidateCompositeVisual(System.Text.Json.JsonElement? compositeVisual, ICollection<ApiError> messages)
    {
        if (compositeVisual is not { ValueKind: System.Text.Json.JsonValueKind.Object } value ||
            !value.TryGetProperty("rig_id", out var rigId) || rigId.ValueKind != System.Text.Json.JsonValueKind.String ||
            !value.TryGetProperty("base_layers", out var baseLayers) || baseLayers.ValueKind != System.Text.Json.JsonValueKind.Object)
        {
            messages.Add(new ApiError("invalid_npc_composite_visual", "Composite NPC visuals require rig_id and base_layers.", ValidationSeverity.Error, "composite_visual"));
        }
    }

    public static void ValidateMovement(
        NpcDraft draft,
        ICollection<ApiError> messages)
    {
        if (!NpcDomainRules.IsSupportedMovementBehavior(draft.MovementBehavior))
        {
            messages.Add(new ApiError(
                "npc_unsupported_movement_behavior",
                "NPC movement behavior must be static or random_wander.",
                ValidationSeverity.Error,
                "movement_behavior"));
        }
        if (!NpcDomainRules.IsWanderRadiusSupported(draft.WanderRadiusTiles))
        {
            messages.Add(new ApiError(
                "npc_invalid_wander_radius",
                $"NPC wander radius must be between 0 and {NpcAuthoringRegistry.MaxWanderRadiusTiles} logical tiles.",
                ValidationSeverity.Error,
                "wander_radius_tiles"));
        }
        if (NpcDomainRules.IsSupportedMovementBehavior(draft.MovementBehavior)
            && NpcDomainRules.IsWanderRadiusSupported(draft.WanderRadiusTiles)
            && !NpcDomainRules.IsMovementConsistent(draft.MovementBehavior, draft.WanderRadiusTiles))
        {
            messages.Add(new ApiError(
                "npc_invalid_wander_radius",
                "Static NPCs use zero wander radius, and random-wander NPCs require a positive wander radius.",
                ValidationSeverity.Error,
                "wander_radius_tiles"));
        }
        if (!NpcDomainRules.IsTickIntervalSupported(draft.TickIntervalMs))
        {
            messages.Add(new ApiError(
                "npc_invalid_tick_interval",
                $"NPC tick interval must be at least {NpcAuthoringRegistry.MinimumTickIntervalMilliseconds} ms.",
                ValidationSeverity.Error,
                "tick_interval_ms"));
        }
        if (!NpcDomainRules.IsIdleChanceSupported(draft.IdleChance))
        {
            messages.Add(new ApiError(
                "npc_invalid_idle_chance",
                "NPC idle chance must be a finite number from 0 to 1.",
                ValidationSeverity.Error,
                "idle_chance"));
        }
    }

    public async Task ValidateInteractionAsync(
        NpcDraft draft,
        ICollection<ApiError> messages,
        bool forPublication,
        CancellationToken cancellationToken = default)
    {
        if (!NpcDomainRules.IsSupportedInteractionType(draft.DefaultInteraction))
        {
            messages.Add(new ApiError(
                "npc_unsupported_interaction",
                "NPC interaction type must be talk.",
                ValidationSeverity.Error,
                "default_interaction"));
        }
        if (!NpcDomainRules.IsInteractionRangeSupported(draft.InteractionRangeTiles))
        {
            messages.Add(new ApiError(
                "npc_invalid_interaction_range",
                "NPC interaction range must be at least one logical tile.",
                ValidationSeverity.Error,
                "interaction_range_tiles"));
        }
        if (draft.InteractionEnabled && !NpcDomainRules.IsDialogueReferenceConsistent(true, draft.DefaultDialogueId))
        {
            messages.Add(new ApiError(
                "npc_dialogue_reference_invalid",
                "Talk-enabled NPCs require a default dialogue ID before publication.",
                forPublication ? ValidationSeverity.Error : ValidationSeverity.Warning,
                "default_dialogue_id"));
        }

        var dialogueId = NpcDomainRules.NormalizeOptional(draft.DefaultDialogueId);
        if (dialogueId is null)
        {
            return;
        }

        if (dialogueId.Length > 100 || !StableIdentifierRegex().IsMatch(dialogueId))
        {
            messages.Add(new ApiError(
                "npc_invalid_dialogue_reference",
                "Dialogue IDs must use lowercase snake-case stable identifiers.",
                ValidationSeverity.Error,
                "default_dialogue_id"));
            return;
        }

        var knownReferences = await _dialogueReferences.LoadAsync(cancellationToken);
        if (knownReferences.Complete
            && knownReferences.DialogueReferences.All(reference => reference.Id != dialogueId))
        {
            messages.Add(new ApiError(
                "npc_dialogue_reference_invalid",
                $"Dialogue ID '{dialogueId}' was not found in the configured MMO Project dialogue catalog.",
                ValidationSeverity.Error,
                "default_dialogue_id"));
        }
        else if (!knownReferences.Complete && draft.InteractionEnabled)
        {
            messages.Add(new ApiError(
                "npc_dialogue_reference_validation_incomplete",
                "Dialogue-reference validation is syntax-only because the MMO Project dialogue catalog is not available to the authoring host.",
                ValidationSeverity.Info,
                "default_dialogue_id"));
        }
    }

    public static void ValidateNotes(
        NpcDraft draft,
        ICollection<ApiError> messages)
    {
        if (draft.Notes is { Length: > NpcAuthoringRegistry.MaxNotesLength }
            || draft.Notes?.Any(character => char.IsControl(character) && character is not '\r' and not '\n' and not '\t') == true)
        {
            messages.Add(new ApiError(
                "npc_invalid_notes",
                $"NPC notes must be {NpcAuthoringRegistry.MaxNotesLength:N0} printable characters or fewer.",
                ValidationSeverity.Error,
                "notes"));
        }
    }

    private static bool TryReadPngDimensions(
        string filePath,
        out int width,
        out int height)
    {
        width = 0;
        height = 0;
        Span<byte> header = stackalloc byte[24];
        using var stream = File.OpenRead(filePath);
        if (stream.Read(header) != header.Length
            || header[0] != 0x89
            || header[1] != 0x50
            || header[2] != 0x4E
            || header[3] != 0x47)
        {
            return false;
        }

        width = ReadBigEndianInt32(header[16..20]);
        height = ReadBigEndianInt32(header[20..24]);
        return width > 0 && height > 0;
    }

    private static int ReadBigEndianInt32(ReadOnlySpan<byte> bytes) =>
        (bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3];

    [GeneratedRegex("^[a-z][a-z0-9]*(_[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex StableIdentifierRegex();
}

public sealed record NpcValidationOutcome(
    bool ValidForDraft,
    bool ValidForPublication,
    IReadOnlyList<ApiError> Messages,
    string? AssetPreviewFilePath);
