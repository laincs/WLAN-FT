using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace WlanFt
{
    public static class NetworkTests
    {
        public static ShareService CreateIsolatedService()
        {
            return new ShareService(0);
        }

        public static int DiscoveryPort(ShareService service)
        {
            return service.DiscoveryPort;
        }

        public static bool IsAllowed(string address)
        {
            return LocalNetwork.IsAllowed(IPAddress.Parse(address));
        }

        public static HttpWebRequest Request(string address, int port)
        {
            return LocalNetwork.CreateRequest(IPAddress.Parse(address), port, "/");
        }

        public static string Download(RemoteFile remote, string destination, List<int> progress)
        {
            string error = null;
            using (ManualResetEvent completed = new ManualResetEvent(false))
            using (ShareController controller = new ShareController(new ShareService(0)))
            {
                controller.StatusChanged += delegate(string message)
                {
                    if (message.StartsWith("Download complete:", StringComparison.Ordinal)) completed.Set();
                };
                controller.DownloadFailed += delegate(string message)
                {
                    error = message;
                    completed.Set();
                };
                controller.DownloadProgress += delegate(RemoteFile file, int percent) { progress.Add(percent); };

                controller.Download(remote, destination);
                if (!completed.WaitOne(TimeSpan.FromSeconds(15)))
                    throw new TimeoutException("The download did not finish.");

                while (controller.GetDownloads().Count != 0) Thread.Sleep(10);
            }

            return error;
        }

        public static string CancelDownload(RemoteFile remote, string destination, List<int> progress, bool immediately)
        {
            string status = null;
            string error = null;
            using (ManualResetEvent completed = new ManualResetEvent(false))
            using (ShareController controller = new ShareController(new ShareService(0)))
            {
                controller.StatusChanged += delegate(string message) { status = message; };
                controller.DownloadFailed += delegate(string message) { error = message; };
                controller.DownloadFinished += delegate(RemoteFile file) { completed.Set(); };
                controller.DownloadProgress += delegate(RemoteFile file, int percent)
                {
                    progress.Add(percent);
                    if ((immediately && percent == 0) || (!immediately && percent > 0 && percent < 100))
                        controller.CancelDownload(remote);
                };

                controller.Download(remote, destination);
                if (!completed.WaitOne(TimeSpan.FromSeconds(15)))
                    throw new TimeoutException("The cancelled download did not finish.");
            }

            return error ?? status;
        }

        public static void TestConcurrentDownloads(string directory)
        {
            TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            RemoteFile first = new RemoteFile { Address = IPAddress.Loopback, Port = port,
                File = new FileRecord { Id = Guid.NewGuid().ToString("N"), Name = "first.bin", Size = 4 } };
            RemoteFile second = new RemoteFile { Address = IPAddress.Loopback, Port = port,
                File = new FileRecord { Id = Guid.NewGuid().ToString("N"), Name = "second.bin", Size = 4 } };
            string firstPath = Path.Combine(directory, "first-download.bin");
            string secondPath = Path.Combine(directory, "second-download.bin");
            File.WriteAllText(firstPath, "original");

            using (ManualResetEvent connected = new ManualResetEvent(false))
            using (ManualResetEvent release = new ManualResetEvent(false))
            using (ManualResetEvent finished = new ManualResetEvent(false))
            using (ShareController controller = new ShareController(new ShareService(0)))
            {
                int requests = 0;
                int completed = 0;
                string error = null;
                int firstProgress = -1;
                int secondProgress = -1;
                controller.DownloadProgress += delegate(RemoteFile remote, int percent)
                {
                    if (remote.File.Id == first.File.Id) firstProgress = percent;
                    if (remote.File.Id == second.File.Id) secondProgress = percent;
                };
                controller.DownloadFailed += delegate(string message) { error = message; };
                controller.DownloadFinished += delegate(RemoteFile remote)
                {
                    if (Interlocked.Increment(ref completed) == 2) finished.Set();
                };

                Thread server = new Thread(delegate()
                {
                    try
                    {
                        for (int i = 0; i < 2; i++)
                        {
                            TcpClient client = listener.AcceptTcpClient();
                            ThreadPool.QueueUserWorkItem(delegate
                            {
                                using (client)
                                {
                                    NetworkStream stream = client.GetStream();
                                    using (StreamReader reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true))
                                    {
                                        string line = reader.ReadLine();
                                        while (!String.IsNullOrEmpty(reader.ReadLine())) { }
                                        if (Interlocked.Increment(ref requests) == 2) connected.Set();
                                        release.WaitOne(TimeSpan.FromSeconds(10));
                                        if (line.Contains(second.File.Id))
                                        {
                                            byte[] response = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 4\r\nConnection: close\r\n\r\ndata");
                                            stream.Write(response, 0, response.Length);
                                        }
                                    }
                                }
                            });
                        }
                    }
                    catch (SocketException) { }
                    catch (ObjectDisposedException) { }
                });
                server.IsBackground = true;
                server.Start();

                try
                {
                    controller.Download(first, firstPath);
                    controller.Download(second, secondPath);
                    if (!connected.WaitOne(TimeSpan.FromSeconds(10)))
                        throw new Exception("Both downloads did not connect concurrently.");
                    if (controller.GetDownloads().Count != 2) throw new Exception("Both downloads should remain active.");

                    bool rejected = false;
                    try { controller.Download(first, Path.Combine(directory, "duplicate.bin")); }
                    catch (InvalidOperationException) { rejected = true; }
                    if (!rejected) throw new Exception("A duplicate file download was allowed.");

                    RemoteFile third = new RemoteFile { Address = IPAddress.Loopback, Port = port,
                        File = new FileRecord { Id = Guid.NewGuid().ToString("N"), Name = "third.bin", Size = 4 } };
                    rejected = false;
                    try { controller.Download(third, secondPath.ToUpperInvariant()); }
                    catch (InvalidOperationException) { rejected = true; }
                    if (!rejected) throw new Exception("A duplicate destination was allowed.");

                    if (!controller.CancelDownload(first)) throw new Exception("Cancellation failed.");
                    release.Set();
                    if (!finished.WaitOne(TimeSpan.FromSeconds(10))) throw new Exception("Downloads did not finish.");
                    if (error != null) throw new Exception(error);
                    if (firstProgress == 100 || secondProgress != 100) throw new Exception("Progress was not independent.");
                    if (File.ReadAllText(firstPath) != "original") throw new Exception("Cancellation modified the destination.");
                    if (File.ReadAllText(secondPath) != "data") throw new Exception("The other download did not complete.");
                    if (controller.GetDownloads().Count != 0) throw new Exception("Completed downloads were not removed.");
                }
                finally
                {
                    release.Set();
                    listener.Stop();
                    server.Join(5000);
                    if (File.Exists(firstPath)) File.Delete(firstPath);
                    if (File.Exists(secondPath)) File.Delete(secondPath);
                }
            }
        }
    }
}
