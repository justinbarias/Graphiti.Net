namespace Graphiti.PyCompat;

/// <summary>
/// The asyncio calls of RULES.md R8 on the exclusive scheduler of decision D22.
/// </summary>
/// <remarks>
/// <para>
/// A Python coroutine is a C# lambda here, because a C# async method starts when it is called. A Python task
/// is a <see cref="Task"/> that this class starts with one trip through the ready queue of
/// <see cref="ExclusiveScheduler"/>, as <c>asyncio.create_task</c> does with <c>loop.call_soon</c>.
/// </para>
/// <para>
/// Python cancels a task by a call to <c>task.cancel()</c>. C# cancels through a
/// <see cref="CancellationToken"/>, which code sees only where it passes the token. A call that never looks
/// at its token runs to its end. A canceled token stays canceled, and a Python cancellation is delivered one
/// time. Each member below says what it does with its token. When one token cancels several waiters at once, they
/// wake in the reverse order of their registration on the token. asyncio wakes them in order.
/// </para>
/// <para>
/// An exception that a call throws reaches the awaiter with its own type, never as an
/// <see cref="AggregateException"/>.
/// </para>
/// </remarks>
internal static class Asyncio
{
    /// <summary>Gets the exclusive scheduler that runs all ported code.</summary>
    internal static TaskScheduler Scheduler => ExclusiveScheduler.Instance;

    /// <summary>Gets a value that is true if the caller runs on the exclusive scheduler.</summary>
    internal static bool IsOnLoop => ExclusiveScheduler.Instance.IsCurrent;

    /// <summary>
    /// Starts <paramref name="main"/> on the scheduler and returns its task, as <c>asyncio.run(main())</c> starts a
    /// coroutine on the loop. It does not block the caller. Only the facade, the judge host, and tests call it.
    /// </summary>
    internal static Task<T> Run<T>(Func<Task<T>> main) => Spawn(main, CancellationToken.None);

    /// <inheritdoc cref="Run{T}(Func{Task{T}})"/>
    internal static Task Run(Func<Task> main) => Spawn(Boxed(main), CancellationToken.None);

    /// <summary>
    /// Runs <paramref name="body"/> on the scheduler. A library that calls a lambda (a work function of
    /// <c>Neo4j.Driver</c>, a Polly callback) calls it on the thread pool, so each such lambda in ported code
    /// starts with this call (R8). On the scheduler it calls <paramref name="body"/> at once. Off the scheduler it
    /// queues the body, and the returned task ends when the body ends. It has no Python form.
    /// </summary>
    internal static Task<T> OnLoopAsync<T>(Func<Task<T>> body) =>
        IsOnLoop ? body() : Spawn(body, CancellationToken.None);

    /// <inheritdoc cref="OnLoopAsync{T}(Func{Task{T}})"/>
    internal static Task OnLoopAsync(Func<Task> body) =>
        IsOnLoop ? body() : Spawn(Boxed(body), CancellationToken.None);

    /// <summary>
    /// <c>asyncio.gather(*coroutines)</c>. It starts each call as a task, in input order, and returns the results in
    /// input order. The first failure in time ends the gather with that exception, and the other calls keep running.
    /// </summary>
    /// <remarks>
    /// The caller must be on the scheduler, or the call throws <see cref="InvalidOperationException"/> (R8).
    /// <paramref name="cancellationToken"/> plays the part of a cancellation of the gather: a call that has not
    /// started when the token is canceled never runs and ends canceled. The calls see the token only through their
    /// lambdas. As in Python, the gather ends with <see cref="OperationCanceledException"/> when the first call ends
    /// canceled, not when the token is canceled.
    /// </remarks>
    internal static Task<List<T>> GatherAsync<T>(IReadOnlyList<Func<Task<T>>> calls, CancellationToken cancellationToken = default)
    {
        RequireLoop(nameof(GatherAsync));
        var children = SpawnAll(calls, cancellationToken);
        return GatherCore(children, returnExceptions: false, () => children.Select(Value).ToList());
    }

