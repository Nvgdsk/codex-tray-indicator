using System.Diagnostics;

namespace CodexTray.Tests;

[CollectionDefinition("IPC timeout isolation", DisableParallelization = true)]
public sealed class IpcTimeoutIsolation;

[Collection("IPC timeout isolation")]
public sealed class IpcTimeoutTests
{
    [Fact]
#pragma warning disable xUnit1031 // Deliberate bounded blocking reproduces worker starvation; finally restores the pool.
    public void SendAsync_WhenCancellationTimerCannotRun_StillTimesOut()
    {
        // The test occupies one worker; connecting occupies the other. A timer
        // callback cannot enforce the deadline, so the pipe must enforce it.
        ThreadPool.GetMinThreads(out int minimumWorkers, out int minimumIo);
        ThreadPool.GetMaxThreads(out int maximumWorkers, out int maximumIo);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        Task<IpcResponse?>? send = null;

        try
        {
            Assert.True(ThreadPool.SetMinThreads(1, minimumIo));
            Assert.True(ThreadPool.SetMaxThreads(2, maximumIo));
            var client = new IpcClient(
                $"CodexTray.Tests.{Guid.NewGuid():N}",
                TimeSpan.FromMilliseconds(250),
                TimeSpan.FromSeconds(1));
            var stopwatch = Stopwatch.StartNew();

            send = client.SendAsync(IpcMessage.Query(), true, cancellation.Token);

            Assert.True(send.Wait(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken), "IPC waited for a starved cancellation timer.");
            Assert.Null(send.GetAwaiter().GetResult());
            Assert.True(stopwatch.Elapsed < TimeSpan.FromMilliseconds(750), stopwatch.Elapsed.ToString());
        }
        finally
        {
            cancellation.Cancel();
            ThreadPool.SetMaxThreads(maximumWorkers, maximumIo);
            ThreadPool.SetMinThreads(minimumWorkers, minimumIo);
            if (send is not null)
            {
                try
                {
                    send.GetAwaiter().GetResult();
                }
                catch (OperationCanceledException)
                {
                    // Cleanup cancellation is not an IPC timeout result.
                }
            }
        }
    }
#pragma warning restore xUnit1031
}
