namespace MMO.ContentStudio.AuthoringHost.Health;

public enum AuthoringSchemaRequirementKind
{
    Table,
    Column,
    Constraint,
    Trigger
}

public sealed record AuthoringSchemaRequirement(
    AuthoringSchemaRequirementKind Kind,
    string ObjectName,
    string? TableName = null)
{
    public string Key => $"{Kind}:{TableName}:{ObjectName}";

    public static AuthoringSchemaRequirement Table(string tableName) =>
        new(AuthoringSchemaRequirementKind.Table, tableName);

    public static AuthoringSchemaRequirement Column(string tableName, string columnName) =>
        new(AuthoringSchemaRequirementKind.Column, columnName, tableName);

    public static AuthoringSchemaRequirement Constraint(string constraintName) =>
        new(AuthoringSchemaRequirementKind.Constraint, constraintName);

    public static AuthoringSchemaRequirement Trigger(string tableName, string triggerName) =>
        new(AuthoringSchemaRequirementKind.Trigger, triggerName, tableName);
}

public interface IAuthoringSchemaRequirementProvider
{
    string FeatureId { get; }

    IReadOnlyList<AuthoringSchemaRequirement> GetRequirements();
}
