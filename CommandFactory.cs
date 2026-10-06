using System;
using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace LogDemo.App.Net.Commands;

// ---------------------------------------------------------------------------
// A single command factory for Microsoft.Extensions.DependencyInjection,
// replacing DryIoc's Func<CustomersViewModel, TCommand> wrappers.
//
// 1. Register once at startup:
//
//        services.AddLogging();
//        services.AddTransient<ICommandFactory, CommandFactory>();
//        services.AddTransient<CustomersViewModel>();
//        // plus whatever services the commands depend on.
//        // The commands themselves do NOT need registering.
//
// 2. The view model takes one factory instead of one Func per command:
//
//        public CustomersViewModel(ICommandFactory commandFactory, ILogger<CustomersViewModel> logger)
//        {
//            ArgumentNullException.ThrowIfNull(commandFactory);
//            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
//
//            LoadCommand = commandFactory.Create<LoadCustomersCommand>(this);
//            RemoveCommand = commandFactory.Create<RemoveCustomerCommand>(this);
//        }
//
// The command classes stay exactly as they are: the view model passed to
// Create fills the constructor parameter of its type, and everything else
// (services, ILogger<TCommand>) is resolved from the container.
// ---------------------------------------------------------------------------

/// <summary>Creates commands that need their owning view model at construction time.</summary>
public interface ICommandFactory
{
    /// <summary>
    /// Builds <typeparamref name="TCommand"/>, passing <paramref name="viewModel"/> to the
    /// constructor parameter of its type and resolving all other parameters from the container.
    /// </summary>
    TCommand Create<TCommand>(object viewModel) where TCommand : class;
}

/// <summary><see cref="ActivatorUtilities"/>-backed <see cref="ICommandFactory"/>.</summary>
public sealed class CommandFactory : ICommandFactory
{
    // One compiled constructor delegate per (command type, view model type), shared across instances.
    private static readonly ConcurrentDictionary<(Type Command, Type ViewModel), ObjectFactory> Cache = new();

    private readonly IServiceProvider serviceProvider;

    // Register as transient so this is the current scope's provider, not the root.
    public CommandFactory(IServiceProvider serviceProvider) =>
        this.serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));

    public TCommand Create<TCommand>(object viewModel) where TCommand : class
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        ObjectFactory factory = Cache.GetOrAdd(
            (typeof(TCommand), viewModel.GetType()),
            key => ActivatorUtilities.CreateFactory(key.Command, new[] { key.ViewModel }));

        return (TCommand)factory(this.serviceProvider, new[] { viewModel });
    }
}