    /// <summary>
    /// <c>asyncio.gather(*coroutines, return_exceptions=...)</c>. With <paramref name="returnExceptions"/> true it never
    /// throws. It waits for each call, and each result is the value of the call or the exception that it threw,
    /// in input order. A canceled call gives its <see cref="OperationCanceledException"/>.
    /// </summary>
    /// <remarks>
    /// With <paramref name="returnExceptions"/> true each call starts even if <paramref name="cancellationToken"/> is
    /// canceled, so that a gather of a task that <see cref="CreateTask{T}"/> started waits for that task, as the
    /// Python cleanup <c>task.cancel(); await asyncio.gather(task, return_exceptions=True)</c> does. It does not throw
    /// when the token is canceled. Python raises <c>CancelledError</c> when the gather itself is canceled. With false it
    /// is <see cref="GatherAsync{T}(IReadOnlyList{Func{Task{T}}}, CancellationToken)"/> with boxed results.
    /// </remarks>
    internal static Task<List<object?>> GatherAsync<T>(
        IReadOnlyList<Func<Task<T>>> calls, bool returnExceptions, CancellationToken cancellationToken = default)
    {
        RequireLoop(nameof(GatherAsync));
        var children = SpawnAll(calls, returnExceptions ? CancellationToken.None : cancellationToken);
        return GatherCore(children, returnExceptions, () => children.Select(Outcome).ToList());
    }

    /// <summary><c>await asyncio.gather(*coroutines)</c> for calls that return <c>None</c>.</summary>
    /// <inheritdoc cref="GatherAsync{T}(IReadOnlyList{Func{Task{T}}}, CancellationToken)" path="/remarks"/>
    internal static Task GatherAsync(IReadOnlyList<Func<Task>> calls, CancellationToken cancellationToken = default) =>
        GatherAsync([.. calls.Select(Boxed)], cancellationToken);

    /// <summary><c>asyncio.gather(*coroutines, return_exceptions=...)</c> for calls that return <c>None</c>. A value is <c>null</c>.</summary>
    /// <inheritdoc cref="GatherAsync{T}(IReadOnlyList{Func{Task{T}}}, bool, CancellationToken)" path="/remarks"/>
    internal static Task<List<object?>> GatherAsync(
        IReadOnlyList<Func<Task>> calls, bool returnExceptions, CancellationToken cancellationToken = default) =>
        GatherAsync([.. calls.Select(Boxed)], returnExceptions, cancellationToken);

    /// <summary>
    /// <c>asyncio.gather(*tasks, return_exceptions=...)</c> over tasks that exist already, for example a task of
    /// <see cref="CreateTask{T}"/>. The gather waits on each task itself, with no task of its own in between, so the
    /// awaiter resumes after the same trips through the ready queue as in Python. Each result is the value of the task,
    /// or with <paramref name="returnExceptions"/> true the exception of the task.
    /// </summary>
    /// <remarks>
    /// It takes no token, because a token cannot reach a task that runs already. Python cancels the tasks when the gather
    /// is canceled. Cancel a task through the source that <see cref="CreateTask{T}"/> gave. The caller must be on the
    /// scheduler, or the call throws <see cref="InvalidOperationException"/> (R8).
    /// </remarks>
    internal static Task<List<object?>> GatherAsync<T>(IReadOnlyList<Task<T>> tasks, bool returnExceptions)
    {
        RequireLoop(nameof(GatherAsync));
        Task<T>[] children = [.. tasks];
        return GatherCore(children, returnExceptions, () => children.Select(Outcome).ToList());
    }

    /// <summary><c>asyncio.gather(*tasks, return_exceptions=...)</c> over tasks that return <c>None</c>. A value is <c>null</c>.</summary>
    /// <inheritdoc cref="GatherAsync{T}(IReadOnlyList{Task{T}}, bool)" path="/remarks"/>
    internal static Task<List<object?>> GatherAsync(IReadOnlyList<Task> tasks, bool returnExceptions)
    {
        RequireLoop(nameof(GatherAsync));
        Task[] children = [.. tasks];
        return GatherCore(children, returnExceptions, () => children.Select(t => t.IsCompletedSuccessfully ? null : (object?)Failure(t)).ToList());
    }

