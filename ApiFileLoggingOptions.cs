namespace LogDemo.Api.Logging;

/// <summary>The "FileLogging" section, same shape as in the WPF apps.</summary>
internal sealed class ApiFileLoggingOptions
{
    public const string SectionName = "FileLogging";

    // Same as the WPF template plus {TraceId}: one id per HTTP request, also returned to the
    // client in ProblemDetails, so "traceId from the error response" -> grep the log.
    public const string DefaultOutputTemplate =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] (T{ThreadId}) [{TraceId}] {SourceContext}: {Message:lj}{NewLine}{Exception}";

    public string? Directory { get; set; }

    public int RetentionDays { get; set; } = 14;

    public int MaxRetainedFiles { get; set; } = 100;

    public int MaxFileSizeMB { get; set; } = 50;

    public string OutputTemplate { get; set; } = DefaultOutputTemplate;
}
