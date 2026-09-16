using System;
using System.Collections.Generic;
using System.Threading;
using System.Diagnostics;

namespace Hpmv {
    class BlockingQueue<T> {
        private Queue<T> _queue = new Queue<T>();

        public T Dequeue(TimeSpan timeout) {
            if (timeout < TimeSpan.Zero) throw new ArgumentOutOfRangeException("timeout");
            var elapsed = Stopwatch.StartNew();
            lock(_queue) {
                while (_queue.Count == 0) {
                    var remaining = timeout - elapsed.Elapsed;
                    if (remaining <= TimeSpan.Zero || !Monitor.Wait(_queue, remaining)) {
                        throw new TimeoutException("Timeout");
                    }
                }
                return _queue.Dequeue();
            }
        }

        public bool TryDequeue(out T data) {
            lock (_queue) {
                if (_queue.Count == 0) { data = default(T); return false; }
                data = _queue.Dequeue(); return true;
            }
        }

        public void Enqueue(T data) {
            if (data == null) throw new ArgumentNullException("data");
            lock(_queue) {
                _queue.Enqueue(data);
                Monitor.Pulse(_queue);
            }
        }

        public int PeekSize() {
            lock (_queue) return _queue.Count;
        }
    }
}
