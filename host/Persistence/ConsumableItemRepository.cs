using MMO.ContentStudio.AuthoringHost.Contracts;
using MMO.ContentStudio.AuthoringHost.Services;
using Npgsql;
using NpgsqlTypes;

namespace MMO.ContentStudio.AuthoringHost.Persistence;

public sealed class ConsumableItemRepository
{
    private readonly AuthoringDatabaseConnectionFactory _connectionFactory;

    public ConsumableItemRepository(AuthoringDatabaseConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<ConsumableItemRecord>> ListAsync(
        string? search,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            select
                i.item_id,
                i.item_name,
                i.icon_texture_path,
                i.equipment_slot_id,
                i.runtime_enabled,
                i.required_strength,
                i.updated_at as item_updated_at,
                p.item_id is not null as has_consumable_profile,
                p.use_action,
                p.consume_quantity,
                p.result_item_id,
                p.success_message,
                p.usable_in_combat,
                p.cooldown_ms,
                p.use_animation_id,
                p.use_sound_resource_path,
                p.updated_at as profile_updated_at,
                greatest(i.updated_at, coalesce(p.updated_at, i.updated_at)) as aggregate_updated_at
            from item_definitions i
            left join item_consumable_profiles p on p.item_id = i.item_id
            where @search is null
               or i.item_id ilike '%' || @search || '%'
               or i.item_name ilike '%' || @search || '%'
            order by
                case when p.item_id is null then 1 else 0 end,
                i.item_name,
                i.item_id;
            """;

        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add("search", NpgsqlDbType.Text).Value = string.IsNullOrWhiteSpace(search)
            ? DBNull.Value
            : search.Trim();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var records = new List<ConsumableItemRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(ReadBaseRecord(reader, [], []));
        }

        return records;
    }

    public async Task<ConsumableItemRecord?> LoadAsync(
        string itemId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        var record = await LoadBaseAsync(connection, null, itemId, false, cancellationToken);
        if (record is null)
        {
            return null;
        }

        IReadOnlyList<ConsumableRequirementDefinition> requirements = record.HasConsumableProfile
            ? await LoadRequirementsAsync(connection, null, itemId, cancellationToken)
            : Array.Empty<ConsumableRequirementDefinition>();
        IReadOnlyList<ConsumableEffectDefinition> effects = record.HasConsumableProfile
            ? await LoadEffectsAsync(connection, null, itemId, cancellationToken)
            : Array.Empty<ConsumableEffectDefinition>();
        return record with { Requirements = requirements, Effects = effects };
    }

    public async Task<IReadOnlyList<AuthoringOption>> LoadSkillOptionsAsync(
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            select skill_id, display_name
            from skill_definitions
            order by sort_order, skill_id;
            """;
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var skills = new List<AuthoringOption>();
        while (await reader.ReadAsync(cancellationToken))
        {
            skills.Add(new AuthoringOption(
                reader.GetString(reader.GetOrdinal("skill_id")),
                reader.GetString(reader.GetOrdinal("display_name"))));
        }

        return skills;
    }

    public async Task<ReferencedItemRecord?> LoadReferencedItemAsync(
        string itemId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            select item_id, item_name, runtime_enabled
            from item_definitions
            where item_id = @item_id;
            """;
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("item_id", itemId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ReferencedItemRecord(
            reader.GetString(reader.GetOrdinal("item_id")),
            reader.GetString(reader.GetOrdinal("item_name")),
            reader.GetBoolean(reader.GetOrdinal("runtime_enabled")));
    }

    public async Task<ConsumableItemRecord> SaveDraftAsync(
        string itemId,
        NormalizedConsumableDraft draft,
        DateTimeOffset? expectedUpdatedAtUtc,
        bool expectNew,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var existing = await LoadBaseAsync(connection, transaction, itemId, true, cancellationToken);
        if (expectNew && existing is not null)
        {
            throw new ConsumableConcurrencyException(itemId, existing.UpdatedAtUtc);
        }
        EnsureExpectedVersion(existing, expectedUpdatedAtUtc);
        EnsureConsumableEditable(existing);

        const string itemSql = """
            insert into item_definitions (
                item_id,
                item_name,
                icon_texture_path,
                equipment_slot_id,
                runtime_enabled,
                required_strength,
                updated_at
            ) values (
                @item_id,
                @item_name,
                @icon_texture_path,
                null,
                false,
                1,
                now()
            )
            on conflict (item_id)
            do update set
                item_name = excluded.item_name,
                icon_texture_path = excluded.icon_texture_path,
                equipment_slot_id = null,
                required_strength = 1,
                runtime_enabled = false,
                updated_at = now();
            """;
        await using (var command = new NpgsqlCommand(itemSql, connection, transaction))
        {
            command.Parameters.AddWithValue("item_id", itemId);
            command.Parameters.AddWithValue("item_name", draft.DisplayName);
            command.Parameters.AddWithValue("icon_texture_path", draft.IconTexturePath);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        const string profileSql = """
            insert into item_consumable_profiles (
                item_id,
                use_action,
                consume_quantity,
                result_item_id,
                success_message,
                usable_in_combat,
                cooldown_ms,
                use_animation_id,
                use_sound_resource_path,
                updated_at
            ) values (
                @item_id,
                @use_action,
                @consume_quantity,
                @result_item_id,
                @success_message,
                @usable_in_combat,
                @cooldown_ms,
                @use_animation_id,
                @use_sound_resource_path,
                now()
            )
            on conflict (item_id)
            do update set
                use_action = excluded.use_action,
                consume_quantity = excluded.consume_quantity,
                result_item_id = excluded.result_item_id,
                success_message = excluded.success_message,
                usable_in_combat = excluded.usable_in_combat,
                cooldown_ms = excluded.cooldown_ms,
                use_animation_id = excluded.use_animation_id,
                use_sound_resource_path = excluded.use_sound_resource_path,
                updated_at = now();
            """;
        await using (var command = new NpgsqlCommand(profileSql, connection, transaction))
        {
            command.Parameters.AddWithValue("item_id", itemId);
            command.Parameters.AddWithValue("use_action", draft.UseAction);
            command.Parameters.AddWithValue("consume_quantity", draft.ConsumeQuantity);
            AddNullableText(command, "result_item_id", draft.ResultItemId);
            AddNullableText(command, "success_message", draft.SuccessMessage);
            command.Parameters.AddWithValue("usable_in_combat", draft.UsableInCombat);
            command.Parameters.AddWithValue("cooldown_ms", draft.CooldownMs);
            AddNullableText(command, "use_animation_id", draft.UseAnimationId);
            AddNullableText(command, "use_sound_resource_path", draft.UseSoundResourcePath);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await ReplaceRequirementsAsync(connection, transaction, itemId, draft.Requirements, cancellationToken);
        await ReplaceEffectsAsync(connection, transaction, itemId, draft.Effects, cancellationToken);

        var savedBase = await LoadBaseAsync(connection, transaction, itemId, false, cancellationToken)
            ?? throw new InvalidOperationException("Saved consumable could not be reloaded inside its transaction.");
        var saved = savedBase with
        {
            Requirements = await LoadRequirementsAsync(connection, transaction, itemId, cancellationToken),
            Effects = await LoadEffectsAsync(connection, transaction, itemId, cancellationToken)
        };
        await transaction.CommitAsync(cancellationToken);
        return saved;
    }

    public async Task<ConsumableItemRecord> SetPublicationAsync(
        string itemId,
        bool runtimeEnabled,
        DateTimeOffset? expectedUpdatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var existingBase = await LoadBaseAsync(connection, transaction, itemId, true, cancellationToken)
            ?? throw new ConsumableNotFoundException(itemId);
        EnsureExpectedVersion(existingBase, expectedUpdatedAtUtc);
        EnsureConsumableEditable(existingBase);
        if (!existingBase.HasConsumableProfile)
        {
            throw new ConsumableProfileMissingException(itemId);
        }
        if (runtimeEnabled)
        {
            await EnsurePublicationReferencesAsync(connection, transaction, itemId, cancellationToken);
        }
        if (existingBase.RuntimeEnabled != runtimeEnabled)
        {
            const string sql = """
                update item_definitions
                set runtime_enabled = @runtime_enabled,
                    updated_at = now()
                where item_id = @item_id;
                """;
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("runtime_enabled", runtimeEnabled);
            command.Parameters.AddWithValue("item_id", itemId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        var savedBase = await LoadBaseAsync(connection, transaction, itemId, false, cancellationToken)
            ?? throw new InvalidOperationException("Published consumable could not be reloaded inside its transaction.");
        var saved = savedBase with
        {
            Requirements = await LoadRequirementsAsync(connection, transaction, itemId, cancellationToken),
            Effects = await LoadEffectsAsync(connection, transaction, itemId, cancellationToken)
        };
        await transaction.CommitAsync(cancellationToken);
        return saved;
    }

    public async Task DeleteAsync(
        string itemId,
        DateTimeOffset? expectedUpdatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var existing = await LoadBaseAsync(connection, transaction, itemId, true, cancellationToken)
            ?? throw new ConsumableNotFoundException(itemId);
        EnsureExpectedVersion(existing, expectedUpdatedAtUtc);
        EnsureConsumableEditable(existing);
        if (!existing.HasConsumableProfile)
        {
            throw new ConsumableProfileMissingException(itemId);
        }
        if (existing.RuntimeEnabled)
        {
            throw new ConsumablePublishedDeleteException(itemId);
        }

        await ExecuteDeleteAsync(connection, transaction, "item_consumable_requirements", itemId, cancellationToken);
        await ExecuteDeleteAsync(connection, transaction, "item_consumable_effects", itemId, cancellationToken);
        await ExecuteDeleteAsync(connection, transaction, "item_consumable_profiles", itemId, cancellationToken);

        const string sql = "delete from item_definitions where item_id = @item_id;";
        await using (var command = new NpgsqlCommand(sql, connection, transaction))
        {
            command.Parameters.AddWithValue("item_id", itemId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task EnsurePublicationReferencesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string itemId,
        CancellationToken cancellationToken)
    {
        const string effectSql = """
            select count(*)
            from item_consumable_effects
            where item_id = @item_id;
            """;
        await using (var effectCommand = new NpgsqlCommand(effectSql, connection, transaction))
        {
            effectCommand.Parameters.AddWithValue("item_id", itemId);
            var effectCount = Convert.ToInt32(await effectCommand.ExecuteScalarAsync(cancellationToken));
            if (effectCount < 1)
            {
                throw new ConsumablePublicationIntegrityException(
                    "consumable_has_no_effects",
                    $"Consumable '{itemId}' cannot be published without an effect.",
                    "effects");
            }
        }

        const string resultSql = """
            select result.item_id, result.runtime_enabled
            from item_consumable_profiles profile
            join item_definitions result on result.item_id = profile.result_item_id
            where profile.item_id = @item_id
              and profile.result_item_id is not null
            for share of result;
            """;
        await using (var resultCommand = new NpgsqlCommand(resultSql, connection, transaction))
        {
            resultCommand.Parameters.AddWithValue("item_id", itemId);
            await using var reader = await resultCommand.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                if (!reader.GetBoolean(reader.GetOrdinal("runtime_enabled")))
                {
                    throw new ConsumablePublicationIntegrityException(
                        "result_item_not_published",
                        $"The configured result item for '{itemId}' is not published.",
                        "result_item_id");
                }
            }
        }

        const string requirementSql = """
            select requirement.target_id
            from item_consumable_requirements requirement
            left join skill_definitions skill on skill.skill_id = requirement.target_id
            where requirement.item_id = @item_id
              and requirement.requirement_type = 'skill_minimum'
              and skill.skill_id is null
            limit 1;
            """;
        await using var requirementCommand = new NpgsqlCommand(requirementSql, connection, transaction);
        requirementCommand.Parameters.AddWithValue("item_id", itemId);
        var missingSkill = await requirementCommand.ExecuteScalarAsync(cancellationToken) as string;
        if (missingSkill is not null)
        {
            throw new ConsumablePublicationIntegrityException(
                "unknown_requirement_skill",
                $"Required skill '{missingSkill}' no longer exists.",
                "requirements");
        }
    }

    private static async Task ReplaceRequirementsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string itemId,
        IReadOnlyList<ConsumableRequirementDefinition> requirements,
        CancellationToken cancellationToken)
    {
        await using (var delete = new NpgsqlCommand(
            "delete from item_consumable_requirements where item_id = @item_id;",
            connection,
            transaction))
        {
            delete.Parameters.AddWithValue("item_id", itemId);
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        const string sql = """
            insert into item_consumable_requirements (
                item_id, requirement_index, requirement_type, target_id, minimum_value
            ) values (
                @item_id, @requirement_index, @requirement_type, @target_id, @minimum_value
            );
            """;
        foreach (var requirement in requirements)
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("item_id", itemId);
            command.Parameters.AddWithValue("requirement_index", requirement.RequirementIndex);
            command.Parameters.AddWithValue("requirement_type", requirement.RequirementType);
            command.Parameters.AddWithValue("target_id", requirement.TargetId);
            command.Parameters.AddWithValue("minimum_value", requirement.MinimumValue);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task ReplaceEffectsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string itemId,
        IReadOnlyList<ConsumableEffectDefinition> effects,
        CancellationToken cancellationToken)
    {
        await using (var delete = new NpgsqlCommand(
            "delete from item_consumable_effects where item_id = @item_id;",
            connection,
            transaction))
        {
            delete.Parameters.AddWithValue("item_id", itemId);
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        const string sql = """
            insert into item_consumable_effects (
                item_id, effect_index, effect_type, target_id, minimum_amount, maximum_amount
            ) values (
                @item_id, @effect_index, @effect_type, @target_id, @minimum_amount, @maximum_amount
            );
            """;
        foreach (var effect in effects)
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("item_id", itemId);
            command.Parameters.AddWithValue("effect_index", effect.EffectIndex);
            command.Parameters.AddWithValue("effect_type", effect.EffectType);
            command.Parameters.AddWithValue("target_id", effect.TargetId);
            command.Parameters.AddWithValue("minimum_amount", effect.MinimumAmount);
            command.Parameters.AddWithValue("maximum_amount", effect.MaximumAmount);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task<ConsumableItemRecord?> LoadBaseAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string itemId,
        bool forUpdate,
        CancellationToken cancellationToken)
    {
        var sql = """
            select
                i.item_id,
                i.item_name,
                i.icon_texture_path,
                i.equipment_slot_id,
                i.runtime_enabled,
                i.required_strength,
                i.updated_at as item_updated_at,
                p.item_id is not null as has_consumable_profile,
                p.use_action,
                p.consume_quantity,
                p.result_item_id,
                p.success_message,
                p.usable_in_combat,
                p.cooldown_ms,
                p.use_animation_id,
                p.use_sound_resource_path,
                p.updated_at as profile_updated_at,
                greatest(i.updated_at, coalesce(p.updated_at, i.updated_at)) as aggregate_updated_at
            from item_definitions i
            left join item_consumable_profiles p on p.item_id = i.item_id
            where i.item_id = @item_id
            """ + (forUpdate ? " for update of i;" : ";");
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("item_id", itemId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadBaseRecord(reader, [], []) : null;
    }

    private static async Task<IReadOnlyList<ConsumableRequirementDefinition>> LoadRequirementsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string itemId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select requirement_index, requirement_type, target_id, minimum_value
            from item_consumable_requirements
            where item_id = @item_id
            order by requirement_index;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("item_id", itemId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var values = new List<ConsumableRequirementDefinition>();
        while (await reader.ReadAsync(cancellationToken))
        {
            values.Add(new ConsumableRequirementDefinition(
                reader.GetInt32(reader.GetOrdinal("requirement_index")),
                reader.GetString(reader.GetOrdinal("requirement_type")),
                reader.GetString(reader.GetOrdinal("target_id")),
                reader.GetInt32(reader.GetOrdinal("minimum_value"))));
        }

        return values;
    }

    private static async Task<IReadOnlyList<ConsumableEffectDefinition>> LoadEffectsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string itemId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select effect_index, effect_type, target_id, minimum_amount, maximum_amount
            from item_consumable_effects
            where item_id = @item_id
            order by effect_index;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("item_id", itemId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var values = new List<ConsumableEffectDefinition>();
        while (await reader.ReadAsync(cancellationToken))
        {
            values.Add(new ConsumableEffectDefinition(
                reader.GetInt32(reader.GetOrdinal("effect_index")),
                reader.GetString(reader.GetOrdinal("effect_type")),
                reader.GetString(reader.GetOrdinal("target_id")),
                reader.GetInt32(reader.GetOrdinal("minimum_amount")),
                reader.GetInt32(reader.GetOrdinal("maximum_amount"))));
        }

        return values;
    }

    private static ConsumableItemRecord ReadBaseRecord(
        NpgsqlDataReader reader,
        IReadOnlyList<ConsumableRequirementDefinition> requirements,
        IReadOnlyList<ConsumableEffectDefinition> effects)
    {
        var equipmentOrdinal = reader.GetOrdinal("equipment_slot_id");
        var hasProfile = reader.GetBoolean(reader.GetOrdinal("has_consumable_profile"));
        return new ConsumableItemRecord(
            reader.GetString(reader.GetOrdinal("item_id")),
            reader.GetString(reader.GetOrdinal("item_name")),
            reader.GetString(reader.GetOrdinal("icon_texture_path")),
            reader.IsDBNull(equipmentOrdinal) ? null : reader.GetString(equipmentOrdinal),
            reader.GetBoolean(reader.GetOrdinal("runtime_enabled")),
            reader.GetInt32(reader.GetOrdinal("required_strength")),
            hasProfile,
            ReadNullableString(reader, "use_action") ?? "use",
            ReadNullableInt(reader, "consume_quantity") ?? 1,
            ReadNullableString(reader, "result_item_id"),
            ReadNullableString(reader, "success_message"),
            ReadNullableBool(reader, "usable_in_combat") ?? true,
            ReadNullableInt(reader, "cooldown_ms") ?? 0,
            ReadNullableString(reader, "use_animation_id"),
            ReadNullableString(reader, "use_sound_resource_path"),
            requirements,
            effects,
            ReadUtc(reader, "aggregate_updated_at"));
    }

    private static string? ReadNullableString(NpgsqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static int? ReadNullableInt(NpgsqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
    }

    private static bool? ReadNullableBool(NpgsqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetBoolean(ordinal);
    }

    private static DateTimeOffset ReadUtc(NpgsqlDataReader reader, string column) =>
        new(DateTime.SpecifyKind(reader.GetFieldValue<DateTime>(reader.GetOrdinal(column)), DateTimeKind.Utc));

    private static void AddNullableText(NpgsqlCommand command, string name, string? value) =>
        command.Parameters.Add(name, NpgsqlDbType.Text).Value = (object?)value ?? DBNull.Value;

    private static async Task ExecuteDeleteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string table,
        string itemId,
        CancellationToken cancellationToken)
    {
        var sql = $"delete from {table} where item_id = @item_id;";
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("item_id", itemId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void EnsureConsumableEditable(ConsumableItemRecord? existing)
    {
        if (existing is not null
            && (existing.EquipmentSlotId is not null || existing.RequiredStrength != 1))
        {
            throw new ConsumableKindConflictException(
                existing.ItemId,
                "Items with equipment metadata must be edited in the Equipment workspace.");
        }
    }

    private static void EnsureExpectedVersion(
        ConsumableItemRecord? existing,
        DateTimeOffset? expectedUpdatedAtUtc)
    {
        if (existing is null)
        {
            return;
        }

        if (expectedUpdatedAtUtc is null
            || existing.UpdatedAtUtc.ToUniversalTime() != expectedUpdatedAtUtc.Value.ToUniversalTime())
        {
            throw new ConsumableConcurrencyException(existing.ItemId, existing.UpdatedAtUtc);
        }
    }
}

public sealed record ConsumableItemRecord(
    string ItemId,
    string DisplayName,
    string IconTexturePath,
    string? EquipmentSlotId,
    bool RuntimeEnabled,
    int RequiredStrength,
    bool HasConsumableProfile,
    string UseAction,
    int ConsumeQuantity,
    string? ResultItemId,
    string? SuccessMessage,
    bool UsableInCombat,
    int CooldownMs,
    string? UseAnimationId,
    string? UseSoundResourcePath,
    IReadOnlyList<ConsumableRequirementDefinition> Requirements,
    IReadOnlyList<ConsumableEffectDefinition> Effects,
    DateTimeOffset UpdatedAtUtc);

public sealed record ReferencedItemRecord(string ItemId, string DisplayName, bool RuntimeEnabled);

public sealed record NormalizedConsumableDraft(
    string DisplayName,
    string IconTexturePath,
    string UseAction,
    int ConsumeQuantity,
    string? ResultItemId,
    string? SuccessMessage,
    bool UsableInCombat,
    int CooldownMs,
    string? UseAnimationId,
    string? UseSoundResourcePath,
    IReadOnlyList<ConsumableRequirementDefinition> Requirements,
    IReadOnlyList<ConsumableEffectDefinition> Effects);

public sealed class ConsumableNotFoundException : Exception
{
    public ConsumableNotFoundException(string itemId) : base($"Item '{itemId}' does not exist.") { }
}

public sealed class ConsumableProfileMissingException : Exception
{
    public ConsumableProfileMissingException(string itemId)
        : base($"Item '{itemId}' has no consumable profile. Save it as a consumable draft first.") { }
}

public sealed class ConsumableKindConflictException : Exception
{
    public ConsumableKindConflictException(string itemId, string message)
        : base($"Item '{itemId}' cannot be edited here. {message}") { }
}

public sealed class ConsumablePublicationIntegrityException : Exception
{
    public ConsumablePublicationIntegrityException(string code, string message, string field)
        : base(message)
    {
        Code = code;
        Field = field;
    }

    public string Code { get; }
    public string Field { get; }
}

public sealed class ConsumableConcurrencyException : Exception
{
    public ConsumableConcurrencyException(string itemId, DateTimeOffset currentUpdatedAtUtc)
        : base($"Item '{itemId}' changed after it was loaded. Reload it before saving.")
    {
        CurrentUpdatedAtUtc = currentUpdatedAtUtc;
    }

    public DateTimeOffset CurrentUpdatedAtUtc { get; }
}

public sealed class ConsumablePublishedDeleteException : Exception
{
    public ConsumablePublishedDeleteException(string itemId)
        : base($"Consumable '{itemId}' must be disabled before it can be deleted.")
    {
    }
}
