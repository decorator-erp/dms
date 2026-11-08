using BuildingBlocks.Messaging;
using Documents.Contracts;
using Documents.Infrastructure.Decorators;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Documents.Application.Tests;

/// <summary>
/// The DI half of <c>DocumentsScopeCoverageTests</c>, mirroring <c>SiteScopeDecoratorWiringTests</c>: every
/// <see cref="IDocumentsScoped"/> message's handler, as the container actually builds it, is wrapped by
/// <see cref="DocumentsScopeDecorator"/> — OUTERMOST, so the 404 answers before validation or the handler
/// sees a payload the caller was never entitled to have checked.
/// </summary>
[Collection(DocumentsApplicationCollection.Name)]
public sealed class DocumentsScopeDecoratorWiringTests(DocumentsApplicationFixture fixture)
{
    private static readonly Type[] ScopedMessages = [.. typeof(IDocumentsScoped).Assembly.GetTypes()
        .Where(t => t is { IsAbstract: false, IsInterface: false } && typeof(IDocumentsScoped).IsAssignableFrom(t))
        .OrderBy(t => t.Name, StringComparer.Ordinal)];

    [Fact]
    public async Task EveryScopedHandler_ResolvesWithTheScopeDecoratorOutermost()
    {
        await using AsyncServiceScope scope = fixture.CreateScope();

        var offenders = new List<string>();
        foreach (Type message in ScopedMessages)
        {
            object? handler = scope.ServiceProvider.GetService(HandlerServiceType(message));
            if (handler is null)
            {
                offenders.Add($"{message.Name}: no handler registered");
            }
            else if (handler.GetType().DeclaringType != typeof(DocumentsScopeDecorator))
            {
                offenders.Add($"{message.Name}: outermost is {handler.GetType().FullName}");
            }
        }

        Assert.Empty(offenders);
    }

    /// <summary>The vacuity check: the walk must actually cover the 24 scoped messages.</summary>
    [Fact]
    public void TheWalkCoversEveryScopedMessage() => Assert.Equal(24, ScopedMessages.Length);

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
