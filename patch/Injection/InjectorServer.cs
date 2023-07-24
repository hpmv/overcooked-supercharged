using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Thrift.Protocol;
using Thrift.Transport;

namespace Hpmv {
    public class InjectorServer {
        private Thread tcpThread;
        private Thread requestThread;
        private bool stopRequested = false;
        public void Start() {
            tcpThread = new Thread(() => {
                StartTcpListener();
            });
            requestThread = new Thread(() => {
                RunRequestLoop();
            });
            tcpThread.Start();
            requestThread.Start();
        }

        public void Destroy()
        {
            UnityEngine.Debug.Log("Destroying injector server");
            stopRequested = true;
            listener?.Stop();  // interrupts AcceptTcpClient if it's running
            requestThread.Interrupt();  // interrupts dequeue timeout
            requestThread.Join();
            tcpThread.Join();
            UnityEngine.Debug.Log("Destroyed injector server");
        }

        private void StartTcpListener() {
            while (!stopRequested)
            {
                try
                {
                    listener = new TcpListener(IPAddress.Loopback, 14455);
                    listener.Start();

                    while (!stopRequested)
                    {
                        UseClient(listener.AcceptTcpClient());
                    }
                }
                catch (Exception e) {
                    UnityEngine.Debug.LogException(e);
                    Thread.Sleep(100);
                }
                finally
                {
                    listener?.Stop();
                    tcpClient?.Close();
                }
            }
        }

        private void UseClient(TcpClient client) {
            client.NoDelay = true;
            TTransport transport = new TStreamTransport(client.GetStream(), client.GetStream());
            TProtocol protocol = new TBinaryProtocol(transport);
            var thriftClient = new Interceptor.Client(protocol);
            lock(sync) {
                if (this.tcpClient != null)
                {
                    this.tcpClient.Close();
                }
                this.client = thriftClient;
                this.tcpClient = client;
            }
        }

        private void RunRequestLoop() {
            while (!stopRequested) {
                OutputData output;
                try {
                    var time = DateTime.Now;
                    while (this.output.PeekSize() > 1) {
                        Console.WriteLine("WEIRD!!!! Output queue size: " + this.output.PeekSize());
                        this.output.Dequeue(TimeSpan.Zero);
                    }
                    output = this.output.Dequeue(TimeSpan.FromSeconds(2));
                    var delta = DateTime.Now - time;
                    if (delta.TotalMilliseconds > 10) {
                        //Console.WriteLine("Time taken to wait for output: " + delta);
                    }
                } catch (Exception) {
                    this.tcpClient?.Close();
                    this.client = null;
                    continue;
                }
                Interceptor.Client client = null;
                TcpClient tcpClient = null;
                lock(sync) {
                    client = this.client;
                    tcpClient = this.tcpClient;
                }

                InputData input;
                try {
                    var time = DateTime.Now;
                    if (client == null) {
                        input = new InputData();
                    } else {
                        client.send_getNext(output);
                        tcpClient.GetStream().Flush();
                        input = client.recv_getNext();
                    }
                    var delta = DateTime.Now - time;
                    if (delta.TotalMilliseconds > 2) {
                        //Console.WriteLine("Time taken to get rpc response: " + delta);
                    }
                } catch (Exception e) {
                    Console.WriteLine(e.Message + "\n" + e.StackTrace);
                    input = new InputData();
                    lock(sync) {
                        this.client = null;
                        this.tcpClient = null;
                    }
                }
                this.input.Enqueue(input);
            }
        }

        public InputData CurrentInput {
            get {
                if (currentInput == null) {
                    try {
                        while (input.PeekSize() > 1) {
                            Console.WriteLine("WEIRD!!!! Input queue size: " + input.PeekSize());
                            input.Dequeue(TimeSpan.Zero);
                        }
                        currentInput = input.Dequeue(TimeSpan.FromSeconds(2));
                    } catch (Exception e) {
                        Console.WriteLine("Timeout: " + e.Message + "\n" + e.StackTrace);
                        currentInput = new InputData();
                    }
                }
                return currentInput;
            }
        }

        public void CommitFrame() {
            // Make sure the drain previous input.
            if (currentInput == null)
            {
                var unused = CurrentInput;
            }
            currentInput = null;
            output.Enqueue(CurrentFrameData);
            CurrentFrameData = new OutputData();
        }

        private BlockingQueue<OutputData> output = new BlockingQueue<OutputData>();
        private BlockingQueue<InputData> input = new BlockingQueue<InputData>();

        public OutputData CurrentFrameData { get; private set; } = new OutputData();
        private InputData currentInput = new InputData();
        private object sync = new object();

        private TcpListener listener;
        private Interceptor.Client client;
        private TcpClient tcpClient;
    }
}