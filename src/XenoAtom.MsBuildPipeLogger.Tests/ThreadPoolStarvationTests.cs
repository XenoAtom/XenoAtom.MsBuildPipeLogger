// Copyright (c) Dave Glick, Alexandre Mutel.
// Licensed under the MIT license.
// See license.txt file in the project root for full license information.

using System.IO.Pipes;
using System.Runtime.InteropServices;

namespace XenoAtom.MsBuildPipeLogger.Tests;

/// <summary>
/// A host that starts many build processes at once can have every thread of its thread pool blocked, for
/// example on the redirected output of those processes. The server reads on a thread of its own, so it has
/// to keep receiving events then: a logger whose pipe is not read gives up and drops the rest of the build.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ThreadPoolStarvationTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(20);

    [TestMethod]
    public void NamedPipe_DeliversEveryEventWhileTheThreadPoolIsStarved()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Inconclusive("On Unix the runtime completes the accept of a named pipe on the thread pool.");
            return;
        }

        using var starvation = ThreadPoolStarvation.Start();
        if (starvation is null)
        {
            Assert.Inconclusive("The thread pool could not be starved on this machine.");
            return;
        }

        const int messageCount = 2000;
        var pipeName = PipeLoggerServer.CreateUniquePipeName("xa-test");
        using var server = new NamedPipeLoggerServer(pipeName);
        var events = new List<PipeBuildEventArgs>();
        server.AnyEventRaised += e => events.Add(e);
        var readThread = StartThread(server.ReadAll);

        // Two submissions, so that accepting the next client is covered as well as reading.
        var writeThread = StartThread(() =>
        {
            for (var submission = 0; submission < 2; submission++)
            {
                using var writer = ParameterParser.GetPipeFromParameters($"name={pipeName}");
                BuildEventAssertions.WriteEvents(writer, messageCount, includeBuildFinished: true);
            }
        });

        Assert.IsTrue(writeThread.Join(TestTimeout), "The logger could not write its events: the server is not reading the pipe.");
        server.StopListening();
        Assert.IsTrue(readThread.Join(TestTimeout), "The read did not end after the stop.");

        Assert.AreEqual(2, events.OfType<PipeBuildFinishedEventArgs>().Count());
        Assert.AreEqual(2 * messageCount, events.OfType<PipeBuildMessageEventArgs>().Count());
    }

    [TestMethod]
    public void NamedPipe_DisposeEndsAReaderBlockedOnASilentClient()
    {
        var pipeName = PipeLoggerServer.CreateUniquePipeName("xa-test");
        var server = new NamedPipeLoggerServer(pipeName);
        var readThread = StartThread(server.ReadAll);

        // A client that connects and then neither writes nor leaves: the transport is blocked in a read.
        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
        client.Connect(10000);
        Thread.Sleep(200);

        server.Dispose();

        Assert.IsTrue(readThread.Join(TestTimeout), "The read did not end after the dispose.");
        Assert.IsTrue(server.WaitForTransportReader(TestTimeout), "The transport reader is still blocked on the client.");
    }

    private static Thread StartThread(ThreadStart start)
    {
        var thread = new Thread(start) { IsBackground = true };
        thread.Start();
        return thread;
    }

    /// <summary>
    /// Blocks every worker thread of the thread pool until disposed.
    /// </summary>
    private sealed class ThreadPoolStarvation : IDisposable
    {
        private readonly ManualResetEventSlim _release = new(false);
        private readonly int _maxWorkerThreads;
        private readonly int _maxCompletionPortThreads;

        private ThreadPoolStarvation(int maxWorkerThreads, int maxCompletionPortThreads)
        {
            _maxWorkerThreads = maxWorkerThreads;
            _maxCompletionPortThreads = maxCompletionPortThreads;
        }

        public static ThreadPoolStarvation? Start()
        {
            ThreadPool.GetMaxThreads(out var maxWorkerThreads, out var maxCompletionPortThreads);
            ThreadPool.GetMinThreads(out var minWorkerThreads, out _);
            var starvation = new ThreadPoolStarvation(maxWorkerThreads, maxCompletionPortThreads);

            // The pool cannot be limited below its minimum, so that is how many threads have to be blocked.
            if (!ThreadPool.SetMaxThreads(minWorkerThreads, maxCompletionPortThreads))
            {
                starvation.Dispose();
                return null;
            }

            for (var i = 0; i < minWorkerThreads; i++)
            {
                ThreadPool.UnsafeQueueUserWorkItem(_ => starvation._release.Wait(), null);
            }

            // The pool is starved once a work item queued now does not run.
            for (var attempt = 0; attempt < 20; attempt++)
            {
                using var probe = new ManualResetEventSlim(false);
                var probeEvent = probe;
                var abandoned = 0;
                ThreadPool.UnsafeQueueUserWorkItem(_ =>
                {
                    if (Volatile.Read(ref abandoned) == 0)
                    {
                        try
                        {
                            probeEvent.Set();
                        }
                        catch (ObjectDisposedException)
                        {
                        }
                    }
                }, null);

                var ran = probe.Wait(TimeSpan.FromMilliseconds(300));
                Volatile.Write(ref abandoned, 1);
                if (!ran)
                {
                    return starvation;
                }
            }

            starvation.Dispose();
            return null;
        }

        public void Dispose()
        {
            _release.Set();
            ThreadPool.SetMaxThreads(_maxWorkerThreads, _maxCompletionPortThreads);
        }
    }
}
