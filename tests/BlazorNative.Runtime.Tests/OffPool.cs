namespace BlazorNative.Runtime.Tests;

/// <summary>
/// RUNS A BLOCKING TEST BODY ON A DEDICATED THREAD (#551). xUnit 2.9 runs test bodies
/// on thread-pool threads, and a body that blocks, in a process wait or a sleep-poll,
/// holds its pool thread for as long as it waits. Early in a run the test process
/// held enough of its own pool threads that a frame crossing the pool once came more
/// than 10 s late. A fact that returns <see cref="Run(Action)"/>'s Task frees its pool
/// thread while the body runs.
///
/// <para><see cref="TaskCreationOptions.LongRunning"/> gives the body its own thread,
/// outside the pool. An exception the body throws faults the Task, so xUnit reports
/// it as the fact's failure.</para>
/// </summary>
internal static class OffPool
{
    internal static Task Run(Action body)
        => Task.Factory.StartNew(body, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    internal static Task<T> Run<T>(Func<T> body)
        => Task.Factory.StartNew(body, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
}
