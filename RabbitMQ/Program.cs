using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RabbitMQ.Client;

namespace RabbitMQ
{
    class Program
    {
        private static async Task Main(params string[] args)
        {
            var factory = new ConnectionFactory
            {
                HostName = "127.0.0.1",
                Port = 5672,
                UserName = "guest",
                Password = "guest",
                VirtualHost = "/",
                RequestedConnectionTimeout = (int)TimeSpan.FromSeconds(2).TotalMilliseconds,
                DispatchConsumersAsync = true
            };

            var connection = factory.CreateConnection();
            using (var c = connection.CreateModel())
                c.ExchangeDeclare("test", durable: true, type: "topic");

            var max_parallel = 100;
            var total_millis = 0L;
            var total_count = 0L;
            var total_errors = 0L;

            Action<IConnection> act;
            switch (args?.FirstOrDefault())
            {
                case "per_thread":
                {
                    act = SendMessageCreateChannelPerThread;
                    Console.WriteLine($"Act: {nameof(SendMessageCreateChannelPerThread)}");
                    break;
                }

                case "per_thread_with_lock":
                {
                    act = SendMessageCreateChannelPerThreadWithLock;
                    Console.WriteLine($"Act: {nameof(SendMessageCreateChannelPerThreadWithLock)}");
                    break;
                }

                case "single":
                {
                    act = SendMessageSingleChannel;
                    Console.WriteLine($"Act: {nameof(SendMessageSingleChannel)}");
                    break;
                }

                default:
                {
                    act = SendMessageCreateChannelAlways;
                    Console.WriteLine($"Act: {nameof(SendMessageCreateChannelAlways)}");
                    break;
                }
            }

            var stopwatch_total  = Stopwatch.StartNew();

            var tasks = Enumerable.Range(0, max_parallel).Select(_ => Task.Run(() =>
            {
                var stopwatch = Stopwatch.StartNew();

                try
                {
                    act(connection);
                    stopwatch.Stop();

                    Interlocked.Add(ref total_millis, stopwatch.ElapsedMilliseconds);
                    Interlocked.Increment(ref total_count);
                }
                catch (Exception ex)
                {
                    stopwatch.Stop();
                    Console.WriteLine("Failed to send a message: elapsed time {0}\n{1}", stopwatch.Elapsed, ex);
                    Interlocked.Increment(ref total_errors);
                }
            })).ToArray();

            await Task.WhenAll(tasks);

            stopwatch_total.Stop();

            Console.WriteLine("Sent {0} messages, avg time {1} ms, {2} errors (total_millis: {3})",
                total_count,
                TimeSpan.FromMilliseconds(total_millis / total_count).TotalMilliseconds,
                total_errors,
                total_millis);

            Console.WriteLine("Total time is {0} seconds{1}",
                    stopwatch_total.Elapsed.TotalSeconds,
                    Environment.NewLine);
        }


        private static void SendMessageCreateChannelAlways(IConnection connection)
        {
            using (var c = connection.CreateModel())
            {
                c.ConfirmSelect();
                c.BasicPublish("test", "test-message", body: Encoding.UTF8.GetBytes("test"));
                c.WaitForConfirmsOrDie(TimeSpan.FromSeconds(1));
            }
        }


        [ThreadStatic]
        private static IModel channel;


        private static void SendMessageCreateChannelPerThread(IConnection connection)
        {
            if (channel == null)
            {
                channel = connection.CreateModel();
                channel.ConfirmSelect();
            }

            channel.BasicPublish("test", "test-message", body: Encoding.UTF8.GetBytes("test"));

            channel.WaitForConfirmsOrDie(TimeSpan.FromSeconds(1));
        }


        private static readonly object CreateChannelLock = new object();


        private static void SendMessageCreateChannelPerThreadWithLock(IConnection connection)
        {
            if (channel == null)
            {
                lock (CreateChannelLock)
                {
                    if (channel == null)
                    {
                        channel = connection.CreateModel();
                        channel.ConfirmSelect();
                    }
                }
            }

            channel.BasicPublish("test", "test-message", body: Encoding.UTF8.GetBytes("test"));

            channel.WaitForConfirmsOrDie(TimeSpan.FromSeconds(1));
        }


        private static IModel singleChannel;


        private static void SendMessageSingleChannel(IConnection connection)
        {
            lock (CreateChannelLock)
            {
                if (singleChannel == null)
                {
                    singleChannel = connection.CreateModel();
                    singleChannel.ConfirmSelect();
                }

                singleChannel.BasicPublish("test", "test-message", body: Encoding.UTF8.GetBytes("test"));

                singleChannel.WaitForConfirmsOrDie(TimeSpan.FromSeconds(1));
            }
        }
    }
}
