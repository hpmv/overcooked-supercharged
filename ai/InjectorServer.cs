using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Thrift.Protocol;
using Thrift.Transport;

namespace Hpmv {
    public class InjectorServer {
        public void Start() {
            new Thread(() => {
                StartTcpListener();
            }).Start();
            new Thread(() => {
                RunRequestLoop();
            }).Start();
        }

        private void StartTcpListener() {
            var server = new TcpListener(IPAddress.Loopback, 14455);
            server.Start();

            while (true) {
                var client = server.AcceptTcpClient();
                HandleClient(client);
            }
        }

        private void HandleClient(TcpClient client) {
            client.NoDelay = true;
            TTransport transport = new TStreamTransport(client.GetStream(), client.GetStream());
            TProtocol protocol = new TBinaryProtocol(transport);
            var thriftClient = new Interceptor.Client(protocol);
            lock(sync) {
                this.client = thriftClient;
                this.tcpClient = client;
            }
        }

        private void RunRequestLoop() {
            while (true) {
                OutputData output = null;
                try {
                    var time = DateTime.Now;
                    output = this.output.Dequeue(TimeSpan.FromSeconds(4));
                    var delta = DateTime.Now - time;
                    if (delta.TotalMilliseconds > 10) {
                        Console.WriteLine("Time taken to wait for output: " + delta);
                    }
                } catch (Exception) {
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
                        Console.WriteLine("Time taken to get rpc response: " + delta);
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
                    committed = false;
                    try {
                        currentInput = input.Dequeue(TimeSpan.FromSeconds(1));
                    } catch (Exception e) {
                        Console.WriteLine("Timeout: " + e.Message + "\n" + e.StackTrace);
                        currentInput = new InputData();
                    }
                }
                return currentInput;
            }
        }

        public void CommitFrameIfNotCommitted() {
            if (!committed) {
                currentInput = null;
                output.Enqueue(CurrentFrameData);
                CurrentFrameData = new OutputData();
                committed = true;
            }
        }

        private BlockingQueue<OutputData> output = new BlockingQueue<OutputData>();
        private BlockingQueue<InputData> input = new BlockingQueue<InputData>();

        public OutputData CurrentFrameData { get; private set; } = new OutputData();
        private bool committed = false;
        private InputData currentInput = new InputData();
        private object sync = new object();

        private Interceptor.Client client;
        private TcpClient tcpClient;
    }
}