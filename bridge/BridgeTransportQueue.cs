// Adapted from the independently authored Oc2Tas bridge in plugin/TransportQueue.cs.
// Only the namespace changes; original session/save/transport semantics are retained.
using System;
using System.Collections.Generic;
using System.Threading;

namespace SuperchargedPatch.Bridge
{
    [Serializable]
    public sealed class TransportState
    {
        public long connectionsOpened, disconnects, requestsQueued, requestsDispatched, requestsCancelledBeforeDispatch;
        public long lastDisconnectedConnectionId, lastDispatchedConnectionId, lastCancelledConnectionId;
        public long activeConnectionId, dispatchingConnectionId;
        public int pendingRequests;
    }

    internal sealed class TransportConnection
    {
        internal readonly long Id;
        internal bool Closed;
        internal TransportConnection(long id) { Id = id; }
    }

    internal sealed class Envelope
    {
        internal readonly TransportConnection Owner;
        public string json, result;
        public readonly ManualResetEvent complete = new ManualResetEvent(false);
        internal Envelope(TransportConnection owner, string request) { Owner = owner; json = request; }
    }

    // CLR-only transport ownership. Socket detection and Unity dispatch use the
    // same gate, so cancellation cannot interleave between the ownership check
    // and applying a request. A request already dispatching finishes first;
    // its disconnect release is then delivered at the next frame boundary.
    internal sealed class TransportQueue
    {
        private readonly object gate = new object();
        private readonly Queue<Envelope> pending = new Queue<Envelope>();
        private readonly Queue<TransportConnection> disconnected = new Queue<TransportConnection>();
        private long nextConnection;
        private readonly TransportState stats = new TransportState();

        internal TransportConnection Open()
        {
            lock (gate)
            {
                stats.connectionsOpened++;
                stats.activeConnectionId=nextConnection+1;
                return new TransportConnection(++nextConnection);
            }
        }

        internal Envelope Enqueue(TransportConnection owner, string json)
        {
            if (owner == null) throw new ArgumentNullException("owner");
            lock (gate)
            {
                if (owner.Closed) throw new InvalidOperationException("Connection is closed.");
                Envelope envelope = new Envelope(owner, json);
                pending.Enqueue(envelope);
                stats.requestsQueued++;
                return envelope;
            }
        }

        internal void Disconnect(TransportConnection owner)
        {
            if (owner == null) return;
            lock (gate)
            {
                if (owner.Closed) return;
                owner.Closed = true;
                disconnected.Enqueue(owner);
                stats.disconnects++;
                stats.lastDisconnectedConnectionId = owner.Id;
                if(stats.activeConnectionId==owner.Id)stats.activeConnectionId=0;
            }
        }

        // Main-thread callbacks are deliberately serialized with cancellation.
        // Process releases before accepting any subsequent connection's input.
        internal bool DispatchNext(Action<TransportConnection> release, Action<Envelope> execute)
        {
            lock (gate)
            {
                if (disconnected.Count != 0)
                {
                    release(disconnected.Dequeue());
                    return true;
                }
                while (pending.Count != 0)
                {
                    Envelope envelope = pending.Dequeue();
                    if (envelope.Owner.Closed)
                    {
                        stats.requestsCancelledBeforeDispatch++;
                        stats.lastCancelledConnectionId = envelope.Owner.Id;
                        continue;
                    }
                    stats.requestsDispatched++;
                    stats.lastDispatchedConnectionId = envelope.Owner.Id;
                    stats.dispatchingConnectionId = envelope.Owner.Id;
                    try { execute(envelope); }
                    finally { stats.dispatchingConnectionId = 0; }
                    return true;
                }
                return false;
            }
        }

        internal TransportState Capture()
        {
            lock (gate) return new TransportState
            {
                connectionsOpened=stats.connectionsOpened, disconnects=stats.disconnects,
                requestsQueued=stats.requestsQueued, requestsDispatched=stats.requestsDispatched,
                requestsCancelledBeforeDispatch=stats.requestsCancelledBeforeDispatch,
                lastDisconnectedConnectionId=stats.lastDisconnectedConnectionId,
                lastDispatchedConnectionId=stats.lastDispatchedConnectionId,
                lastCancelledConnectionId=stats.lastCancelledConnectionId, pendingRequests=pending.Count,
                activeConnectionId=stats.activeConnectionId, dispatchingConnectionId=stats.dispatchingConnectionId
            };
        }
    }
}
