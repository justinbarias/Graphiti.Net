namespace Graphiti.PyCompat;

/// <summary>
/// The one process-wide exclusive scheduler of decision D22. It plays the part of the asyncio event loop.
/// </summary>
/// <remarks>
/// <para>
/// One dedicated thread runs the queued tasks one at a time, in the order of the queue (FIFO), as the ready
/// queue of asyncio does. Code between two <c>await</c> points therefore never runs at the same time as
/// other code on the scheduler.
/// </para>
/// <para>
/// An <c>await</c> inside code on the scheduler captures this scheduler, so its continuation comes back
/// here. A continuation that becomes ready on the loop thread itself runs in line. That is the case of
/// <c>await f()</c> on an async method that ends on the loop, which in Python runs in the same task with
/// no trip through the ready queue. A continuation that becomes ready on another thread is queued.
/// </para>
/// <para>
/// Warning: the in-line rule also applies to a plain <see cref="TaskCompletionSource"/> that code on the loop
/// completes. Its waiters then run inside <c>SetResult</c>, in the middle of code that has no <c>await</c>, where
/// asyncio only schedules the waiter. Each <see cref="Asyncio"/> helper creates its sources with
/// <see cref="TaskCreationOptions.RunContinuationsAsynchronously"/>. Code outside the helpers must do the same.
/// </para>
/// </remarks>
internal sealed class ExclusiveScheduler : TaskScheduler
{
    /// <summary>The scheduler of all ported code.</summary>
    internal static readonly ExclusiveScheduler Instance = new();

    private readonly Queue<Task> queue = new();
    private readonly Thread thread;

    private ExclusiveScheduler()
    {
        thread = new Thread(Loop) { IsBackground = true, Name = "Graphiti event loop" };
        thread.Start();
    }

    /// <inheritdoc/>
    public override int MaximumConcurrencyLevel => 1;

    /// <summary>
    /// Gets a value that is true if the caller runs on the loop thread inside a task of this scheduler.
    /// It is false on any other thread, and false for a task of another scheduler.
    /// </summary>
    internal bool IsCurrent => Environment.CurrentManagedThreadId == thread.ManagedThreadId && Current == this;

    /// <summary>Queues <paramref name="action"/> at the end of the ready queue, as <c>loop.call_soon</c> does.</summary>
    internal void Post(Action action) =>
        _ = Task.Factory.StartNew(action, CancellationToken.None, TaskCreationOptions.DenyChildAttach, this);

    /// <inheritdoc/>
    protected override void QueueTask(Task task)
    {
        lock (queue)
        {
            queue.Enqueue(task);
            Monitor.Pulse(queue);
        }
    }

    /// <inheritdoc/>
    protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued) =>
        !taskWasPreviouslyQueued
        && Environment.CurrentManagedThreadId == thread.ManagedThreadId
        && TryExecuteTask(task);

    /// <inheritdoc/>
    protected override IEnumerable<Task> GetScheduledTasks()
    {
        lock (queue)
        {
            return queue.ToArray();
        }
    }

    private void Loop()
    {
        while (true)
        {
            Task task;
            lock (queue)
            {
                while (queue.Count == 0)
                {
                    Monitor.Wait(queue);
                }

                task = queue.Dequeue();
            }

            TryExecuteTask(task);
        }
    }
}
