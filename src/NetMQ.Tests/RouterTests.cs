﻿using System;
using NUnit.Framework;
using System.Text;

namespace NetMQ.Tests 
{
    [TestFixture]
    public class RouterTests 
    {
        [Test]
        public void Mandatory() 
        {
            using (var context = NetMQContext.Create())
            using (var router = context.CreateRouterSocket()) {
                router.Options.RouterMandatory = true;
                router.BindRandomPort("tcp://*");

                Assert.Throws<HostUnreachableException>(() => router.SendMoreFrame("UNKNOWN").SendFrame("Hello"));
            }
        }

        [Test]
        public void ReceiveReadyDot35Bug() 
        {
            // In .NET 3.5, we saw an issue where ReceiveReady would be raised every second despite nothing being received

            using (var context = NetMQContext.Create())
            using (var server = context.CreateRouterSocket()) {
                server.BindRandomPort("tcp://127.0.0.1");
                server.ReceiveReady += (s, e) => {
                    Assert.Fail("Should not receive");
                };

                Assert.IsFalse(server.Poll(TimeSpan.FromMilliseconds(1500)));
            }
        }


        [Test]
        public void TwoMessagesFromRouterToDealer() 
        {
            using (var context = NetMQContext.Create())
            using (var poller = new Poller())
            using (var server = context.CreateRouterSocket())
            using (var client = context.CreateDealerSocket()) {
                var port = server.BindRandomPort("tcp://*");
                client.Connect("tcp://127.0.0.1:" + port);
                poller.AddSocket(client);
                var cnt = 0;
                client.ReceiveReady += (object sender, NetMQSocketEventArgs e) => {
                    var strs = e.Socket.ReceiveMultipartStrings();
                    foreach (var str in strs) {
                        Console.WriteLine(str);
                    }
                    cnt++;
                    if (cnt == 2) {
                        poller.Cancel();
                    }
                };
                byte[] clientId = Encoding.Unicode.GetBytes("ClientId");
                client.Options.Identity = clientId;

                const string request = "GET /\r\n";

                const string response = "HTTP/1.0 200 OK\r\n" +
                        "Content-Type: text/plain\r\n" +
                        "\r\n" +
                        "Hello, World!";

                client.SendFrame(request);

                byte[] serverId = server.ReceiveFrameBytes();
                Assert.AreEqual(request, server.ReceiveFrameString());

                // two messages in a row, not frames
                server.SendMoreFrame(serverId).SendFrame(response);
                server.SendMoreFrame(serverId).SendFrame(response);

                poller.PollTimeout = 1000;
                poller.PollTillCancelled();
            }
        }

    }
}
