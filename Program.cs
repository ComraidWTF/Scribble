using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using LogDemo.Api;
using LogDemo.Api.Logging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Debugging;
using Serilog.Extensions.Logging;

// Same philosophy as the WPF apps (see LoggingHost):
//  - code only depends on Microsoft.Extensions.Logging.ILogger<T>; Serilog is just the file sink behind it;
//  - filtering is done by the standard "Logging:LogLevel" section (hot-reloadable);
//  - the Serilog logger exists before the host is built, so startup failures end up in the file.

const string ApplicationName = "LogDemo-Api";

SelfLog.Enable(message => System.Diagnostics.Debug.WriteLine("[Serilog] " + message));

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

ApiFileLoggingOptions fileOptions =
    builder.Configuration.GetSection(ApiFileLoggingOptions.SectionName).Get<ApiFileLoggingOptions>() ?? new ApiFileLoggingOptions();

// A server has no "Documents" folder: default to a "logs" folder next to the executable.
// (Under IIS the app pool identity needs write access to it.)
string logDirectory = Path.GetFullPath(Path.Combine(
    AppContext.BaseDirectory,
    Environment.ExpandEnvironmentVariables(string.IsNullOrWhiteSpace(fileOptions.Directory) ? "logs" : fileOptions.Directory)));

Serilog.Core.Logger serilog = new LoggerConfiguration()
    .MinimumLevel.Verbose() // MEL filters, exactly like the WPF apps
    .Enrich.FromLogContext()
    .Enrich.WithThreadId()
    .WriteTo.File(
        // A server runs for weeks, so roll daily instead of one file per session:
        // LogDemo-Api_20261013.log, LogDemo-Api_20261013_001.log (size roll), ...
        path: Path.Combine(logDirectory, ApplicationName + "_.log"),
        outputTemplate: fileOptions.OutputTemplate,
        formatProvider: CultureInfo.InvariantCulture,
        rollingInterval: RollingInterval.Day,
        fileSizeLimitBytes: fileOptions.MaxFileSizeMB * 1024L * 1024L,
        rollOnFileSizeLimit: true,
        // Retention is done by the sink while the process runs; a startup-only cleaner
        // (LogRetentionCleaner) would never run on a server that stays up for months.
        retainedFileCountLimit: fileOptions.MaxRetainedFiles,
        retainedFileTimeLimit: TimeSpan.FromDays(fileOptions.RetentionDays),
        // Unlike the WPF app, an API can log a lot: buffer writes and flush every second.
        buffered: true,
        flushToDiskInterval: TimeSpan.FromSeconds(1),
        shared: false)
    .CreateLogger();

try
{
    // Replace the default providers (Console, Debug, EventLog, EventSource) with ours.
    builder.Logging.ClearProviders();

    // Not AddSerilog(): same reason as in LoggingHost - keep "Logging:LogLevel" in charge.
    builder.Logging.AddProvider(new SerilogLoggerProvider(serilog, dispose: false));
    builder.Logging.AddDebug();
    if (builder.Environment.IsDevelopment())
    {
        builder.Logging.AddConsole();
    }

    // Unhandled exceptions -> logged by the exception handler middleware (category
    // Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware, level Error) and returned
    // as RFC 7807 ProblemDetails that include "traceId" - the same id written on every log line.
    builder.Services.AddProblemDetails();

    WebApplication app = builder.Build();

    app.UseExceptionHandler();
    app.UseStatusCodePages();

    // Session header/footer, like the WPF apps.
    ILogger startupLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(ApplicationName);
    string version = typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
    app.Lifetime.ApplicationStarted.Register(() =>
        startupLogger.ApiStarted(ApplicationName, version, app.Environment.EnvironmentName, Environment.ProcessId, logDirectory));
    app.Lifetime.ApplicationStopping.Register(() => startupLogger.ApiStopping(ApplicationName));

    // Example endpoint: inject ILogger<T> exactly as in the view models / commands.
    app.MapGet("/customers/{id:int}", (int id, ILogger<CustomerEndpoints> logger) =>
    {
        if (id <= 0)
        {
            logger.CustomerRequestRejected(id);
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid customer id");
        }

        logger.CustomerFetched(id);
        return Results.Ok(new { Id = id });
    });

    app.MapGet("/crash", () =>
    {
        throw new InvalidOperationException("Simulated failure");
    });

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException) // HostAbortedException = EF Core tooling, not a crash
{
    // The equivalent of GlobalExceptionHandler.HandleStartupFailure: the host may not exist,
    // so write straight to Serilog.
    serilog.Fatal(ex, "{Application} terminated unexpectedly", ApplicationName);
    throw;
}
finally
{
    serilog.Dispose(); // flushes the buffered file
}
