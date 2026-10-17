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
    /// <summary>The error CODE, never the description. Descriptions are written for the person who made
    /// the request and routinely interpolate the values that caused the failure — an email address, an
    /// id, and (once commands started carrying credentials) anything a validator chose to quote back.
    /// Codes are a closed, deliberately chosen vocabulary, which is what a log wants anyway.</summary>
    public const string Command_Failed = "Command {CommandName} failed. Error: {ErrorCode}";

    public const string Query_Handling = "Handling query {QueryName}";
    public const string Query_Succeeded = "Query {QueryName} completed successfully";
    /// <summary>The error CODE, never the description — see <see cref="Command_Failed"/>.</summary>
    public const string Query_Failed = "Query {QueryName} failed. Error: {ErrorCode}";
}
