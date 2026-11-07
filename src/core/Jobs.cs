// DNA-AND · core · the job system: one worker thread per CPU core, all pulling jobs from one shared queue,
// so heavy work (measuring a planet, building many bodies, packing clips) uses every core.
// Jobs must not touch shared state without their own care: each writes only its own slots.
using System;
using System.Collections.Generic;
using System.Threading;

namespace DnaAnd.Core
{
    internal static class Jobs
    {
        private static readonly Queue<Action> _queue = new Queue<Action>();
        private static readonly object _lock = new object();
        private static Thread[] _workers;

        /// <summary>How many workers there are: one per core.</summary>
        public static int Workers { get { Start(); return _workers.Length; } }

        private static void Start()
        {
            if (_workers != null) return;
            lock (_lock)
            {
                if (_workers != null) return;
                var w = new Thread[Math.Max(1, Environment.ProcessorCount)];
                for (int i = 0; i < w.Length; i++)
                {
                    w[i] = new Thread(Work) { IsBackground = true, Name = "DNA-AND worker " + (i + 1) };   // background: they never hold the game open
                    w[i].Start();
                }
                _workers = w;
            }
        }

        private static void Work()
        {
            while (true)
            {
                Action job;
                lock (_lock)
                {
                    while (_queue.Count == 0) Monitor.Wait(_lock);
                    job = _queue.Dequeue();
                }
                job();
            }
        }

        /// <summary>Queues one job to run on any worker.</summary>
        public static void Run(Action job)
        {
            Start();
            lock (_lock) { _queue.Enqueue(job); Monitor.Pulse(_lock); }
        }

        /// <summary>Runs <paramref name="body"/> for every i from 0 to count-1, spread over all the workers in even
        /// chunks, and waits until all are done. The calling thread helps too. The first error is passed on.</summary>
        public static void For(int count, Action<int> body)
        {
            if (count <= 0) return;
            int chunks = Math.Min(count, Workers * 4), left = chunks;
            Exception err = null;
            var done = new ManualResetEvent(false);
            int next = -1;
            Action runChunks = () =>
            {
                int c;
                while ((c = Interlocked.Increment(ref next)) < chunks)
                {
                    int a = (int)((long)count * c / chunks), b = (int)((long)count * (c + 1) / chunks);
                    try { for (int i = a; i < b; i++) body(i); }
                    catch (Exception e) { Interlocked.CompareExchange(ref err, e, null); }
                    if (Interlocked.Decrement(ref left) == 0) done.Set();
                }
            };
            int helpers = Math.Min(Workers, chunks) - 1;
            for (int h = 0; h < helpers; h++) Run(runChunks);
            runChunks();
            done.WaitOne();
            done.Close();
            if (err != null) throw new InvalidOperationException("A job failed: " + err.Message, err);
        }
    }
}
