using System;
using System.Collections.Generic;
using System.Threading;

namespace Hpmv {
    class BlockingQueue<T>
    {
        private int _count = 0;
        private Queue<T> _queue = new Queue<T>();

        public T Dequeue(TimeSpan timeout)
        {
            lock (_queue)
            {
                while (_count <= 0) {
                    if (!Monitor.Wait(_queue, timeout)) {
                        throw new TimeoutException("Timeout");
                    }
                }
                _count--;
                return _queue.Dequeue();
            }
        }

        public void Enqueue(T data)
        {
            if (data == null) throw new ArgumentNullException("data");
            lock (_queue)
            {
                _queue.Enqueue(data);
                _count++;
                Monitor.Pulse(_queue);
            }
        }
    }
}