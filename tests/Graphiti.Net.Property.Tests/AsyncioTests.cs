namespace Graphiti.PyCompat.Tests;

using Graphiti.PyCompat;

/// <summary>
/// The <see cref="Asyncio"/> helpers. The first test runs each script that asyncio ran in
/// <c>port/tools/generate_asyncio_values.py</c> and compares the traces. The other tests check what Python has no
/// form for: the exclusive scheduler, the hops on and off it, and cancellation through a token. No test waits for
/// real time to decide its result. Gates (<see cref="TaskCompletionSource"/>) force each order.
/// </summary>
public class AsyncioTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(20);

    public static TheoryData<string> ScriptIds => [.. AsyncioScripts.Scripts.Select(s => s.GetProperty("id").GetString() ?? string.Empty)];

    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    private static List<string> Trace(System.Text.Json.JsonElement script) =>
        [.. script.GetProperty("trace").EnumerateArray().Select(e => e.GetString() ?? string.Empty)];

    [Theory]
    [MemberData(nameof(ScriptIds))]
    public async Task Script_trace_matches_asyncio(string id)
    {
        var script = AsyncioScripts.Script(id);
        var expected = script.GetProperty("trace").EnumerateArray().Select(e => e.GetString() ?? string.Empty).ToList();
        var actual = await AsyncioScripts.TraceAsync(script).WaitAsync(Guard, TestToken);
        Assert.Equal(expected, actual);
    }

    // Differences from asyncio that rules of R8 accept. Each test asserts the trace of the helpers. The comment names
    // the asyncio trace, which the generator records in the group "differs". If the helpers come to match asyncio, the
    // second assertion fails, and the rule can go.

    [Fact]
    public async Task Differs_by_rule_cleanup_after_a_caught_cancellation_ends_at_once()
    {
        // asyncio: t:caught CancelledError, t:to_thread -> 1, main:caught CancelledError, main:after, main:-> None.
        // The cancellation is delivered once, and the cleanup runs. The token stays canceled, so the cleanup that passes
        // it ends at once with OperationCanceledException.
        var script = AsyncioScripts.Difference("differs_cleanup_after_cancel");
        var actual = await AsyncioScripts.TraceAsync(script).WaitAsync(Guard, TestToken);
        Assert.Equal(["t:caught CancelledError", "main:caught CancelledError", "main:after", "main:-> None"], actual);
        Assert.NotEqual(Trace(script), actual);
    }

    [Fact]
    public async Task Differs_by_rule_one_cancellation_wakes_waiters_in_reverse_order()
    {
        // asyncio: w1, w2, w3 catch CancelledError in that order. A token runs its callbacks in the reverse order of
        // registration, so the waiters wake w3, w2, w1.
        var script = AsyncioScripts.Difference("differs_cancel_wake_order");
        var actual = await AsyncioScripts.TraceAsync(script).WaitAsync(Guard, TestToken);
        Assert.Equal(
            ["w3:caught CancelledError", "w2:caught CancelledError", "w1:caught CancelledError", "main:caught CancelledError", "main:-> None"],
            actual);
        Assert.NotEqual(Trace(script), actual);
    }

    [Fact]
    public void There_are_at_least_40_scripts_and_part_comes_from_a_seed()
    {
        var ids = ScriptIds.Select(row => row.Data).ToList();
        Assert.True(ids.Count >= 40, $"{ids.Count} scripts");
        Assert.Contains(ids, id => id.StartsWith("random_", StringComparison.Ordinal));
    }

    // The exclusive scheduler.

    [Fact]
    public async Task Code_on_the_scheduler_never_overlaps_and_loses_no_update_over_200_runs()
    {
        var token = TestToken;
        for (var run = 0; run < 200; run++)
        {
            var result = await Asyncio.Run(async () =>
            {
                var shared = new List<int>();
                long counter = 0;
                var inside = false;
                var overlaps = 0;
                var offLoop = 0;

                async Task<int> WorkerAsync(int id)
                {
                    for (var step = 0; step < 5; step++)
                    {
                        // A read, a pause, and a write with no lock. Two segments that ran at the same time would
                        // lose an update or see `inside` set.
                        overlaps += inside ? 1 : 0;
                        inside = true;
                        var before = counter;
                        Thread.SpinWait(30);
                        counter = before + 1;
                        shared.Add(id);
                        inside = false;
                        if (step % 2 == 0)
                        {
                            await Asyncio.ToThreadAsync(() => Thread.SpinWait(20), token);
                        }
                        else
                        {
                            await Asyncio.CallHostAsync(async t => await Task.Yield(), token);
                        }

                        offLoop += Asyncio.IsOnLoop ? 0 : 1;
                    }

                    return id;
                }

                var ids = await Asyncio.GatherAsync([.. Enumerable.Range(0, 16).Select(i => (Func<Task<int>>)(() => WorkerAsync(i)))], token);
                return (counter, shared.Count, overlaps, offLoop, ids);
            }).WaitAsync(Guard, token);

            Assert.Equal(80, result.counter);
            Assert.Equal(80, result.Item2);
            Assert.Equal(0, result.overlaps);
            Assert.Equal(0, result.offLoop);
            Assert.Equal(Enumerable.Range(0, 16), result.ids);
        }
    }

    [Fact]
    public async Task Run_starts_on_the_scheduler_and_returns_without_blocking()
    {
        var gate = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var task = Asyncio.Run(async () =>
        {
            var onLoop = Asyncio.IsOnLoop;
            var value = await gate.Task;
            return (onLoop, Asyncio.IsOnLoop, value);
        });

        // Run returned before the gate opened: it does not block the caller.
        Assert.False(task.IsCompleted);
        Assert.False(Asyncio.IsOnLoop);
        gate.SetResult(5);
        Assert.Equal((true, true, 5), await task.WaitAsync(Guard, TestToken));
    }

    [Fact]
    public async Task An_exception_reaches_the_awaiter_with_its_own_type()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Asyncio.Run<int>(() => throw new KeyNotFoundException("k")).WaitAsync(Guard, TestToken));
        await Assert.ThrowsAsync<FormatException>(() => Asyncio.Run(async () =>
        {
            await Task.Yield();
            throw new FormatException("f");
        }).WaitAsync(Guard, TestToken));

        var token = TestToken;
        var e = await Record.ExceptionAsync(() => Asyncio.Run(() => Asyncio.GatherAsync(
            [
                async () =>
                {
                    await Task.Yield();
                    throw new FormatException("first");
                },
                async () =>
                {
                    await Task.Yield();
                    await Task.Yield();
                    throw new KeyNotFoundException("second");
                },
            ],
            token)).WaitAsync(Guard, token));
        Assert.IsType<FormatException>(e);
        Assert.Equal("first", e.Message);
    }

    [Fact]
    public async Task OnLoopAsync_from_the_thread_pool_runs_the_body_on_the_scheduler()
    {
        var result = await Task.Run(async () =>
        {
            var before = Asyncio.IsOnLoop;
            var inside = await Asyncio.OnLoopAsync(async () =>
            {
                var first = Asyncio.IsOnLoop;
                await Task.Yield();
                var gathered = await Asyncio.GatherAsync([() => Task.FromResult(Asyncio.IsOnLoop)], CancellationToken.None);
                return first && Asyncio.IsOnLoop && gathered[0];
            });
            return (before, inside);
        }).WaitAsync(Guard, TestToken);
        Assert.Equal((false, true), result);
    }

    [Fact]
    public async Task OnLoopAsync_on_the_scheduler_runs_the_body_at_once()
    {
        var order = await Asyncio.Run(async () =>
        {
            var log = new List<string>();
            var task = Asyncio.OnLoopAsync(() =>
            {
                log.Add("body");
                return Task.CompletedTask;
            });
            log.Add("returned");
            await task;
            var typed = Asyncio.OnLoopAsync(() =>
            {
                log.Add("typed body");
                return Task.FromResult(1);
            });
            log.Add("typed returned");
            await typed;
            return log;
        }).WaitAsync(Guard, TestToken);
        Assert.Equal(["body", "returned", "typed body", "typed returned"], order);
    }

    [Fact]
    public async Task The_gather_members_and_CreateTask_throw_off_the_scheduler()
    {
        Func<Task<int>> one = () => Task.FromResult(1);
        Func<Task> none = () => Task.CompletedTask;
        var token = TestToken;
        Action[] calls =
        [
            () => Asyncio.GatherAsync([one], token),
            () => Asyncio.GatherAsync([one], returnExceptions: true, token),
            () => Asyncio.GatherAsync([none], token),
            () => Asyncio.GatherAsync(one, one, token),
            () => Asyncio.GatherAsync(one, one, one, token),
            () => Asyncio.SemaphoreGatherAsync([one], 2, token),
            () => Asyncio.SemaphoreGatherAsync([none], 2, token),
            () => Asyncio.SemaphoreGatherAsync(one, one, 2, token),
            () => Asyncio.SemaphoreGatherAsync(one, one, one, 2, token),
            () => Asyncio.CreateTask(_ => one(), out _),
        ];
        foreach (var call in calls)
        {
            Assert.Throws<InvalidOperationException>(call);
        }

        // A library callback on the thread pool that forgot OnLoopAsync fails, and does not race.
        var e = await Record.ExceptionAsync(() => Asyncio.Run(async () =>
            await Task.Run(() => Asyncio.GatherAsync([one], token), token)).WaitAsync(Guard, token));
        Assert.IsType<InvalidOperationException>(e);
    }

    [Fact]
    public async Task The_tuple_and_untyped_overloads_keep_types_and_order()
    {
        var token = TestToken;
        var result = await Asyncio.Run(async () =>
        {
            var log = new List<string>();
            var pair = await Asyncio.GatherAsync(
                async () =>
                {
                    await Task.Yield();
                    return 1;
                },
                () => Task.FromResult("two"),
                token);
            var triple = await Asyncio.SemaphoreGatherAsync(() => Task.FromResult(1L), () => Task.FromResult('c'), () => Task.FromResult(3.5), 1, token);
            var pairBySemaphore = await Asyncio.SemaphoreGatherAsync(() => Task.FromResult("a"), () => Task.FromResult(2), 1, token);
            var tripleByGather = await Asyncio.GatherAsync(() => Task.FromResult(1), () => Task.FromResult(2), () => Task.FromResult(3), token);
            var four = await Asyncio.SemaphoreGatherAsync(
                async () =>
                {
                    await Task.Yield();
                    return 1L;
                },
                () => Task.FromResult("b"),
                () => Task.FromResult(3.5),
                () => Task.FromResult('d'),
                2,
                token);
            Assert.Equal((1L, "b", 3.5, 'd'), four);
            await Asyncio.GatherAsync(
                [
                    async () =>
                    {
                        await Task.Yield();
                        log.Add("a");
                    },
                    () =>
                    {
                        log.Add("b");
                        return Task.CompletedTask;
                    },
                ],
                token);
            await Asyncio.SemaphoreGatherAsync([() => Async(log, "c"), () => Async(log, "d")], 1, token);
            var untyped = await Asyncio.GatherAsync([() => Async(log, "e"), () => throw new ArgumentException("x")], returnExceptions: true, token);
            return (pair, triple, pairBySemaphore, tripleByGather, log, untyped);
        }).WaitAsync(Guard, token);

        Assert.Equal((1, "two"), result.pair);
        Assert.Equal((1L, 'c', 3.5), result.triple);
        Assert.Equal(("a", 2), result.pairBySemaphore);
        Assert.Equal((1, 2, 3), result.tripleByGather);
        Assert.Equal(["b", "a", "c", "d", "e"], result.log);
        Assert.Null(result.untyped[0]);
        Assert.IsType<ArgumentException>(result.untyped[1]);

        static async Task Async(List<string> log, string text)
        {
            await Task.Yield();
            log.Add(text);
        }
    }

    [Fact]
    public async Task CallHostAsync_runs_the_host_off_the_scheduler_and_resumes_on_it()
    {
        using var source = new CancellationTokenSource();
        var outer = source.Token;
        var result = await Asyncio.Run(async () =>
        {
            var hostOnLoop = true;
            var hostToken = CancellationToken.None;
            var value = await Asyncio.CallHostAsync(
                async t =>
                {
                    hostOnLoop = Asyncio.IsOnLoop;
                    hostToken = t;
                    await Task.Yield();
                    return 7;
                },
                outer);
            var resumed = Asyncio.IsOnLoop;
            var error = await Record.ExceptionAsync(() => Asyncio.CallHostAsync<int>(
                _ => throw new HttpRequestException("down", null, System.Net.HttpStatusCode.BadGateway), outer));
            var onLoopAfterError = Asyncio.IsOnLoop;
            await Asyncio.CallHostAsync(_ => Task.CompletedTask, outer);
            return (hostOnLoop, resumed, value, hostToken == outer, error, onLoopAfterError);
        }).WaitAsync(Guard, TestToken);

        Assert.False(result.hostOnLoop);
        Assert.True(result.resumed);
        Assert.Equal(7, result.value);
        Assert.True(result.Item4, "the host call gets the token of the caller");
        Assert.Equal(System.Net.HttpStatusCode.BadGateway, Assert.IsType<HttpRequestException>(result.error).StatusCode);
        Assert.True(result.onLoopAfterError);
    }

    [Fact]
    public async Task ToThreadAsync_runs_off_the_scheduler_and_resumes_on_it()
    {
        var token = TestToken;
        var result = await Asyncio.Run(async () =>
        {
            var onLoop = await Asyncio.ToThreadAsync(() => Asyncio.IsOnLoop, token);
            var resumed = Asyncio.IsOnLoop;
            var ran = false;
            await Asyncio.ToThreadAsync(() => ran = true, token);
            var error = await Record.ExceptionAsync(() => Asyncio.ToThreadAsync<int>(() => throw new IOException("disk"), token));
            return (onLoop, resumed, ran, error, Asyncio.IsOnLoop);
        }).WaitAsync(Guard, token);

        Assert.False(result.onLoop);
        Assert.True(result.resumed);
        Assert.True(result.ran);
        Assert.IsType<IOException>(result.error);
        Assert.True(result.Item5);
    }

    // Cancellation through a token, member by member (R8).

    [Fact]
    public void IsCancellation_is_true_only_for_OperationCanceledException_with_a_canceled_token()
    {
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        var live = CancellationToken.None;
        Assert.True(Asyncio.IsCancellation(new OperationCanceledException(), canceled.Token));
        Assert.True(Asyncio.IsCancellation(new TaskCanceledException(), canceled.Token));
        Assert.False(Asyncio.IsCancellation(new OperationCanceledException(), live));

        // The timeout of HttpClient: a TaskCanceledException while the caller's token is live. Python catches a timeout.
        Assert.False(Asyncio.IsCancellation(new TaskCanceledException("timeout", new TimeoutException()), live));
        Assert.False(Asyncio.IsCancellation(new TimeoutException(), canceled.Token));
        Assert.False(Asyncio.IsCancellation(new ArgumentException("x"), canceled.Token));
    }

    [Fact]
    public async Task GatherAsync_with_a_canceled_token_starts_no_call_and_ends_canceled()
    {
        var result = await Asyncio.Run(async () =>
        {
            using var source = new CancellationTokenSource();
            var ran = 0;
            var task = Asyncio.GatherAsync(
                [
                    () =>
                    {
                        ran++;
                        return Task.FromResult(1);
                    },
                    () =>
                    {
                        ran++;
                        return Task.FromResult(2);
                    },
                ],
                source.Token);

            // The calls start on the next trip through the loop. A cancellation before that trip keeps them from running.
            await source.CancelAsync();
            var error = await Record.ExceptionAsync(() => task);
            return (ran, error, Asyncio.IsCancellation(error ?? new InvalidOperationException(), source.Token));
        }).WaitAsync(Guard, TestToken);

        Assert.Equal(0, result.ran);
        Assert.IsAssignableFrom<OperationCanceledException>(result.error);
        Assert.True(result.Item3);
    }

    [Fact]
    public async Task GatherAsync_with_returnExceptions_never_throws_and_waits_for_each_call()
    {
        var result = await Asyncio.Run(async () =>
        {
            using var source = new CancellationTokenSource();
            await source.CancelAsync();
            var gate = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
            var task = Asyncio.GatherAsync<long>(
                [
                    () => gate.Task,
                    async () =>
                    {
                        await Task.Yield();
                        source.Token.ThrowIfCancellationRequested();
                        return 2;
                    },
                ],
                returnExceptions: true,
                source.Token);
            await Task.Yield();
            await Task.Yield();
            await Task.Yield();
            var endedEarly = task.IsCompleted;
            gate.SetResult(1);
            return (endedEarly, await task);
        }).WaitAsync(Guard, TestToken);

        Assert.False(result.endedEarly);
        Assert.Equal(1L, result.Item2[0]);
        Assert.IsAssignableFrom<OperationCanceledException>(result.Item2[1]);
    }

    [Fact]
    public async Task SemaphoreGatherAsync_cancellation_keeps_waiting_calls_from_starting()
    {
        var result = await Asyncio.Run(async () =>
        {
            using var source = new CancellationTokenSource();
            var started = new List<int>();
            var first = new AsyncEvent();
            var task = Asyncio.SemaphoreGatherAsync<int>(
                [
                    async () =>
                    {
                        started.Add(0);
                        return await first.WaitAsync(source.Token) ? 0 : -1;
                    },
                    () =>
                    {
                        started.Add(1);
                        return Task.FromResult(1);
                    },
                ],
                1,
                source.Token);
            await Task.Yield();
            await Task.Yield();
            await source.CancelAsync();
            var error = await Record.ExceptionAsync(() => task);
            await Task.Yield();
            await Task.Yield();
            return (started, error);
        }).WaitAsync(Guard, TestToken);

        Assert.Equal([0], result.started);
        Assert.IsAssignableFrom<OperationCanceledException>(result.error);
    }

    [Fact]
    public async Task SemaphoreGatherAsync_with_a_negative_limit_throws_the_Python_message()
    {
        var token = TestToken;
        var error = await Record.ExceptionAsync(() => Asyncio.Run(() => Asyncio.SemaphoreGatherAsync([() => Task.FromResult(1)], -1, token)).WaitAsync(Guard, token));
        Assert.IsType<ArgumentException>(error);
        Assert.Equal("Semaphore initial value must be >= 0", error.Message);
    }

    [Fact]
    public async Task CreateTask_has_its_own_token_and_ignores_the_token_of_its_creator()
    {
        var result = await Asyncio.Run(async () =>
        {
            using var creator = new CancellationTokenSource();
            await creator.CancelAsync();
            var seen = CancellationToken.None;
            var task = Asyncio.CreateTask(
                async t =>
                {
                    seen = t;
                    await Task.Yield();
                    return 3;
                },
                out var cancel);
            using (cancel)
            {
                var value = await task;
                return (value, seen == cancel.Token, seen.IsCancellationRequested);
            }
        }).WaitAsync(Guard, TestToken);

        Assert.Equal((3, true, false), result);
    }

    [Fact]
    public async Task CreateTask_cancel_reaches_a_running_call_through_its_token()
    {
        var result = await Asyncio.Run(async () =>
        {
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var task = Asyncio.CreateTask(
                async t =>
                {
                    started.SetResult();
                    await Task.Delay(Timeout.Infinite, t);
                },
                out var cancel);
            using (cancel)
            {
                await started.Task;
                await cancel.CancelAsync();
                var error = await Record.ExceptionAsync(() => task);
                return (error, Asyncio.IsCancellation(error ?? new InvalidOperationException(), cancel.Token));
            }
        }).WaitAsync(Guard, TestToken);

        Assert.IsAssignableFrom<OperationCanceledException>(result.error);
        Assert.True(result.Item2);
    }

    [Fact]
    public async Task WaitForAsync_cancels_the_call_at_the_limit_and_throws_TimeoutException()
    {
        var token = TestToken;
        var result = await Asyncio.Run(async () =>
        {
            var callToken = CancellationToken.None;
            var error = await Record.ExceptionAsync(() => Asyncio.WaitForAsync(
                async t =>
                {
                    callToken = t;
                    await Task.Delay(Timeout.Infinite, t);
                },
                0.002,
                TimeProvider.System,
                token));
            return (error, callToken.IsCancellationRequested);
        }).WaitAsync(Guard, token);

        var timeout = Assert.IsType<TimeoutException>(result.error);
        Assert.Equal(AsyncioScripts.Meta.GetProperty("timeout_error_text").GetString(), timeout.Message);
        Assert.IsAssignableFrom<OperationCanceledException>(timeout.InnerException);
        Assert.True(result.IsCancellationRequested);
    }

    [Fact]
    public async Task WaitForAsync_runs_its_timer_on_the_given_clock()
    {
        var clock = new ManualClock();
        var token = TestToken;
        var result = await Asyncio.Run(async () =>
        {
            var waiting = new AsyncEvent();
            var task = Asyncio.WaitForAsync(t => waiting.WaitAsync(t), 3600.0, clock, token);
            await Task.Yield();
            clock.Advance(TimeSpan.FromSeconds(3599.999));
            await Task.Yield();
            await Task.Yield();
            var beforeLimit = task.IsCompleted;
            clock.Advance(TimeSpan.FromSeconds(0.001));
            var error = await Record.ExceptionAsync(() => task);
            return (beforeLimit, error);
        }).WaitAsync(Guard, token);

        // The limit is one hour, so only the given clock can end the call within the guard.
        Assert.False(result.beforeLimit, "the timeout fired before the clock reached the limit");
        Assert.IsType<TimeoutException>(result.error);
    }

    [Fact]
    public async Task WaitForAsync_canceled_by_the_caller_throws_OperationCanceledException_and_not_a_timeout()
    {
        var result = await Asyncio.Run(async () =>
        {
            using var source = new CancellationTokenSource();
            var waiting = new AsyncEvent();
            var task = Asyncio.WaitForAsync(t => waiting.WaitAsync(t), 1000.0, TimeProvider.System, source.Token);
            await Task.Yield();
            await source.CancelAsync();
            var error = await Record.ExceptionAsync(() => task);
            return (error, Asyncio.IsCancellation(error ?? new InvalidOperationException(), source.Token));
        }).WaitAsync(Guard, TestToken);

        Assert.IsAssignableFrom<OperationCanceledException>(result.error);
        Assert.True(result.Item2);
    }

    [Fact]
    public async Task WaitForAsync_with_no_limit_passes_the_callers_token()
    {
        using var source = new CancellationTokenSource();
        var outer = source.Token;
        var same = await Asyncio.Run(() => Asyncio.WaitForAsync(t => Task.FromResult(t == outer), null, TimeProvider.System, outer)).WaitAsync(Guard, TestToken);
        Assert.True(same);
    }

    [Fact]
    public async Task ShieldAsync_canceled_ends_the_awaiter_and_the_task_runs_on()
    {
        var result = await Asyncio.Run(async () =>
        {
            using var source = new CancellationTokenSource();
            var gate = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var inner = Asyncio.CreateTask(async _ => await gate.Task + 1, out var cancel);
            using (cancel)
            {
                var shielded = Asyncio.ShieldAsync(inner, source.Token);
                await source.CancelAsync();
                var error = await Record.ExceptionAsync(() => shielded);
                var innerStillRunning = !inner.IsCompleted;
                gate.SetResult(41);
                var value = await inner;
                return (error, innerStillRunning, value);
            }
        }).WaitAsync(Guard, TestToken);

        Assert.IsAssignableFrom<OperationCanceledException>(result.error);
        Assert.True(result.innerStillRunning);
        Assert.Equal(42, result.value);
    }

    [Fact]
    public async Task ToThreadAsync_canceled_ends_the_awaiter_at_once_and_the_thread_runs_to_its_end()
    {
        var token = TestToken;
        using var release = new ManualResetEventSlim();
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var result = await Asyncio.Run(async () =>
        {
            using var source = new CancellationTokenSource();
            var task = Asyncio.ToThreadAsync(
                () =>
                {
                    started.SetResult();
                    release.Wait(token);
                    finished.SetResult();
                },
                source.Token);
            await started.Task;
            await source.CancelAsync();
            var error = await Record.ExceptionAsync(() => task);
            var neverRan = true;
            using var already = new CancellationTokenSource();
            await already.CancelAsync();
            var skipped = await Record.ExceptionAsync(() => Asyncio.ToThreadAsync(() => neverRan = false, already.Token));
            await Asyncio.ToThreadAsync(() => { }, token);
            return (error, finished.Task.IsCompleted, skipped, neverRan);
        }).WaitAsync(Guard, token);

        Assert.IsAssignableFrom<OperationCanceledException>(result.error);
        Assert.False(result.IsCompleted, "the awaiter ended before the thread");
        release.Set();
        await finished.Task.WaitAsync(Guard, token);
        Assert.IsAssignableFrom<OperationCanceledException>(result.skipped);
        Assert.True(result.neverRan);
    }

    [Fact]
    public async Task CallHostAsync_canceled_ends_the_awaiter_and_the_host_sees_the_token()
    {
        var hostSaw = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var result = await Asyncio.Run(async () =>
        {
            using var source = new CancellationTokenSource();
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var task = Asyncio.CallHostAsync(
                async t =>
                {
                    started.SetResult();
                    try
                    {
                        await Task.Delay(Timeout.Infinite, t);
                    }
                    finally
                    {
                        hostSaw.SetResult(t.IsCancellationRequested);
                    }
                },
                source.Token);
            await started.Task;
            await source.CancelAsync();
            var error = await Record.ExceptionAsync(() => task);
            return (error, Asyncio.IsCancellation(error ?? new InvalidOperationException(), source.Token), Asyncio.IsOnLoop);
        }).WaitAsync(Guard, TestToken);

        Assert.IsAssignableFrom<OperationCanceledException>(result.error);
        Assert.True(result.Item2);
        Assert.True(result.IsOnLoop);
        Assert.True(await hostSaw.Task.WaitAsync(Guard, TestToken));
    }

    [Fact]
    public async Task AsyncEvent_wait_canceled_ends_one_waiter_and_set_wakes_the_rest()
    {
        var result = await Asyncio.Run(async () =>
        {
            using var source = new CancellationTokenSource();
            var e = new AsyncEvent();
            var canceled = e.WaitAsync(source.Token);
            var kept = e.WaitAsync(CancellationToken.None);
            await source.CancelAsync();
            var error = await Record.ExceptionAsync(() => canceled);
            var keptWaiting = !kept.IsCompleted;
            e.Set();
            var woke = await kept;
            var again = e.WaitAsync(source.Token);
            return (error, keptWaiting, woke, again.IsCompletedSuccessfully, e.IsSet());
        }).WaitAsync(Guard, TestToken);

        Assert.IsAssignableFrom<OperationCanceledException>(result.error);
        Assert.True(result.keptWaiting);
        Assert.True(result.woke);
        Assert.True(result.IsCompletedSuccessfully, "a wait on a set event ends at once, even with a canceled token");
        Assert.True(result.Item5);
    }

    /// <summary>A clock that moves only when a test calls <see cref="Advance"/>. Timers fire inside that call.</summary>
    private sealed class ManualClock : TimeProvider
    {
        private readonly List<ManualTimer> timers = [];
        private long ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => ticks;

        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddTicks(ticks);

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state);
            timer.Change(dueTime, period);
            return timer;
        }

        public void Advance(TimeSpan by)
        {
            ticks += by.Ticks;
            foreach (var timer in timers.Where(t => t.Due is { } due && due <= ticks).ToList())
            {
                timer.Fire();
            }
        }

        private sealed class ManualTimer(ManualClock clock, TimerCallback callback, object? state) : ITimer
        {
            public long? Due { get; private set; }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                Due = dueTime == Timeout.InfiniteTimeSpan ? null : clock.ticks + dueTime.Ticks;
                if (!clock.timers.Contains(this))
                {
                    clock.timers.Add(this);
                }

                return true;
            }

            public void Fire()
            {
                Due = null;
                callback(state);
            }

            public void Dispose() => Due = null;

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
