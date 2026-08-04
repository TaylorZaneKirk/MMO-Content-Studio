using MMO.ContentStudio.AuthoringHost.Contracts;
using Npgsql;

namespace MMO.ContentStudio.AuthoringHost.Health;

public sealed class SchemaHealthInspector
{
    public Task<HealthCheck> CheckAsync(
        NpgsqlConnection connection,
        AuthoringSchemaRequirement requirement,
        CancellationToken cancellationToken) =>
        requirement.Kind switch
        {
            AuthoringSchemaRequirementKind.Table =>
                CheckTableAsync(connection, requirement.ObjectName, cancellationToken),
            AuthoringSchemaRequirementKind.Column =>
                CheckColumnAsync(
                    connection,
                    RequireTableName(requirement),
                    requirement.ObjectName,
                    cancellationToken),
            AuthoringSchemaRequirementKind.Constraint =>
                CheckConstraintAsync(connection, requirement.ObjectName, cancellationToken),
            AuthoringSchemaRequirementKind.Trigger =>
                CheckTriggerAsync(
                    connection,
                    RequireTableName(requirement),
                    requirement.ObjectName,
                    true,
                    cancellationToken),
            AuthoringSchemaRequirementKind.AbsentTrigger =>
                CheckTriggerAsync(
                    connection,
                    RequireTableName(requirement),
                    requirement.ObjectName,
                    false,
                    cancellationToken),
            _ => throw new ArgumentOutOfRangeException(
                nameof(requirement),
                requirement.Kind,
                "Unsupported authoring schema requirement kind.")
        };

    private static string RequireTableName(AuthoringSchemaRequirement requirement) =>
        string.IsNullOrWhiteSpace(requirement.TableName)
            ? throw new InvalidOperationException(
                $"Schema requirement '{requirement.Key}' requires a table name.")
            : requirement.TableName;

    private static async Task<HealthCheck> CheckTableAsync(
        NpgsqlConnection connection,
        string tableName,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select exists (
                select 1
                from information_schema.tables
                where table_schema = 'public'
                  and table_name = @table_name
            );
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("table_name", tableName);
        var exists = (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);

        return exists
            ? new HealthCheck(
                $"table:{tableName}",
                HealthState.Healthy,
                $"Required table '{tableName}' exists.")
            : new HealthCheck(
                $"table:{tableName}",
                HealthState.Unhealthy,
                $"Required table '{tableName}' is missing.");
    }

    private static async Task<HealthCheck> CheckColumnAsync(
        NpgsqlConnection connection,
        string tableName,
        string columnName,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select exists (
                select 1
                from information_schema.columns
                where table_schema = 'public'
                  and table_name = @table_name
                  and column_name = @column_name
            );
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("table_name", tableName);
        command.Parameters.AddWithValue("column_name", columnName);
        var exists = (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);

        return exists
            ? new HealthCheck(
                $"column:{tableName}.{columnName}",
                HealthState.Healthy,
                $"Required column '{tableName}.{columnName}' exists.")
            : new HealthCheck(
                $"column:{tableName}.{columnName}",
                HealthState.Unhealthy,
                $"Required column '{tableName}.{columnName}' is missing.");
    }

    private static async Task<HealthCheck> CheckConstraintAsync(
        NpgsqlConnection connection,
        string constraintName,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select exists (
                select 1
                from information_schema.table_constraints
                where constraint_schema = 'public'
                  and constraint_name = @constraint_name
            );
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("constraint_name", constraintName);
        var exists = (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);

        return exists
            ? new HealthCheck(
                $"constraint:{constraintName}",
                HealthState.Healthy,
                $"Required constraint '{constraintName}' exists.")
            : new HealthCheck(
                $"constraint:{constraintName}",
                HealthState.Unhealthy,
                $"Required constraint '{constraintName}' is missing.");
    }

    private static async Task<HealthCheck> CheckTriggerAsync(
        NpgsqlConnection connection,
        string tableName,
        string triggerName,
        bool shouldExist,
        CancellationToken cancellationToken)
    {
        const string sql = """
            select exists (
                select 1
                from information_schema.triggers
                where event_object_schema = 'public'
                  and event_object_table = @table_name
                  and trigger_name = @trigger_name
            );
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("table_name", tableName);
        command.Parameters.AddWithValue("trigger_name", triggerName);
        var exists = (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);

        if (shouldExist)
        {
            return exists
                ? new HealthCheck(
                    $"trigger:{triggerName}",
                    HealthState.Healthy,
                    $"Required trigger '{triggerName}' exists on '{tableName}'.")
                : new HealthCheck(
                    $"trigger:{triggerName}",
                    HealthState.Unhealthy,
                    $"Required trigger '{triggerName}' is missing from '{tableName}'.");
        }

        return !exists
            ? new HealthCheck(
                $"absent-trigger:{triggerName}",
                HealthState.Healthy,
                $"Obsolete trigger '{triggerName}' is absent from '{tableName}'.")
            : new HealthCheck(
                $"absent-trigger:{triggerName}",
                HealthState.Unhealthy,
                $"Obsolete trigger '{triggerName}' still exists on '{tableName}'; apply migration 023_item_tool_capability_independence.sql so tool capabilities are not restricted to hand equipment.");
    }
}
