// The time limit that a test of the search units and of the decorators puts on a body that runs on the scheduler
// (port/RULES.md, R25). The E2E project links this file.
namespace Graphiti.Port.Evaluation;

using Graphiti.PyCompat;

// A time limit for a body that runs on the scheduler (R25). [Fact(Timeout)] cannot end a test whose body blocks the one
// thread of the scheduler: xunit waits for the test, and each later test queues behind the blocked thread, so a
// blocking call in ported code hangs the whole run. The guard waits for the body with a limit, and it ends the process
// with a message if the scheduler does not answer after the limit, so that the run fails and does not hang.
internal static class LoopGuard
{
    internal static async Task<T> RunAsync<T>(Func<Task<T>> body)
    {
        var task = Asyncio.Run(body);
        try
        {
            return await task.WaitAsync(TimeSpan.FromSeconds(4));
        }
        catch (TimeoutException)
        {
            try
            {
                await Asyncio.Run(() => Task.CompletedTask).WaitAsync(TimeSpan.FromSeconds(1));
            }
            catch (TimeoutException)
            {
                Environment.FailFast("The scheduler of the port is blocked: a call in ported code blocks its thread (R8, R25).");
            }

            throw;
        }
    }

    internal static async Task RunAsync(Func<Task> body) =>
        await RunAsync<object?>(async () =>
        {
            await body();
            return null;
        });
}