    /// <summary><c>a, b = await asyncio.gather(f(), g())</c> with two result types.</summary>
    /// <inheritdoc cref="GatherAsync{T}(IReadOnlyList{Func{Task{T}}}, CancellationToken)" path="/remarks"/>
    internal static Task<(T1, T2)> GatherAsync<T1, T2>(
        Func<Task<T1>> first, Func<Task<T2>> second, CancellationToken cancellationToken = default)
    {
        RequireLoop(nameof(GatherAsync));
        var a = Spawn(first, cancellationToken);
        var b = Spawn(second, cancellationToken);
        return GatherCore([a, b], returnExceptions: false, () => (Value(a), Value(b)));
    }

    /// <summary><c>a, b, c = await asyncio.gather(f(), g(), h())</c> with three result types.</summary>
    /// <inheritdoc cref="GatherAsync{T}(IReadOnlyList{Func{Task{T}}}, CancellationToken)" path="/remarks"/>
    internal static Task<(T1, T2, T3)> GatherAsync<T1, T2, T3>(
        Func<Task<T1>> first, Func<Task<T2>> second, Func<Task<T3>> third, CancellationToken cancellationToken = default)
    {
        RequireLoop(nameof(GatherAsync));
        var a = Spawn(first, cancellationToken);
        var b = Spawn(second, cancellationToken);
        var c = Spawn(third, cancellationToken);
        return GatherCore([a, b, c], returnExceptions: false, () => (Value(a), Value(b), Value(c)));
    }

    /// <summary><c>a, b, c, d = await asyncio.gather(f(), g(), h(), i())</c> with four result types.</summary>
    /// <inheritdoc cref="GatherAsync{T}(IReadOnlyList{Func{Task{T}}}, CancellationToken)" path="/remarks"/>
    internal static Task<(T1, T2, T3, T4)> GatherAsync<T1, T2, T3, T4>(
        Func<Task<T1>> first,
        Func<Task<T2>> second,
        Func<Task<T3>> third,
        Func<Task<T4>> fourth,
        CancellationToken cancellationToken = default)
    {
        RequireLoop(nameof(GatherAsync));
        var a = Spawn(first, cancellationToken);
        var b = Spawn(second, cancellationToken);
        var c = Spawn(third, cancellationToken);
        var d = Spawn(fourth, cancellationToken);
        return GatherCore([a, b, c, d], returnExceptions: false, () => (Value(a), Value(b), Value(c), Value(d)));
    }

    /// <summary>
    /// The mechanism of upstream <c>semaphore_gather</c> (<c>helpers.py</c>):
    /// <c>asyncio.gather</c> over each call wrapped in <c>async with asyncio.Semaphore(max_coroutines)</c>.
    /// The ported <c>Helpers.SemaphoreGatherAsync</c> resolves <c>max_coroutines or SEMAPHORE_LIMIT</c> and calls this.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It starts the calls in input order and returns the results in input order. At most
    /// <paramref name="maxCoroutines"/> calls run at one time, and a waiting call starts when a running one ends, in
    /// input order. The first failure in time ends the gather. The other calls are not canceled, and the waiting ones
    /// still start. A negative limit throws <see cref="ArgumentException"/> with the message of
    /// <c>asyncio.Semaphore</c>. A limit of 0 starts no call, as in Python.
    /// </para>
    /// <para>
    /// The caller must be on the scheduler, or the call throws <see cref="InvalidOperationException"/> (R8). When
    /// <paramref name="cancellationToken"/> is canceled, a call that waits for the semaphore never starts and ends
    /// canceled, as a canceled <c>acquire</c> does.
    /// </para>
    /// </remarks>
    internal static Task<List<T>> SemaphoreGatherAsync<T>(
        IReadOnlyList<Func<Task<T>>> calls, long maxCoroutines, CancellationToken cancellationToken = default)
    {
        RequireLoop(nameof(SemaphoreGatherAsync));
        var semaphore = new LoopSemaphore(maxCoroutines);
        return GatherAsync([.. calls.Select(call => Wrap(semaphore, call, cancellationToken))], cancellationToken);
    }

