using System.Collections.Concurrent;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;

namespace Diligencia.IntegrationTests.Support;

public sealed class FakeBackgroundJobClient : IBackgroundJobClient
{
    private int _next;
    public ConcurrentQueue<Job> Jobs { get; } = new();

    public string Create(Job job, IState state)
    {
        Jobs.Enqueue(job);
        return Interlocked.Increment(ref _next).ToString();
    }

    public bool ChangeState(string jobId, IState state, string expectedState) => true;
}
