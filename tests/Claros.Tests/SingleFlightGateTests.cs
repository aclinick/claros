using Claros.Internal;

namespace Claros.Tests;

public class SingleFlightGateTests
{
    [Fact]
    public void Enter_AllowsOneOperationAtATime()
    {
        var gate = new SingleFlightGate();

        gate.Enter("Engine", "a request");
        gate.Exit();
        gate.Enter("Engine", "a request"); // sequential reuse is fine
        gate.Exit();
    }

    [Fact]
    public void Enter_RejectsASecondConcurrentOperation()
    {
        // Fails fast instead of racing the native engine, and instead of silently
        // queueing - which would hide the caller's bug.
        var gate = new SingleFlightGate();
        gate.Enter("Engine", "a synthesis request");

        var ex = Assert.Throws<InvalidOperationException>(
            () => gate.Enter("Engine", "a synthesis request"));

        Assert.Contains("thread hostile", ex.Message);
        Assert.Contains("Engine", ex.Message);
    }

    [Fact]
    public void Exit_ReleasesTheGateForTheNextCaller()
    {
        var gate = new SingleFlightGate();
        gate.Enter("Engine", "a request");
        gate.Exit();

        gate.Enter("Engine", "a request");
        gate.Exit();
    }

    [Fact]
    public async Task Enter_UnderRealContention_AdmitsExactlyOne()
    {
        var gate = new SingleFlightGate();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var admitted = 0;
        var rejected = 0;

        var racers = Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            await start.Task;
            try
            {
                gate.Enter("Engine", "a request");
                Interlocked.Increment(ref admitted);
                await Task.Delay(50);
                gate.Exit();
            }
            catch (InvalidOperationException)
            {
                Interlocked.Increment(ref rejected);
            }
        })).ToArray();

        start.SetResult();
        await Task.WhenAll(racers);

        Assert.Equal(1, admitted);
        Assert.Equal(7, rejected);
    }
}