    /// <summary><c>await semaphore_gather(*coroutines)</c> for calls that return <c>None</c>.</summary>
    /// <inheritdoc cref="SemaphoreGatherAsync{T}(IReadOnlyList{Func{Task{T}}}, long, CancellationToken)" path="/remarks"/>
    internal static Task SemaphoreGatherAsync(
        IReadOnlyList<Func<Task>> calls, long maxCoroutines, CancellationToken cancellationToken = default) =>
        SemaphoreGatherAsync([.. calls.Select(Boxed)], maxCoroutines, cancellationToken);

    /// <summary><c>a, b = await semaphore_gather(f(), g())</c> with two result types.</summary>
    /// <inheritdoc cref="SemaphoreGatherAsync{T}(IReadOnlyList{Func{Task{T}}}, long, CancellationToken)" path="/remarks"/>
    internal static Task<(T1, T2)> SemaphoreGatherAsync<T1, T2>(
        Func<Task<T1>> first, Func<Task<T2>> second, long maxCoroutines, CancellationToken cancellationToken = default)
    {
        RequireLoop(nameof(SemaphoreGatherAsync));
        var semaphore = new LoopSemaphore(maxCoroutines);
        return GatherAsync(Wrap(semaphore, first, cancellationToken), Wrap(semaphore, second, cancellationToken), cancellationToken);
    }

