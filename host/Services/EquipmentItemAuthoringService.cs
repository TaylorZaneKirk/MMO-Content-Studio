using System.Text;
using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Persistence;
using Npgsql;

namespace MMO.ContentStudio.AuthoringHost.Services;

public sealed class EquipmentItemAuthoringService
{
    private const string VisualAssetModelMessage =
        "The current runtime derives player-layer asset keys from item name and slot. T3A does not store a separate visual asset override yet.";

    private readonly EquipmentItemRepository _repository;
    private readonly ItemAssetService _assetService;
    private readonly ILogger<EquipmentItemAuthoringService> _logger;

    public EquipmentItemAuthoringService(
        EquipmentItemRepository repository,
        ItemAssetService assetService,
        ILogger<EquipmentItemAuthoringService> logger)
    {
        _repository = repository;
        _assetService = assetService;
        _logger = logger;
    }

    public async Task<AuthoringOperationResult<EquipmentAuthoringOptionsResponse>> LoadOptionsAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var slots = await _repository.LoadSlotsAsync(cancellationToken);
            var skills = await _repository.LoadSkillsAsync(cancellationToken);
            var wearableSlots = slots
                .Where(slot => EquipmentItemRepository.IsWearableSlot(slot.SlotId))
                .Select(slot => new AuthoringOption(slot.SlotId, slot.DisplayName))
                .ToArray();
            var deferredHandSlots = slots
                .Where(slot => EquipmentItemRepository.IsHandSlot(slot.SlotId))
                .Select(slot => new AuthoringOption(slot.SlotId, slot.DisplayName))
                .ToArray();
            return AuthoringOperationResult<EquipmentAuthoringOptionsResponse>.Success(
                new EquipmentAuthoringOptionsResponse(
                    wearableSlots,
                    deferredHandSlots,
                    skills
                        .Select(skill => new AuthoringOption(skill.SkillId, skill.DisplayName))
                        .ToArray(),
                    CombatBonusOptions(),
                    false,
                    VisualAssetModelMessage));
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            return DatabaseFailure<EquipmentAuthoringOptionsResponse>(exception);
        }
    }

    public async Task<AuthoringOperationResult<EquipmentCatalogResponse>> ListAsync(
        string? search,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var records = await _repository.ListAsync(search, cancellationToken);
            return AuthoringOperationResult<EquipmentCatalogResponse>.Success(
                new EquipmentCatalogResponse(
                    DateTimeOffset.UtcNow,
                    records.Select(ToSummary).ToArray()));
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            return DatabaseFailure<EquipmentCatalogResponse>(exception);
        }
    }

    public async Task<AuthoringOperationResult<EquipmentItemDefinition>> LoadAsync(
        string itemId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var record = await _repository.LoadAsync(itemId, cancellationToken);
            return record is null
                ? AuthoringOperationResult<EquipmentItemDefinition>.Failure(new ApiError(
                    "item_not_found",
                    $"Item '{itemId}' does not exist.",
                    ValidationSeverity.Error,
                    "item_id"))
                : AuthoringOperationResult<EquipmentItemDefinition>.Success(ToDefinition(record));
        }
        catch (Exception exception) when (IsDatabaseFailure(exception))
        {
            return DatabaseFailure<EquipmentItemDefinition>(exception);
        }
    }

    private EquipmentItemDefinition ToDefinition(EquipmentItemRecord record)
    {
        var asset = _assetService.Resolve(record.IconTexturePath);
        return new EquipmentItemDefinition(
            record.ItemId,
            record.DisplayName,
            record.IconTexturePath,
            record.RuntimeEnabled ? "Published" : "Draft",
            AuthoringKind(record),
            record.EquipmentSlotId,
            record.EquipmentSlotDisplayName,
            record.RequiredStrength,
            record.Requirements,
            record.SkillModifiers,
            record.CombatProfile,
            record.CombatBonuses,
            EditableInEquipment(record),
            DeriveVisualAssetKey(record.EquipmentSlotId, record.DisplayName),
            VisualAssetModelMessage,
            record.UpdatedAtUtc,
            asset.FilePath);
    }

    private static EquipmentItemSummary ToSummary(EquipmentItemRecord record) =>
        new(
            record.ItemId,
            record.DisplayName,
            record.IconTexturePath,
            record.RuntimeEnabled ? "Published" : "Draft",
            AuthoringKind(record),
            record.EquipmentSlotId,
            record.EquipmentSlotDisplayName,
            EditableInEquipment(record),
            record.UpdatedAtUtc);

    private static string AuthoringKind(EquipmentItemRecord record)
    {
        if (record.HasConsumableProfile)
        {
            return "Consumable";
        }

        if (EquipmentItemRepository.IsHandSlot(record.EquipmentSlotId)
            || record.HasCombatProfile)
        {
            return "WeaponOrTool";
        }

        return EquipmentItemRepository.IsWearableSlot(record.EquipmentSlotId)
            || record.RequiredStrength > 1
            || record.HasCombatBonuses
            || record.HasSkillRequirements
            || record.HasSkillModifiers
                ? "Equipment"
                : "Basic";
    }

    private static bool EditableInEquipment(EquipmentItemRecord record) =>
        !record.HasConsumableProfile
        && !record.HasCombatProfile
        && EquipmentItemRepository.IsWearableSlot(record.EquipmentSlotId);

    private static string? DeriveVisualAssetKey(string? slotId, string displayName)
    {
        if (slotId is null)
        {
            return null;
        }

        var builder = new StringBuilder(displayName.Trim().Length);
        var previousWasSeparator = false;
        foreach (var character in displayName.Trim().ToLowerInvariant())
        {
            if (character is '\'' || character == '\u2019')
            {
                continue;
            }

            if (character is '-' or '/' || char.IsWhiteSpace(character))
            {
                if (!previousWasSeparator && builder.Length > 0)
                {
                    builder.Append('_');
                    previousWasSeparator = true;
                }
                continue;
            }

            builder.Append(character);
            previousWasSeparator = false;
        }

        var key = builder.ToString().Trim('_');
        return slotId == "legs" && key.EndsWith("_legs", StringComparison.Ordinal)
            ? key[..^"_legs".Length]
            : key;
    }

    private static IReadOnlyList<AuthoringOption> CombatBonusOptions() =>
    [
        new AuthoringOption("attack_thrust", "Attack Thrust"),
        new AuthoringOption("attack_slash", "Attack Slash"),
        new AuthoringOption("attack_crush", "Attack Crush"),
        new AuthoringOption("attack_ranged", "Attack Ranged"),
        new AuthoringOption("attack_magic", "Attack Magic"),
        new AuthoringOption("strength_melee", "Strength Melee"),
        new AuthoringOption("strength_ranged", "Strength Ranged"),
        new AuthoringOption("strength_magic", "Strength Magic"),
        new AuthoringOption("defence_thrust", "Defence Thrust"),
        new AuthoringOption("defence_slash", "Defence Slash"),
        new AuthoringOption("defence_crush", "Defence Crush"),
        new AuthoringOption("defence_ranged", "Defence Ranged"),
        new AuthoringOption("defence_magic", "Defence Magic")
    ];

    private AuthoringOperationResult<T> DatabaseFailure<T>(Exception exception)
    {
        _logger.LogWarning(exception, "Equipment authoring database operation failed");
        return AuthoringOperationResult<T>.Failure(new ApiError(
            "database_unavailable",
            "The configured development database is unavailable or missing the T3A equipment schema.",
            ValidationSeverity.Error,
            Remediation: "Review the Environment tab and apply the MMO Project equipment, skill, and combat-bonus migrations."));
    }

    private static bool IsDatabaseFailure(Exception exception) =>
        exception is AuthoringDatabaseUnavailableException
            or NpgsqlException
            or TimeoutException
            or InvalidOperationException;
}
