namespace BuildingBlocks.Behaviors;

/// <summary>
/// Structured log message templates for the CQRS pipeline itself. Module-specific templates
/// (e.g. "User {UserId} created") belong to the module that raises them, not here — this file is
/// generic infrastructure only.
/// </summary>
internal static class LogMessages
{
    public const string Command_Handling = "Handling command {CommandName}";
    public const string Command_Succeeded = "Command {CommandName} completed successfully";
    public const string Command_Failed = "Command {CommandName} failed. Error: {ErrorDescription}";

    public const string Query_Handling = "Handling query {QueryName}";
    public const string Query_Succeeded = "Query {QueryName} completed successfully";
    public const string Query_Failed = "Query {QueryName} failed. Error: {ErrorDescription}";
}
