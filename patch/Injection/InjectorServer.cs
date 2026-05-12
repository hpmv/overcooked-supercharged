using System;
using System.Collections.Generic;
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
            Console.WriteLine("InjectorServer.Start called");
            tcpThread = new Thread(() => {
                Console.WriteLine("Injector tcp connector thread starting");
                StartTcpConnector();
            });
            requestThread = new Thread(() => {
                Console.WriteLine("Injector request thread starting");
                RunRequestLoop();
            });
            tcpThread.Start();
            requestThread.Start();
            Console.WriteLine("InjectorServer threads started");
        }

        public void Destroy()
        {
            Console.WriteLine("InjectorServer.Destroy called");
            UnityEngine.Debug.Log("Destroying injector server");
            stopRequested = true;
            tcpClient?.Close();
            tcpThread.Interrupt();
            requestThread.Interrupt();  // interrupts dequeue timeout
            requestThread.Join();
            tcpThread.Join();
            UnityEngine.Debug.Log("Destroyed injector server");
        }

        private void StartTcpConnector() {
            DateTime nextFailureLog = DateTime.MinValue;
            while (!stopRequested)
            {
                TcpClient existingClient;
                lock (sync)
                {
                    existingClient = this.tcpClient;
                }
                if (existingClient != null)
                {
                    if (IsClientConnected(existingClient))
                    {
                        SleepConnector(TimeSpan.FromMilliseconds(250));
                        continue;
                    }

                    Console.WriteLine("Injector controller connection is closed; reconnecting");
                    lock (sync)
                    {
                        if (this.tcpClient == existingClient)
                        {
                            this.client = null;
                            this.tcpClient = null;
                        }
                    }
                    existingClient.Close();
                }

                try
                {
                    var client = ConnectToController();
                    UseClient(client);
                }
                catch (Exception e) {
                    if (DateTime.Now >= nextFailureLog)
                    {
                        Console.WriteLine("Injector connect failed: " + e.Message);
                        nextFailureLog = DateTime.Now.AddSeconds(5);
                    }
                    SleepConnector(TimeSpan.FromMilliseconds(500));
                }
            }
        }

        private TcpClient ConnectToController()
        {
            Exception lastException = null;
            foreach (var address in GetControllerAddresses())
            {
                try
                {
                    Console.WriteLine("Attempting to connect injector to controller on " + address + ":14455");
                    var client = new TcpClient(AddressFamily.InterNetwork);
                    client.NoDelay = true;
                    client.Connect(address, 14455);
                    Console.WriteLine("Injector connected to controller at " + address + ":14455 from " + client.Client.LocalEndPoint);
                    return client;
                }
                catch (Exception e)
                {
                    lastException = e;
                }
            }
            throw lastException ?? new SocketException();
        }

        private IEnumerable<IPAddress> GetControllerAddresses()
        {
            yield return IPAddress.Loopback;

            IPAddress[] hostAddresses;
            try
            {
                hostAddresses = Dns.GetHostAddresses(Dns.GetHostName());
            }
            catch
            {
                yield break;
            }

            foreach (var address in hostAddresses)
            {
                if (address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(address))
                {
                    continue;
                }
                yield return address;
            }
        }

        private void SleepConnector(TimeSpan delay) {
            try {
                Thread.Sleep(delay);
            }
            catch (ThreadInterruptedException) {
            }
        }

        private bool IsClientConnected(TcpClient client)
        {
            try
            {
                var socket = client.Client;
                return !(socket.Poll(0, SelectMode.SelectRead) && socket.Available == 0);
            }
            catch
            {
                return false;
            }
        }

        private void UseClient(TcpClient client) {
            Console.WriteLine("Injector using controller connection");
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
                } catch (TimeoutException) {
                    continue;
                } catch (ThreadInterruptedException) {
                    if (!stopRequested)
                    {
                        continue;
                    }
                    break;
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
                        //Console.WriteLine("Time taken to get rpc response: " + delta);
                    }
                } catch (Exception e) {
                    Console.WriteLine(e.Message + "\n" + e.StackTrace);
                    input = new InputData();
                    lock(sync) {
                        tcpClient?.Close();
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

        private Interceptor.Client client;
        private TcpClient tcpClient;
    }
}
