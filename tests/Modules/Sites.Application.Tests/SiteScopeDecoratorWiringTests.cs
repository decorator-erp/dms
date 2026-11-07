using BuildingBlocks.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Sites.Contracts;
using Sites.Infrastructure.Decorators;
using Xunit;

namespace Sites.Application.Tests;

/// <summary>
/// <b>S13.</b> The DI half of <c>SiteScopeCoverageTests</c>. That test proves every message carrying a
/// <c>SiteId</c> is marked <see cref="ISiteScoped"/>; this one proves every marked message's handler,
/// as the container actually builds it, is wrapped by <see cref="SiteScopeDecorator"/> — and wrapped
/// OUTERMOST, so the 404 answers before validation or the handler sees the payload.
///
/// <para><c>SiteScopeRegistration</c> skips a scoped message whose handler is not registered yet, by
/// design. So a scoped message whose handler is registered under the wrong interface — or not at all —
/// would pass the marker test and run with no scope check. Resolving each one from the real container
/// is the only way to see that.</para>
/// </summary>
[Collection(SitesApplicationCollection.Name)]
public sealed class SiteScopeDecoratorWiringTests(SitesApplicationFixture fixture)
{
    private static readonly Type[] ScopedMessages = [.. typeof(ISiteScoped).Assembly.GetTypes()
        .Where(t => t is { IsAbstract: false, IsInterface: false } && typeof(ISiteScoped).IsAssignableFrom(t))
        .OrderBy(t => t.Name, StringComparer.Ordinal)];

    [Fact]
    public async Task EveryISiteScopedHandler_ResolvesWithTheScopeDecoratorOutermost()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();

        var offenders = new List<string>();

        foreach (Type message in ScopedMessages)
        {
            Type handlerType = HandlerServiceType(message);
            object? handler = scope.ServiceProvider.GetService(handlerType);

            if (handler is null)
            {
                offenders.Add($"{message.Name}: no handler registered");
            }
            else if (handler.GetType().DeclaringType != typeof(SiteScopeDecorator))
            {
                offenders.Add($"{message.Name}: outermost is {handler.GetType().FullName}");
            }
        }

        Assert.Empty(offenders);
    }

    /// <summary>The vacuity check: the walk above must actually walk the 18 scoped messages.</summary>
    [Fact]
    public void TheWalkCoversEveryScopedMessage() => Assert.Equal(18, ScopedMessages.Length);

    private static Type HandlerServiceType(Type message)
    {
        if (typeof(ICommand).IsAssignableFrom(message))
        {
            return typeof(ICommandHandler<>).MakeGenericType(message);
        }

        foreach (Type contract in message.GetInterfaces().Where(i => i.IsGenericType))
        {
            Type definition = contract.GetGenericTypeDefinition();
            Type response = contract.GetGenericArguments()[0];

            if (definition == typeof(ICommand<>))
            {
                return typeof(ICommandHandler<,>).MakeGenericType(message, response);
            }

            if (definition == typeof(IQuery<>))
            {
                return typeof(IQueryHandler<,>).MakeGenericType(message, response);
            }
        }

        throw new InvalidOperationException($"{message.Name} is neither a command nor a query.");
    }
}
