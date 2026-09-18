using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Mise.SharedKernel.Infrastructure;

namespace Mise.UnitTests.SharedKernel;

/// <summary>
/// A real <see cref="ServiceProvider"/> rather than a mocked <see cref="IServiceProvider"/>:
/// <see cref="ServiceProviderServiceExtensions.GetServices{T}"/> is an extension method, which
/// Moq cannot usefully intercept — building a tiny real container is simpler and proves the
/// actual DI resolution path <see cref="DomainEventPublisher"/> depends on.
/// </summary>
public class DomainEventPublisherTests
{
    private sealed record TestEvent(string Value) : IDomainEvent;

    private sealed class RecordingHandler(List<TestEvent> received) : IDomainEventHandler<TestEvent>
    {
        public Task HandleAsync(TestEvent domainEvent, CancellationToken cancellationToken)
        {
            received.Add(domainEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingHandler : IDomainEventHandler<TestEvent>
    {
        public Task HandleAsync(TestEvent domainEvent, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Handler failure.");
    }

    private static DomainEventPublisher BuildSut(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        configure(services);
        var provider = services.BuildServiceProvider();
        return new DomainEventPublisher(provider, NullLogger<DomainEventPublisher>.Instance);
    }

    [Fact]
    public async Task PublishAsync_NoHandlersRegistered_CompletesWithoutThrowing()
    {
        var sut = BuildSut(_ => { });

        var act = () => sut.PublishAsync(new TestEvent("x"), CancellationToken.None);

        await act.Should().NotThrowAsync(because: "an event with no subscriber yet is a normal, unfinished-wiring state, not an error.");
    }

    [Fact]
    public async Task PublishAsync_OneHandlerRegistered_InvokesItWithTheEvent()
    {
        var received = new List<TestEvent>();
        var sut = BuildSut(services => services.AddSingleton<IDomainEventHandler<TestEvent>>(new RecordingHandler(received)));
        var domainEvent = new TestEvent("hello");

        await sut.PublishAsync(domainEvent, CancellationToken.None);

        received.Should().ContainSingle().Which.Should().Be(domainEvent);
    }

    [Fact]
    public async Task PublishAsync_MultipleHandlersRegistered_InvokesAllOfThem()
    {
        var receivedA = new List<TestEvent>();
        var receivedB = new List<TestEvent>();
        var sut = BuildSut(services =>
        {
            services.AddSingleton<IDomainEventHandler<TestEvent>>(new RecordingHandler(receivedA));
            services.AddSingleton<IDomainEventHandler<TestEvent>>(new RecordingHandler(receivedB));
        });

        await sut.PublishAsync(new TestEvent("multi"), CancellationToken.None);

        receivedA.Should().ContainSingle();
        receivedB.Should().ContainSingle();
    }

    [Fact]
    public async Task PublishAsync_HandlerThrows_PropagatesTheException()
    {
        var sut = BuildSut(services => services.AddSingleton<IDomainEventHandler<TestEvent>, ThrowingHandler>());

        var act = () => sut.PublishAsync(new TestEvent("boom"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>(
            because: "correction #10 — a cross-module side effect failing must be as visible as any other failure in the same request, never silently swallowed.");
    }
}