    /// <summary><c>a, b, c = await semaphore_gather(f(), g(), h())</c> with three result types.</summary>
    /// <inheritdoc cref="SemaphoreGatherAsync{T}(IReadOnlyList{Func{Task{T}}}, long, CancellationToken)" path="/remarks"/>
    internal static Task<(T1, T2, T3)> SemaphoreGatherAsync<T1, T2, T3>(
        Func<Task<T1>> first, Func<Task<T2>> second, Func<Task<T3>> third, long maxCoroutines, CancellationToken cancellationToken = default)
    {
        RequireLoop(nameof(SemaphoreGatherAsync));
        var semaphore = new LoopSemaphore(maxCoroutines);
        return GatherAsync(
            Wrap(semaphore, first, cancellationToken),
            Wrap(semaphore, second, cancellationToken),
            Wrap(semaphore, third, cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// <c>a, b, c, d = await semaphore_gather(f(), g(), h(), i())</c> with four result types. The one upstream call
    /// with different result types has this form (<c>search/search.py</c>).
    /// </summary>
    /// <inheritdoc cref="SemaphoreGatherAsync{T}(IReadOnlyList{Func{Task{T}}}, long, CancellationToken)" path="/remarks"/>
    internal static Task<(T1, T2, T3, T4)> SemaphoreGatherAsync<T1, T2, T3, T4>(
        Func<Task<T1>> first,
        Func<Task<T2>> second,
        Func<Task<T3>> third,
        Func<Task<T4>> fourth,
        long maxCoroutines,
        CancellationToken cancellationToken = default)
    {
        RequireLoop(nameof(SemaphoreGatherAsync));
        var semaphore = new LoopSemaphore(maxCoroutines);
        return GatherAsync(
            Wrap(semaphore, first, cancellationToken),
            Wrap(semaphore, second, cancellationToken),
            Wrap(semaphore, third, cancellationToken),
            Wrap(semaphore, fourth, cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// <c>asyncio.create_task(coro)</c> and <c>asyncio.ensure_future(coro)</c>. The call starts on the next trip
    /// through the ready queue. <c>task.cancel()</c> is <c>cancel.Cancel()</c>: the call sees the token that it
    /// gets, and a call that has not started when the token is canceled never runs and ends canceled.
    /// </summary>
    /// <remarks>
    /// The task does not see the token of its creator, as a Python task does not see the cancellation of the task
    /// that created it. Off the scheduler it throws <see cref="InvalidOperationException"/>, as Python raises
    /// <c>RuntimeError</c> with no running loop.
    /// </remarks>
    internal static Task<T> CreateTask<T>(Func<CancellationToken, Task<T>> coroutine, out CancellationTokenSource cancel)
    {
        RequireLoop(nameof(CreateTask));
        var source = new CancellationTokenSource();
        var token = source.Token;
        cancel = source;
        return Spawn(() => coroutine(token), token);
    }

    /// <inheritdoc cref="CreateTask{T}(Func{CancellationToken, Task{T}}, out CancellationTokenSource)"/>
    internal static Task CreateTask(Func<CancellationToken, Task> coroutine, out CancellationTokenSource cancel) =>
        CreateTask(Boxed(coroutine), out cancel);

    /// <summary>
    /// <c>asyncio.wait_for(coro, timeout)</c> with the timeout in seconds. <c>None</c> is <c>null</c> and waits with no
    /// limit. The timer runs on <paramref name="timeProvider"/>, the clock that <c>asyncio.sleep</c> uses in the port
    /// (R8), as asyncio has one loop clock for both. At the limit the token that the call gets is canceled. If the call then ends with
    /// <see cref="OperationCanceledException"/>, this throws <see cref="TimeoutException"/> with an empty message, as
    /// <c>str(TimeoutError())</c> is empty, and the cancellation inside. If the call catches the cancellation and
    /// returns a value, the value is returned, as in Python.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A timeout of 0 or less never starts the call and throws <see cref="TimeoutException"/>, as Python does. A
    /// <c>nan</c> timeout ends at once (Python raises <c>TimeoutError</c> too). A timeout above about 49 days, or
    /// infinity, sets no limit.
    /// </para>
    /// <para>
    /// The call gets a token that <paramref name="cancellationToken"/> also cancels. A cancellation through
    /// <paramref name="cancellationToken"/> ends with <see cref="OperationCanceledException"/> and not with a
    /// timeout.
    /// </para>
    /// <para>
    /// It ends only when the call ends. A call that ignores its token runs past the limit, and this waits for it. If that
    /// call then returns a value, the value is returned. In Python the cancellation reaches the call at its next await,
    /// so a call runs past the limit only if it catches the cancellation.
    /// </para>
    /// </remarks>
    internal static async Task<T> WaitForAsync<T>(
        Func<CancellationToken, Task<T>> call, double? timeout, TimeProvider timeProvider, CancellationToken cancellationToken = default)
    {
        if (timeout is not { } seconds)
        {
            return await call(cancellationToken);
        }

        if (seconds <= 0)
        {
            // Python wraps the call in a task, cancels it before its first step, and waits for it. The call never
            // runs. The three yields are the trips through the ready queue of that cancellation (_cancel_and_wait):
            // the step of the canceled task, its done callback, and the wake-up of the caller.
            await Task.Yield();
            await Task.Yield();
            await Task.Yield();
            throw new TimeoutException(string.Empty, new TaskCanceledException());
        }

        var delay = double.IsNaN(seconds) ? TimeSpan.Zero
            : seconds * 1000 < uint.MaxValue - 1 ? TimeSpan.FromSeconds(seconds)
            : Timeout.InfiniteTimeSpan;
        using var timer = new CancellationTokenSource(delay, timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timer.Token);
        try
        {
            return await call(linked.Token);
        }
        catch (OperationCanceledException e) when (timer.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(string.Empty, e);
        }
    }

    /// <inheritdoc cref="WaitForAsync{T}(Func{CancellationToken, Task{T}}, double?, TimeProvider, CancellationToken)"/>
    internal static Task WaitForAsync(
        Func<CancellationToken, Task> call, double? timeout, TimeProvider timeProvider, CancellationToken cancellationToken = default) =>
        WaitForAsync(Boxed(call), timeout, timeProvider, cancellationToken);

    /// <summary>
    /// <c>await asyncio.shield(task)</c>. It ends with the outcome of <paramref name="task"/>. When
    /// <paramref name="cancellationToken"/> is canceled first, it ends with <see cref="OperationCanceledException"/>
    /// and <paramref name="task"/> keeps running, as the awaiter of a shield gets <c>CancelledError</c> and the inner
    /// task does not.
    /// </summary>
    /// <remarks>Start <paramref name="task"/> with <see cref="CreateTask{T}"/>, as Python starts it with <c>ensure_future</c>.</remarks>
    internal static Task<T> ShieldAsync<T>(Task<T> task, CancellationToken cancellationToken = default)
    {
        if (task.IsCompleted)
        {
            return task;
        }

        var outer = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var registration = cancellationToken.Register(() => outer.TrySetCanceled(cancellationToken));
        _ = task.ContinueWith(
            done =>
            {
                registration.Dispose();
                CopyOutcome(done, outer);
            },
            CancellationToken.None,
            TaskContinuationOptions.None,
            TaskScheduler.Current);
        return outer.Task;
    }

    /// <summary>
    /// <c>await asyncio.to_thread(func)</c>. It runs <paramref name="func"/> on the thread pool, and the awaiter
    /// resumes on the scheduler. In ported code it is the one allowed way to leave the scheduler (R8).
    /// </summary>
    /// <remarks>
    /// When <paramref name="cancellationToken"/> is canceled, the awaiter gets <see cref="OperationCanceledException"/>
    /// at once. A <paramref name="func"/> that has started runs to its end, as a Python thread does. One that has not
    /// started never runs.
    /// </remarks>
    internal static Task<T> ToThreadAsync<T>(Func<T> func, CancellationToken cancellationToken = default)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var registration = cancellationToken.Register(() => done.TrySetCanceled(cancellationToken));
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                if (!done.Task.IsCompleted)
                {
                    done.TrySetResult(func());
                }
            }
            catch (Exception e)
            {
                done.TrySetException(e);
            }
            finally
            {
                registration.Dispose();
            }
        });
        return done.Task;
    }

    /// <inheritdoc cref="ToThreadAsync{T}(Func{T}, CancellationToken)"/>
    internal static Task ToThreadAsync(Action func, CancellationToken cancellationToken = default) =>
        ToThreadAsync<object?>(
            () =>
            {
                func();
                return null;
            },
            cancellationToken);

    /// <summary>
    /// Calls a host service (<c>IChatClient</c>, <c>IEmbeddingGenerator</c>, a host <c>ICrossEncoder</c>) on the
    /// thread pool, and the awaiter resumes on the scheduler (decision D33). It has no Python form. Only the lead's
    /// adapters in <c>Graphiti.Internal</c> call it.
    /// </summary>
    /// <remarks>
    /// The host call gets <paramref name="cancellationToken"/>. When the token is canceled, the awaiter gets
    /// <see cref="OperationCanceledException"/> at once, as a Python <c>await</c> on an HTTP call ends at a
    /// cancellation. The exception of the host call reaches the awaiter with its own type.
    /// </remarks>
    internal static Task<T> CallHostAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken = default)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var registration = cancellationToken.Register(() => done.TrySetCanceled(cancellationToken));
        _ = Task.Run(() => call(cancellationToken), CancellationToken.None).ContinueWith(
            host =>
            {
                registration.Dispose();
                CopyOutcome(host, done);
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return done.Task;
    }

    /// <inheritdoc cref="CallHostAsync{T}(Func{CancellationToken, Task{T}}, CancellationToken)"/>
    internal static Task CallHostAsync(Func<CancellationToken, Task> call, CancellationToken cancellationToken = default) =>
        CallHostAsync(Boxed(call), cancellationToken);

    /// <summary>
    /// True if <paramref name="exception"/> is a cancellation in the Python sense (<c>asyncio.CancelledError</c>):
    /// an <see cref="OperationCanceledException"/> while <paramref name="cancellationToken"/> is canceled.
    /// A Python <c>except Exception:</c> is <c>catch (Exception e) when (!Asyncio.IsCancellation(e, cancellationToken))</c>
    /// (R4). The timeout of <c>HttpClient</c> is an <see cref="OperationCanceledException"/> with a token that is
    /// not canceled, and Python catches a timeout, so the type alone does not decide.
    /// </summary>
    internal static bool IsCancellation(Exception exception, CancellationToken cancellationToken) =>
        exception is OperationCanceledException && cancellationToken.IsCancellationRequested;

    private static void RequireLoop(string member)
    {
        if (!IsOnLoop)
        {
            throw new InvalidOperationException(
                $"Asyncio.{member} must run on the exclusive scheduler. Start a library callback with Asyncio.OnLoopAsync (RULES.md R8).");
        }
    }

    /// <summary>
    /// Starts <paramref name="body"/> as a task: one trip through the ready queue, then the body runs, as
    /// <c>loop.create_task</c> does. The returned task ends one trip after the body ends, as the done callbacks of a
    /// Python task are scheduled with <c>call_soon</c>. If <paramref name="token"/> is canceled before the first step,
    /// the body never runs.
    /// </summary>
    private static Task<T> Spawn<T>(Func<Task<T>> body, CancellationToken token)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        ExclusiveScheduler.Instance.Post(() =>
        {
            if (token.IsCancellationRequested)
            {
                done.TrySetCanceled(token);
                return;
            }

            Task<T> step;
            try
            {
                step = body();
            }
            catch (Exception e)
            {
                done.TrySetException(e);
                return;
            }

            if (step.IsCompleted)
            {
                CopyOutcome(step, done);
            }
            else
            {
                _ = step.ContinueWith(
                    t => CopyOutcome(t, done),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    ExclusiveScheduler.Instance);
            }
        });
        return done.Task;
    }

    private static Task<T>[] SpawnAll<T>(IReadOnlyList<Func<Task<T>>> calls, CancellationToken token)
    {
        var children = new Task<T>[calls.Count];
        for (var i = 0; i < children.Length; i++)
        {
            children[i] = Spawn(calls[i], token);
        }

        return children;
    }

    /// <summary>
    /// The outer future of <c>asyncio.gather</c>. Each child reports one trip after it ends (its done callback). The
    /// gather ends with the first failure, or when each child has ended, and its awaiter resumes one trip later.
    /// A child that has ended before the gather starts reports at once, in input order, as Python 3.12 runs the done
    /// callbacks of such children before it returns the outer future. All of this runs on the loop, so the counter
    /// needs no lock.
    /// </summary>
    private static Task<TResult> GatherCore<TResult>(Task[] children, bool returnExceptions, Func<TResult> results)
    {
        if (children.Length == 0)
        {
            return Task.FromResult(results());
        }

        var outer = new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = 0;

        void OnDone(Task done)
        {
            finished++;
            if (outer.Task.IsCompleted)
            {
                return;
            }

            if (!returnExceptions && !done.IsCompletedSuccessfully)
            {
                outer.TrySetException(Failure(done));
                return;
            }

            if (finished == children.Length)
            {
                outer.TrySetResult(results());
            }
        }

        var doneAtStart = new List<Task>();
        foreach (var child in children)
        {
            if (child.IsCompleted)
            {
                doneAtStart.Add(child);
            }
            else
            {
                _ = child.ContinueWith(OnDone, CancellationToken.None, TaskContinuationOptions.None, ExclusiveScheduler.Instance);
            }
        }

        foreach (var child in doneAtStart)
        {
            OnDone(child);
        }

        return outer.Task;
    }

    private static Func<Task<T>> Wrap<T>(LoopSemaphore semaphore, Func<Task<T>> call, CancellationToken token) =>
        async () =>
        {
            await semaphore.AcquireAsync(token);
            try
            {
                return await call();
            }
            finally
            {
                semaphore.Release();
            }
        };

    // GetAwaiter().GetResult() below runs only on a task that has ended, so it never blocks. It gives the value, or
    // throws the exception of the task with its own type.
    private static T Value<T>(Task<T> task) => task.GetAwaiter().GetResult();

    private static object? Outcome<T>(Task<T> task) => task.IsCompletedSuccessfully ? Value(task) : Failure(task);

    private static Exception Failure(Task task)
    {
        try
        {
            task.GetAwaiter().GetResult();
        }
        catch (Exception e)
        {
            return e;
        }

        throw new InvalidOperationException("the task did not fail");
    }

    private static void CopyOutcome<T>(Task<T> from, TaskCompletionSource<T> to)
    {
        if (from.IsCompletedSuccessfully)
        {
            to.TrySetResult(Value(from));
        }
        else
        {
            // The same exception object, also for a cancellation, so the awaiter sees its own type.
            to.TrySetException(Failure(from));
        }
    }

    private static Func<Task<object?>> Boxed(Func<Task> call) =>
        async () =>
        {
            await call();
            return null;
        };

    private static Func<CancellationToken, Task<object?>> Boxed(Func<CancellationToken, Task> call) =>
        async token =>
        {
            await call(token);
            return null;
        };

    /// <summary>
    /// <c>asyncio.Semaphore</c> for <see cref="SemaphoreGatherAsync{T}"/>, used on the loop only. Waiters wake in the
    /// order that they came, one trip after a release, as in Python. A waiter whose token is canceled ends canceled
    /// and is passed over.
    /// </summary>
    private sealed class LoopSemaphore
    {
        private readonly Queue<(TaskCompletionSource Waiter, CancellationTokenRegistration Registration)> waiters = new();
        private long value;

        internal LoopSemaphore(long value)
        {
            if (value < 0)
            {
                throw new ArgumentException("Semaphore initial value must be >= 0");
            }

            this.value = value;
        }

        internal Task AcquireAsync(CancellationToken token)
        {
            if (value > 0)
            {
                value--;
                return Task.CompletedTask;
            }

            var waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var registration = token.Register(() => waiter.TrySetCanceled(token));
            waiters.Enqueue((waiter, registration));
            return waiter.Task;
        }

        internal void Release()
        {
            value++;
            while (value > 0 && waiters.TryDequeue(out var next))
            {
                next.Registration.Dispose();
                if (next.Waiter.TrySetResult())
                {
                    value--;
                }
            }
        }
    }
}

/// <summary>
/// <c>asyncio.Event</c>. Use it on the scheduler only, as a Python event belongs to one loop.
/// </summary>
internal sealed class AsyncEvent
{
    private readonly List<(TaskCompletionSource<bool> Waiter, CancellationTokenRegistration Registration)> waiters = [];
    private bool value;

