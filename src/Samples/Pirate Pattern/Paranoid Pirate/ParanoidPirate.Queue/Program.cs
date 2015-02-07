﻿using System;
using System.Text;

using NetMQ;

namespace ParanoidPirate.Queue
{
    public class Program
    {
        /// <summary>
        ///     does load-balancing with heartbeating on worker tasks to detect
        ///     crashed, blocked or slow running worker tasks    
        /// </summary>
        static void Main ()
        {
            using (var ctx = NetMQContext.Create ())
            using (var frontend = ctx.CreateRouterSocket ())
            using (var backend = ctx.CreateRouterSocket ())
            {
                frontend.Bind (Commons.QUEUE_FRONTEND);
                backend.Bind (Commons.QUEUE_BACKEND);

                var workers = new Workers ();
                var heartbeatAt = DateTime.UtcNow + TimeSpan.FromMilliseconds (Commons.HEARTBEAT_INTERVAL);

                Console.WriteLine ("[QUEUE] Start listening!");

                while (true)
                {
                    // wait for a specifyed time for messages to arrive at the backend (worker)
                    if (backend.Poll (TimeSpan.FromMilliseconds (Commons.PPP_TICK)))
                    {
                        var msg = backend.ReceiveMessage ();
                        // use workers identity for load-balancing
                        var workerIdentity = Unwrap (msg);
                        var worker = new Worker (workerIdentity);
                        workers.Ready (worker);

                        if (msg.FrameCount == 1)
                        {
                            var data = msg[0].ConvertToString ();

                            if (data == Commons.PPP_HEARTBEAT)
                                Console.WriteLine ("[QUEUE <- WORKER] Received a Heartbeat from {0}", workerIdentity);
                            else if (data == Commons.PPP_READY)
                                Console.WriteLine ("[QUEUE <- WORKER] Received a READY form {0}", workerIdentity);
                            else
                                Console.WriteLine ("[QUEUE <- WORKER] ERROR received an invalid message!");
                        }
                        else
                        {
                            Console.WriteLine ("[QUEUE -> CLIENT] sending from {0} {1} ", workerIdentity, PrintMessage (msg));
                            frontend.SendMessage (msg);
                        }
                    }

                    // if we have workers available handle client requests
                    if (workers.Available)
                    {
                        // are any messages available (queued by NetMQ.Socket)
                        if (frontend.Poll (TimeSpan.FromMilliseconds (Commons.PPP_TICK)))
                        {
                            var request = frontend.ReceiveMessage ();
                            // get next available worker
                            var worker = workers.Next ();
                            // wrap message with worker's address
                            var msg = Wrap (worker, request);

                            Console.WriteLine ("[QUEUE -> WORKER] sending from {0} {1} ", worker.ConvertToString (), PrintMessage (msg));
                            backend.SendMessage (msg);
                        }
                    }

                    // now handle heartbeating after sockets have been taken care of
                    if (DateTime.UtcNow > heartbeatAt)
                    {
                        heartbeatAt = DateTime.UtcNow + TimeSpan.FromMilliseconds (Commons.HEARTBEAT_INTERVAL);
                        // send heartbeat to every worker
                        foreach (var worker in workers)
                        {
                            var heartbeat = new NetMQMessage ();

                            heartbeat.Push (new NetMQFrame (Commons.PPP_HEARTBEAT));
                            heartbeat.Push (worker.Identity);

                            Console.WriteLine ("[QUEUE -> WORKER] sending heartbeat!");

                            backend.SendMessage (heartbeat);
                        }
                    }

                    // remove all dead or expired workers
                    workers.Purge ();
                }

                // clean up
                //foreach (var worker in workers)
                //    worker.Dispose ();
            }

        }

        private static NetMQFrame Unwrap (NetMQMessage msg)
        {
            var id = msg.Pop ();
            // forget the empty frame
            msg.Pop ();

            return id;
        }

        private static NetMQMessage Wrap (NetMQFrame identity, NetMQMessage msg)
        {
            var result = new NetMQMessage (msg);

            result.PushEmptyFrame ();
            result.Push (identity);

            return result;
        }

        private static string PrintMessage (NetMQMessage msg)
        {
            var sb = new StringBuilder ();

            foreach (var frame in msg)
                sb.Append ("[" + frame.ConvertToString () + "]");

            return sb.ToString ();
        }
    }
}
