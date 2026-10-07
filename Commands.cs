// Combined: BaseCommand, DelegateBaseCommand<T>/DelegateBaseCommand, DelegateBaseAsyncCommand<T>/DelegateBaseAsyncCommand.
// The untyped DelegateBaseCommand / DelegateBaseAsyncCommand expose parameterless Invoke()/InvokeAsync(ct) and CanInvoke().
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;

namespace LogDemo.Logging.Wpf.Commands;

// ===================== BaseCommand =====================
/// <summary>
/// Shared plumbing for <see cref="DelegateBaseCommand{T}"/> and <see cref="DelegateBaseAsyncCommand{T}"/>: every execution
/// runs inside a logging scope (command name + short correlation id) and a trace span, is timed, and
/// failures are logged exactly once.
/// </summary>
public abstract class BaseCommand : ICommand
{
    private protected BaseCommand(ILogger logger)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public event EventHandler? CanExecuteChanged;

    /// <summary>Name used in log messages. Defaults to the class name.</summary>
    public virtual string Name => GetType().Name;

    protected ILogger Logger { get; }

    public bool CanExecute(object? parameter)
    {
        try
        {
            return CanExecuteParameter(parameter);
        }
        catch (Exception ex)
        {
            // CanExecute runs during layout; throwing here would take the UI down.
            Logger.CanExecuteFailed(Name, ex);
            return false;
        }
    }

    public abstract void Execute(object? parameter);

    /// <summary>Re-queries <see cref="CanExecute"/>. Safe to call from any thread.</summary>
    public void RaiseCanExecuteChanged()
    {
        Dispatcher? dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            dispatcher.BeginInvoke(new Action(() => CanExecuteChanged?.Invoke(this, EventArgs.Empty)));
        }
    }

    /// <summary>Converts the untyped parameter and evaluates the derived command's conditions.</summary>
    private protected abstract bool CanExecuteParameter(object? parameter);

    /// <summary>
    /// Casts the ICommand parameter to <typeparamref name="T"/>. <c>null</c> is accepted when <typeparamref name="T"/>
    /// allows it (nullable annotations are not enforced, so a <c>string</c> command can still receive <c>null</c>).
    /// A parameter of the wrong type makes the command disabled rather than throwing during binding.
    /// </summary>
    private protected static bool TryGetParameter<T>(object? parameter, out T value)
    {
        if (parameter is T typed)
        {
            value = typed;
            return true;
        }

        value = default!;
        return parameter is null && default(T) is null;
    }

    /// <summary>
    /// Override for <em>expected</em> failures the command can present itself (inline message, dialog).
    /// Return <c>true</c> when handled; the failure is then logged as a warning and not rethrown.
    /// Unexpected failures (return <c>false</c>) are logged as errors and rethrown to the global handler.
    /// </summary>
    protected virtual bool TryHandleFailure(Exception exception) => false;

    /// <summary>
    /// Everything logged while the command runs - including by services it calls, across awaits -
    /// carries <c>CommandName</c> and <c>CommandId</c>, so one user action can be followed through the log.
    /// When central export is on, the execution is also a span: HTTP calls it makes become child spans,
    /// and its log records carry the trace id, which links Loki and Tempo both ways.
    /// </summary>
    private protected CommandExecution BeginExecution()
    {
        string commandId = Guid.NewGuid().ToString("N").Substring(0, 6);
        Activity? activity = AppTracing.Source.StartActivity(Name);
        activity?.SetTag("command.id", commandId);
        IDisposable? scope = Logger.BeginScope("{CommandName}#{CommandId}", Name, commandId);
        return new CommandExecution(scope, activity);
    }

    /// <summary>Logs the failure and returns whether it was handled (i.e. must not be rethrown).</summary>
    private protected bool HandleFailure(Exception exception, long elapsedMs, CommandExecution execution)
    {
        if (exception is OperationCanceledException)
        {
            Logger.CommandCanceled(Name, elapsedMs);
            return true;
        }

        execution.Failed(exception);

        if (TryHandleFailure(exception))
        {
            Logger.CommandFailedHandled(Name, elapsedMs, exception);
            return true;
        }

        Logger.CommandFailed(Name, elapsedMs, exception);
        exception.MarkAsLogged();
        return false;
    }

    /// <summary>The logging scope and span of one execution; ends both when disposed.</summary>
    private protected sealed class CommandExecution : IDisposable
    {
        private readonly IDisposable? scope;
        private readonly Activity? activity;

        public CommandExecution(IDisposable? scope, Activity? activity)
        {
            this.scope = scope;
            this.activity = activity;
        }

        /// <summary>Marks the span as failed, handled or not: in Tempo both are errors worth seeing.</summary>
        public void Failed(Exception exception)
        {
            this.activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            this.activity?.AddException(exception);
        }

        public void Dispose()
        {
            this.scope?.Dispose();
            this.activity?.Dispose();
        }
    }
}

// ===================== DelegateBaseCommand =====================
/// <summary>
/// Base class for synchronous commands that take a <typeparamref name="T"/> parameter.
/// Derived classes implement <see cref="Invoke"/>.
/// </summary>
public abstract class DelegateBaseCommand<T> : BaseCommand
{
    protected DelegateBaseCommand(ILogger logger)
        : base(logger)
    {
    }

    public sealed override void Execute(object? parameter)
    {
        if (!CanExecute(parameter) || !TryGetParameter(parameter, out T value))
        {
            Logger.CommandSkipped(Name);
            return;
        }

        using (CommandExecution execution = BeginExecution())
        {
            Logger.CommandExecuting(Name);
            Stopwatch stopwatch = Stopwatch.StartNew();
            try
            {
                Invoke(value);
                Logger.CommandCompleted(Name, stopwatch.ElapsedMilliseconds);
            }
            catch (Exception ex) when (HandleFailure(ex, stopwatch.ElapsedMilliseconds, execution))
            {
                // Logged and handled. The filter runs before the stack unwinds, so the log entry
                // still has the command scope; unhandled exceptions propagate to the global handler.
            }
        }
    }

