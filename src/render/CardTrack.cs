// Universe · render · the .cdf97w's picture track coded on the graphics card (CardVp9): recorded into each frame as it is drawn, its
// blocks' decisions and coefficients taken back once the frame is done, and written into the stream by our VP9 writer on a thread of
// its own. The picture never comes back to the processor. One account holds the whole clip under its cap (850 MB for ten minutes):
// the quantiser for each frame set from it as the frame is recorded; still over at the coarsest, a frame repeats the last one.
using System;
using System.Collections.Generic;
using System.Threading;

namespace Universe.Render
{
    internal sealed class CardTrack
    {
        public const int KeyEvery = 60;
        private readonly CardVp9 _card; private readonly Vp9Writer _writer; private readonly Thread _worker;
        private readonly System.Collections.Concurrent.BlockingCollection<Tuple<CardFrame, uint>> _queue = new System.Collections.Concurrent.BlockingCollection<Tuple<CardFrame, uint>>(48);
        private readonly List<byte[]> _frames = new List<byte[]>(); private readonly List<bool> _keys = new List<bool>(); private readonly List<uint> _times = new List<uint>();
        private readonly int _q0; private readonly double _capPerFrame; private readonly object _rc = new object();
        private double _spent, _budget; private int _qNow, _sinceKey; private bool _keyNext = true; private double[] _camPrev;
        // the frames in the order they were filmed (each a frame on the card, or a repeat); the card's come back a frame later
        private readonly Queue<Tuple<bool, uint>> _order = new Queue<Tuple<bool, uint>>(); private readonly Queue<CardFrame> _back = new Queue<CardFrame>();
        public int Width { get { return _card.W; } }
        public int Height { get { return _card.H; } }
        public int Repeated { get; private set; }

        /// <param name="device">The game's own graphics device.</param>
        public CardTrack(IntPtr device, int w, int h, int qIndex, double capBytesPerSecond)
        {
            _card = new CardVp9(device, w, h);
            _writer = new Vp9Writer(null, w, h, qIndex) { KeepFrames = false };
            _q0 = qIndex; _qNow = qIndex; _capPerFrame = capBytesPerSecond / 30 * 0.94; _budget = _capPerFrame * 8;   // (the first whole frame allowed eight frames' worth)
            _worker = new Thread(() =>
            {
                foreach (var f in _queue.GetConsumingEnumerable())
                {
                    _writer.AddCard(f.Item1, 33);
                    var bytes = _writer.LastFrame; _frames.Add(bytes); _keys.Add(_writer.LastKey); _times.Add(f.Item2);
                    if (_capPerFrame > 0)
                        lock (_rc)
                        {
                            _spent += bytes.Length; _budget += _capPerFrame;
                            double over = (_spent - _budget) / _capPerFrame;
                            if (over > 2) _qNow = Math.Min(255, _qNow + (int)Math.Min(32, 2 + over / 2));
                            else if (over < -2 && _qNow > _q0) _qNow = Math.Max(_q0, _qNow - 2);
                        }
                }
            }) { IsBackground = true, Name = "front man writer" };
            _worker.Start();
        }

        /// <summary>Records this frame's coding into the frame's own command list, from the drawn picture; <paramref name="camera"/> the
        /// camera as the universe's equations have it (UniverseView.CameraNow). False: nothing recorded (no slot free yet).</summary>
        public bool Record(IntPtr list, IntPtr backBuffer, int srcW, int srcH, uint ms, double[] camera)
        {
            int q; bool key, repeat;
            lock (_rc)
            {
                q = _qNow; key = _keyNext || _sinceKey >= KeyEvery - 1;
                repeat = !key && _capPerFrame > 0 && q >= 255 && (_spent - _budget) / _capPerFrame > 30;   // (over at the coarsest: the last frame again)
            }
            if (repeat) { lock (_order) _order.Enqueue(Tuple.Create(true, ms)); Repeated++; lock (_rc) _sinceKey++; return true; }
            if (!_card.SlotFree) return false;
            if (!_card.Record(list, backBuffer, IntPtr.Zero, srcW, srcH, q, key, Vp9Writer.LevelFor(q), camera, key ? null : _camPrev, ms)) return false;
            _camPrev = camera; lock (_rc) { _keyNext = false; _sinceKey = key ? 0 : _sinceKey + 1; } lock (_order) _order.Enqueue(Tuple.Create(false, ms));
            return true;
        }
        /// <summary>After the frame's fence: every frame the card has finished, taken back and handed to the writer (the game waits
        /// only when the writer is 48 frames behind).</summary>
        public void Collect()
        {
            _card.Finished();
            for (var f = _card.Take(); f != null; f = _card.Take())
            {
                if (f.Overflow) lock (_rc) _keyNext = true;                           // (over the card's room: written as a repeat; the card's picture went on, so the next is whole)
                lock (_order) _back.Enqueue(f);
            }
            // handed to the writer in the order filmed: a repeat at once, a card frame once it has come back
            lock (_order)
                while (_order.Count > 0)
                {
                    var o = _order.Peek();
                    if (o.Item1) { _order.Dequeue(); _queue.Add(Tuple.Create((CardFrame)null, o.Item2)); }
                    else if (_back.Count > 0) { _order.Dequeue(); _queue.Add(Tuple.Create(_back.Dequeue(), o.Item2)); }
                    else break;
                }
        }
        /// <summary>Every frame written, in order: their bytes, whether each is whole, their times.</summary>
        public void Finish(List<byte[]> frames, List<bool> keys, List<uint> times)
        {
            _queue.CompleteAdding(); _worker.Join();
            frames.AddRange(_frames); keys.AddRange(_keys); times.AddRange(_times);
            _card.Dispose();
        }
    }
}
