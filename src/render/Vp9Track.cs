// Universe · render · the .cdf97w's picture track: the frames as they are filmed, through our own VP9 writer, so any player
// (Media Player, a chat app's) shows the clip. The frames are coded in runs of 60, each run starting with a whole frame, each run
// on a thread of its own, so several cores keep up with the filming; a frame that comes while every coder is full is let go (the
// times of the rest are kept, so the clip keeps its timing).
using System;
using System.Collections.Generic;
using System.Threading;

namespace Universe.Render
{
    internal sealed class Vp9Track
    {
        public const int Run = 60;
        private sealed class Gop { public readonly List<byte[]> Frames = new List<byte[]>(); public readonly List<bool> Keys = new List<bool>(); public readonly List<uint> Times = new List<uint>(); public System.Collections.Concurrent.BlockingCollection<Tuple<byte[][], uint>> In = new System.Collections.Concurrent.BlockingCollection<Tuple<byte[][], uint>>(); public Thread Worker; public int Count; }
        private readonly int _w, _h, _q; private readonly Vp9Writer _shape;
        private readonly List<Gop> _gops = new List<Gop>(); private Gop _cur;
        private readonly System.Collections.Concurrent.BlockingCollection<Tuple<byte[], int, int, uint>> _intake = new System.Collections.Concurrent.BlockingCollection<Tuple<byte[], int, int, uint>>(2);
        private readonly Thread _intaker; private int _waiting; private readonly int _maxWaiting;
        public int Width { get { return _w; } }
        public int Height { get { return _h; } }
        public int Dropped { get; private set; }

        private readonly bool _noDrop;
        /// <param name="noDrop">Never let a frame go: the one adding waits while the coders are full.</param>
        public Vp9Track(int w, int h, int qIndex = 120, bool noDrop = false)
        {
            _w = w & ~1; _h = h & ~1; _q = qIndex; _noDrop = noDrop; _shape = new Vp9Writer(null, _w, _h, _q);
            _maxWaiting = noDrop ? 48 : Run * Math.Max(2, Math.Min(6, Environment.ProcessorCount - 2));   // (frames held at most: a few runs' worth; never letting one go, 48, under 600 MB at 4K, then the game waits)
            // the intake: each frame turned into the coder's planes off the game's thread, then handed to its run
            _intaker = new Thread(() =>
            {
                foreach (var f in _intake.GetConsumingEnumerable())
                {
                    if (_noDrop) while (Interlocked.CompareExchange(ref _waiting, 0, 0) >= _maxWaiting) Thread.Sleep(1);
                    else if (Interlocked.CompareExchange(ref _waiting, 0, 0) >= _maxWaiting) { Dropped++; continue; }
                    var planes = _shape.Prepare(f.Item1, f.Item2, f.Item3);
                    if (_cur == null || _cur.Count == Run) StartRun();
                    Interlocked.Increment(ref _waiting); _cur.Count++; _cur.In.Add(Tuple.Create(planes, f.Item4));
                }
                if (_cur != null) _cur.In.CompleteAdding();
            }) { IsBackground = true, Name = "front man intake" };
            _intaker.Start();
        }
        void StartRun()
        {
            if (_cur != null) _cur.In.CompleteAdding();
            var g = new Gop(); _gops.Add(g); _cur = g;
            g.Worker = new Thread(() =>
            {
                var v = new Vp9Writer(null, _w, _h, _q) { KeyEvery = int.MaxValue, KeepFrames = false };
                foreach (var f in g.In.GetConsumingEnumerable()) { v.AddPrepared(f.Item1, 33); g.Frames.Add(v.LastFrame); g.Keys.Add(v.LastKey); g.Times.Add(f.Item2); Interlocked.Decrement(ref _waiting); }
            }) { IsBackground = true, Name = "front man coder", Priority = ThreadPriority.BelowNormal };
            g.Worker.Start();
        }
        /// <summary>A frame (RGB, top row first) filmed at <paramref name="ms"/>; let go when the intake is busy.</summary>
        public void Add(byte[] rgb, int srcW, int srcH, uint ms) { if (_noDrop) _intake.Add(Tuple.Create(rgb, srcW, srcH, ms)); else if (!_intake.TryAdd(Tuple.Create(rgb, srcW, srcH, ms))) Dropped++; }
        /// <summary>Every frame coded, in order: their bytes, whether each is whole, their times.</summary>
        public void Finish(List<byte[]> frames, List<bool> keys, List<uint> times)
        {
            _intake.CompleteAdding(); _intaker.Join();
            foreach (var g in _gops) { g.Worker.Join(); frames.AddRange(g.Frames); keys.AddRange(g.Keys); times.AddRange(g.Times); }
        }
    }
}
