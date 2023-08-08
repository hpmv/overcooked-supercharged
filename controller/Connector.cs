﻿using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Thrift.Protocol;
using Thrift.Transport;
using Thrift.Transport.Client;

namespace Hpmv
{
    class Connector {
        private Interceptor.IAsync handler;

        public Connector(Interceptor.IAsync handler) {
            this.handler = handler;
        }

        public async Task Connect(CancellationToken token = default) {
            Console.WriteLine("Began connect");
            var client = new TcpClient();
            client.NoDelay = true;
            await client.ConnectAsync("localhost", 14455);
            Console.WriteLine("Connected");

            TTransport transport = new TStreamTransport(client.GetStream(), client.GetStream(), new Thrift.TConfiguration());
            TProtocol protocol = new TBinaryProtocol(transport);
            var server = new Interceptor.AsyncProcessor(handler);
            while (!token.IsCancellationRequested) {
                try {
                    if (!await server.ProcessAsync(protocol, protocol, token)) {
                        break;
                    }
                }
                catch {
                    break;
                }
            }
            client.Close();
        }
    }

    class DebugHandler : Interceptor.IAsync {
        public async Task<InputData> getNext(OutputData output, CancellationToken cancellationToken = default) {
            MemoryStream ms = new MemoryStream();
            TTransport trans = new TStreamTransport(ms, ms, new Thrift.TConfiguration());
            TJsonProtocol prot = new TJsonProtocol(trans);
            await output.WriteAsync(prot, cancellationToken);
            var buf = ms.GetBuffer();
            var json = Encoding.UTF8.GetString(buf, 0, buf.Length);
            Console.WriteLine(json);
            return new InputData();
        }
    }
}
