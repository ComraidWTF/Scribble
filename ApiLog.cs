using Microsoft.Extensions.Logging;

namespace LogDemo.Api.Logging;

/// <summary>
/// This API's log messages in one place, same pattern as AppLog in the WPF apps:
/// stable event ids, source-generated, zero allocations when a level is disabled.
/// </summary>
/// <remarks>Event id ranges: 5000 lifetime, 5100 customers.</remarks>
internal static partial class ApiLog
{
    [LoggerMessage(5000, LogLevel.Information, "{Application} {Version} started ({Environment}, pid {ProcessId}), logging to {LogDirectory}")]
    public static partial void ApiStarted(this ILogger logger, string application, string version, string environment, int processId, string logDirectory);

    [LoggerMessage(5001, LogLevel.Information, "{Application} stopping")]
    public static partial void ApiStopping(this ILogger logger, string application);

    [LoggerMessage(5100, LogLevel.Information, "Fetched customer {CustomerId}")]
    public static partial void CustomerFetched(this ILogger logger, int customerId);

    [LoggerMessage(5101, LogLevel.Warning, "Rejected customer request with id {CustomerId}")]
    public static partial void CustomerRequestRejected(this ILogger logger, int customerId);
}
