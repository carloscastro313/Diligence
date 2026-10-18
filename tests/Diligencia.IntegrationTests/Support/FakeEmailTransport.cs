using System.Collections.Concurrent;
using Diligencia.BuildingBlocks.Email;

namespace Diligencia.IntegrationTests.Support;

public sealed class FakeEmailTransport : IEmailTransport
{
    private int _failures;
    public ConcurrentQueue<(string To, string Subject, string Body)> Sent { get; } = new();

    public void FailNext(int n) => Interlocked.Exchange(ref _failures, n);

    public Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken)
    {
        if (Interlocked.Decrement(ref _failures) >= 0) throw new InvalidOperationException("SMTP caído (fake)");
        Interlocked.Exchange(ref _failures, 0);
        Sent.Enqueue((to, subject, body));
        return Task.CompletedTask;
    }
}
