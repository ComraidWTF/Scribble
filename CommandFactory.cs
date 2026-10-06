using System;
using Prism.Ioc;

namespace LogDemo.App.Net.Commands;

// ---------------------------------------------------------------------------
// Prism command factory using ContainerLocator: replaces DryIoc's
// Func<CustomersViewModel, TCommand> wrappers with a static helper, so the
// view model needs no factory in its constructor. Container-agnostic (works
// on DryIoc, Unity, etc.).
//
// 1. Register the commands in App.RegisterTypes (transient, so every view
//    model instance gets its own commands):
//
//        containerRegistry.Register<LoadCustomersCommand>();
//        containerRegistry.Register<RemoveCustomerCommand>();
//
// 2. In the view model, drop the Func parameters and create the commands:
//
//        public CustomersViewModel(ILogger<CustomersViewModel> logger)
//        {
//            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
//
//            LoadCommand = CommandFactory.Create<LoadCustomersCommand>(this);
//            RemoveCommand = CommandFactory.Create<RemoveCustomerCommand>(this);
//        }
//
// The command classes stay exactly as they are: the view model passed to
// Create fills the constructor parameter of its type, and everything else
// (services, ILogger<TCommand>) is resolved by the container. Nothing is
// cached here; each call returns a new command bound to the given view model.
//
// Unit tests: point ContainerLocator at a test container with
// ContainerLocator.SetContainerExtension(...) and call
// ContainerLocator.ResetContainer() afterwards, since the locator is global.
// ---------------------------------------------------------------------------

/// <summary>Creates commands that need their owning view model at construction time.</summary>
public static class CommandFactory
{
    /// <summary>
    /// Resolves a new <typeparamref name="TCommand"/> from Prism's container, passing
    /// <paramref name="viewModel"/> to the constructor parameter of its type and resolving
    /// all other parameters normally.
    /// </summary>
    /// <exception cref="InvalidOperationException">The Prism container has not been initialised.</exception>
    public static TCommand Create<TCommand>(object viewModel) where TCommand : class
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        // Read at call time, not cached in a static field, so tests can swap the container.
        IContainerProvider container = ContainerLocator.Container
            ?? throw new InvalidOperationException(
                $"Prism's container is not initialised; cannot create {typeof(TCommand).Name}.");

        return (TCommand)container.Resolve(typeof(TCommand), (viewModel.GetType(), viewModel));
    }
}