    protected virtual bool CanInvoke(T parameter) => true;

    protected abstract void Invoke(T parameter);

    private protected sealed override bool CanExecuteParameter(object? parameter) =>
        TryGetParameter(parameter, out T value) && CanInvoke(value);
}

/// <summary>Base class for synchronous commands that ignore their parameter.</summary>
public abstract class DelegateBaseCommand : DelegateBaseCommand<object?>
{
    protected DelegateBaseCommand(ILogger logger)
        : base(logger)
    {
    }

    /// <summary>Runs the command from code; the parameter is ignored, so none has to be passed.</summary>
    public void Execute() => Execute(null);

    public bool CanExecute() => CanExecute(null);

    /// <summary>Additional conditions for running the command.</summary>
    protected virtual bool CanInvoke() => true;

    /// <summary>The command's work. Derived classes implement this instead of the parameterised overload.</summary>
    protected abstract void Invoke();

    // The parameter is ignored: seal the object? overloads and route them to the parameterless ones,
    // so derived classes only ever see (and are asked to implement) Invoke() / CanInvoke().
    protected sealed override bool CanInvoke(object? parameter) => CanInvoke();

    protected sealed override void Invoke(object? parameter) => Invoke();
}

// ===================== DelegateBaseAsyncCommand =====================
/// <summary>
/// Base class for asynchronous commands that take a <typeparamref name="T"/> parameter: disables itself
/// while running (no double-clicks), supports cancellation, and never lets an exception escape silently
/// from <c>async void</c>.
/// </summary>
public abstract class DelegateBaseAsyncCommand<T> : BaseCommand
{
    private CancellationTokenSource? cancellation;

    protected DelegateBaseAsyncCommand(ILogger logger)
        : base(logger)
    {
    }

    public bool IsExecuting { get; private set; }

    /// <summary>
    /// ICommand entry point. <c>async void</c> is unavoidable here; <see cref="ExecuteAsync"/> only
    /// throws for unhandled (already logged) failures, which then reach the Dispatcher's global handler.
    /// </summary>
    public sealed override async void Execute(object? parameter)
    {
        if (!TryGetParameter(parameter, out T value))
        {
            Logger.CommandSkipped(Name);
            return;
        }

        await ExecuteAsync(value);
    }

    /// <summary>Awaitable entry point, used by tests and by other code that needs to wait for completion.</summary>
    public async Task ExecuteAsync(T parameter)
    {
        if (!CanExecute(parameter))
        {
            Logger.CommandSkipped(Name);
            return;
        }

        using CancellationTokenSource cancellation = new CancellationTokenSource();
        this.cancellation = cancellation;
        IsExecuting = true;
        RaiseCanExecuteChanged();

        using (CommandExecution execution = BeginExecution())
        {
            Logger.CommandExecuting(Name);
            Stopwatch stopwatch = Stopwatch.StartNew();
            try
            {
                await InvokeAsync(parameter, cancellation.Token);
                Logger.CommandCompleted(Name, stopwatch.ElapsedMilliseconds);
            }
            catch (Exception ex) when (HandleFailure(ex, stopwatch.ElapsedMilliseconds, execution))
            {
                // Logged and handled.
            }
            finally
            {
                this.cancellation = null;
                IsExecuting = false;
                RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>Requests cancellation of the running execution, if any.</summary>
    public void Cancel() => this.cancellation?.Cancel();

    /// <summary>Additional conditions for starting; the "not already running" check is built in.</summary>
    protected virtual bool CanInvoke(T parameter) => true;

    protected abstract Task InvokeAsync(T parameter, CancellationToken cancellationToken);

    private protected sealed override bool CanExecuteParameter(object? parameter) =>
        !IsExecuting && TryGetParameter(parameter, out T value) && CanInvoke(value);
}

/// <summary>Base class for asynchronous commands that ignore their parameter.</summary>
public abstract class DelegateBaseAsyncCommand : DelegateBaseAsyncCommand<object?>
{
    protected DelegateBaseAsyncCommand(ILogger logger)
        : base(logger)
    {
    }

    /// <summary>Awaitable run from code; the parameter is ignored, so none has to be passed.</summary>
    public Task ExecuteAsync() => ExecuteAsync(null);

    public bool CanExecute() => CanExecute(null);

    /// <summary>Additional conditions for starting; the "not already running" check is built in.</summary>
    protected virtual bool CanInvoke() => true;

    /// <summary>The command's work. Derived classes implement this instead of the parameterised overload.</summary>
    protected abstract Task InvokeAsync(CancellationToken cancellationToken);

    // The parameter is ignored: seal the object? overloads and route them to the parameterless ones,
    // so derived classes only ever see (and are asked to implement) InvokeAsync(ct) / CanInvoke().
    protected sealed override bool CanInvoke(object? parameter) => CanInvoke();

    protected sealed override Task InvokeAsync(object? parameter, CancellationToken cancellationToken) =>
        InvokeAsync(cancellationToken);
}

// ===================== Usage =====================
// public sealed class LoadCustomersCommand : DelegateBaseAsyncCommand
// {
//     protected override bool CanInvoke() => !viewModel.IsBusy;
//     protected override async Task InvokeAsync(CancellationToken cancellationToken) { ... }
// }
//
// public sealed class OpenLogFolderCommand : DelegateBaseCommand
// {
//     protected override void Invoke() { ... }
// }