    /// <summary><c>event.is_set()</c>.</summary>
    internal bool IsSet() => value;

    /// <summary>
    /// <c>event.set()</c>. Each waiter resumes, in the order in which it started to wait, one trip through the ready
    /// queue later. A waiter that has been woken stays woken if <see cref="Clear"/> follows.
    /// </summary>
    internal void Set()
    {
        if (value)
        {
            return;
        }

        value = true;
        foreach (var (waiter, registration) in waiters)
        {
            registration.Dispose();
            waiter.TrySetResult(true);
        }

        waiters.Clear();
    }

    /// <summary><c>event.clear()</c>.</summary>
    internal void Clear() => value = false;

    /// <summary>
    /// <c>await event.wait()</c>. It returns <c>true</c> at once if the event is set. When
    /// <paramref name="cancellationToken"/> is canceled, this waiter ends with <see cref="OperationCanceledException"/>
    /// and the other waiters keep waiting.
    /// </summary>
    internal Task<bool> WaitAsync(CancellationToken cancellationToken = default)
    {
        if (value)
        {
            return Task.FromResult(true);
        }

        waiters.RemoveAll(w => w.Waiter.Task.IsCompleted);
        var waiter = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var registration = cancellationToken.Register(() => waiter.TrySetCanceled(cancellationToken));
        waiters.Add((waiter, registration));
        return waiter.Task;
    }
}
