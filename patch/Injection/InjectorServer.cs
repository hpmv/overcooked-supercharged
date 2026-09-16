using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Thrift.Protocol;
using Thrift.Transport;

namespace Hpmv {
    public class InjectorServer {
        private Thread tcpThread, requestThread;
        private volatile bool stopRequested;
        private readonly object sync = new object();
        private TcpListener listener;
        private TcpClient tcpClient;
        private Interceptor.Client client;
        private long connectionEpoch, observedEpoch, nextExchange, pendingExchange;
        private long publishedObservations, controllerRepliesConsumed, controlPauses;
        private long pausedPollMisses, staleReplies, staleObservations, replacements;
        private long blockingWaits, blockingTimeouts;
        private sealed class Observation { internal OutputData Value; internal long Epoch, Exchange; }
        private sealed class Reply { internal InputData Value; internal long Epoch, Exchange; internal bool FromController; }
        private readonly BlockingQueue<Observation> output = new BlockingQueue<Observation>();
        private readonly BlockingQueue<Reply> input = new BlockingQueue<Reply>();
        private InputData currentInput = new InputData();
        private readonly InputData waitingPause = new InputData { RequestPause = true };
        public OutputData CurrentFrameData { get; private set; } = new OutputData();

        public void Start() {
            tcpThread = new Thread(StartTcpListener) { IsBackground = true };
            requestThread = new Thread(RunRequestLoop) { IsBackground = true };
            tcpThread.Start(); requestThread.Start();
        }
        public void Destroy() {
            stopRequested = true;
            listener?.Stop();
            lock (sync) { tcpClient?.Close(); client = null; tcpClient = null; connectionEpoch++; }
            requestThread?.Interrupt();
            requestThread?.Join(); tcpThread?.Join();
        }
        private void StartTcpListener() {
            while (!stopRequested) {
                try {
                    int port;
                    if (!Int32.TryParse(Environment.GetEnvironmentVariable("OC2SC_PORT"), out port)) port = 14455;
                    listener = new TcpListener(IPAddress.Loopback, port); listener.Start();
                    while (!stopRequested) UseClient(listener.AcceptTcpClient());
                }
                catch (Exception error) {
                    if (!stopRequested) { UnityEngine.Debug.LogException(error); Thread.Sleep(100); }
                }
                finally { listener?.Stop(); }
            }
        }
        private void UseClient(TcpClient socket) {
            socket.NoDelay = true; socket.ReceiveTimeout = 1000; socket.SendTimeout = 1000;
            TTransport transport = new TStreamTransport(socket.GetStream(), socket.GetStream());
            var replacement = new Interceptor.Client(new TBinaryProtocol(transport));
            lock (sync) {
                var previous = tcpClient;
                client = replacement; tcpClient = socket; connectionEpoch++; replacements++;
                WakeRunningWaitWithPause();
                previous?.Close();
            }
        }
        private void RunRequestLoop() {
            while (!stopRequested) {
                Observation observation;
                try { observation = output.Dequeue(TimeSpan.FromSeconds(2)); }
                catch (TimeoutException) { continue; } // Idle is not a broken controller.
                catch (ThreadInterruptedException) { if (stopRequested) return; else continue; }
                Interceptor.Client endpoint;
                TcpClient socket;
                lock (sync) {
                    if (observation.Epoch != connectionEpoch) { staleObservations++; continue; }
                    endpoint = client; socket = tcpClient;
                }
                InputData response;
                try {
                    if (endpoint == null) response = new InputData { RequestPause = true };
                    else {
                        endpoint.send_getNext(observation.Value); socket.GetStream().Flush();
                        response = endpoint.recv_getNext();
                        if (response == null) throw new InvalidOperationException("Controller returned no input response.");
                    }
                }
                catch (Exception error) {
                    Console.WriteLine("Controller exchange failed: " + error.Message);
                    FailConnection(observation.Epoch, endpoint, socket);
                    continue;
                }
                PublishReply(observation, response, endpoint != null);
            }
        }
        // Old RPC failures must never disconnect a newly accepted controller.
        private void FailConnection(long epoch, Interceptor.Client endpoint, TcpClient socket) {
            lock (sync) {
                if (epoch != connectionEpoch || !ReferenceEquals(endpoint, client) || !ReferenceEquals(socket, tcpClient)) return;
                client = null; tcpClient = null; connectionEpoch++;
                WakeRunningWaitWithPause();
                socket?.Close();
            }
        }
        // Called under sync. Wake the retained running wait immediately on a
        // transport transition; this response carries no accepted frame tag.
        private void WakeRunningWaitWithPause() {
            if (pendingExchange != 0)
                input.Enqueue(new Reply { Value = new InputData { RequestPause = true }, Epoch = connectionEpoch,
                    Exchange = pendingExchange, FromController = false });
        }
        private void PublishReply(Observation observation, InputData value, bool fromController) {
            lock (sync) {
                if (observation.Epoch != connectionEpoch || observation.Exchange != pendingExchange) { staleReplies++; return; }
                input.Enqueue(new Reply { Value = value, Epoch = observation.Epoch, Exchange = observation.Exchange, FromController = fromController });
            }
        }
        private void Accept(InputData value, bool fromController) {
            currentInput = SuperchargedPatch.Bridge.NativeSessionBridge.FilterInput(value);
            SuperchargedPatch.TASLogicalButton.MarkNextInputReason(fromController ? "controller-reply" : "control-pause");
            // A controller reply with Input unset is a phase/control response
            // (notably RequestResume), not a new neutral gameplay sample. A
            // locally manufactured control pause still fails safe to neutral.
            SuperchargedPatch.TASLogicalButton.ApplyInputFrame(currentInput, fromController);
            if (fromController) controllerRepliesConsumed++; else controlPauses++;
        }
        private bool AcceptReply(Reply reply) {
            lock (sync) {
                if (reply.Epoch != connectionEpoch || reply.Exchange != pendingExchange) { staleReplies++; return false; }
                pendingExchange = 0;
            }
            Accept(reply.Value, reply.FromController); return true;
        }
        // Main thread only. A missing reply is not an accepted input frame.
        // Connection transitions issue a control-only pause, with no NextFrame.
        public bool TryGetCurrentInput(out InputData value) {
            if (currentInput != null) { value = SuperchargedPatch.Bridge.NativeSessionBridge.FilterInput(currentInput); return true; }
            bool changed;
            lock (sync) {
                changed = observedEpoch != connectionEpoch;
                if (changed) { observedEpoch = connectionEpoch; pendingExchange = 0; }
            }
            if (changed) {
                Accept(new InputData { RequestPause = true }, false);
                value = SuperchargedPatch.Bridge.NativeSessionBridge.FilterInput(currentInput); return true;
            }
            Reply reply;
            while (input.TryDequeue(out reply)) {
                if (!AcceptReply(reply)) continue;
                value = SuperchargedPatch.Bridge.NativeSessionBridge.FilterInput(currentInput); return true;
            }
            value = null; return false;
        }
        public InputData CurrentInput {
            get {
                InputData value;
                if (TryGetCurrentInput(out value)) return value;
                // LateUpdate skips capture/commit when this paused poll misses.
                // Diagnostic readers receive no frame/warp or accepted input.
                if (SuperchargedPatch.Helpers.IsPaused()) return waitingPause;
                blockingWaits++;
                var elapsed = Stopwatch.StartNew();
                try {
                    while (true) {
                        var remaining = TimeSpan.FromSeconds(2) - elapsed.Elapsed;
                        if (remaining <= TimeSpan.Zero) throw new TimeoutException("Controller input timeout.");
                        if (AcceptReply(input.Dequeue(remaining))) return SuperchargedPatch.Bridge.NativeSessionBridge.FilterInput(currentInput);
                        if (TryGetCurrentInput(out value)) return value;
                    }
                }
                catch (TimeoutException) {
                    blockingTimeouts++;
                    lock (sync) {
                        tcpClient?.Close(); client = null; tcpClient = null;
                        connectionEpoch++; observedEpoch = connectionEpoch; pendingExchange = 0;
                    }
                    Accept(new InputData { RequestPause = true }, false);
                    return SuperchargedPatch.Bridge.NativeSessionBridge.FilterInput(currentInput);
                }
            }
        }
        public void SkipPausedCallback() {
            pausedPollMisses++;
            // Keep native messages/registry updates. This scalar describes one
            // Unity callback, not all render callbacks waiting on a reply.
            CurrentFrameData.PhysicsFramesElapsed = 0;
        }
        public void CommitFrame() {
            if (currentInput == null) {
                var unused = CurrentInput;
                if (currentInput == null) throw new InvalidOperationException("Cannot commit an unaccepted paused input.");
            }
            lock (sync) {
                if (pendingExchange != 0) throw new InvalidOperationException("Only one controller exchange may be outstanding.");
                observedEpoch = connectionEpoch;
                pendingExchange = ++nextExchange;
                output.Enqueue(new Observation { Value = CurrentFrameData, Epoch = connectionEpoch, Exchange = pendingExchange });
                publishedObservations++;
                CurrentFrameData = new OutputData(); currentInput = null;
            }
        }
        public object Diagnostics() {
            lock (sync) return new Dictionary<string, object> {
                { "policy", "paused-nonblocking-single-outstanding-exchange; running-wait-retained" },
                { "connectionEpoch", connectionEpoch }, { "controllerConnected", client != null },
                { "pendingExchange", pendingExchange }, { "publishedObservations", publishedObservations },
                { "controllerRepliesConsumed", controllerRepliesConsumed }, { "controlOnlyPauses", controlPauses },
                { "pausedPollMisses", pausedPollMisses }, { "staleRepliesDiscarded", staleReplies },
                { "staleObservationsDiscarded", staleObservations }, { "controllerReplacements", replacements },
                { "runningBlockingWaits", blockingWaits }, { "runningBlockingTimeouts", blockingTimeouts },
                { "queuedObservations", output.PeekSize() }, { "queuedReplies", input.PeekSize() }
            };
        }
    }
}
