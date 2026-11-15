using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace WlanFt
{
    public sealed class ShareService : IDisposable
    {
        private const int DiscoveryPort = 43871;
        private const string DiscoveryPrefix = "WLAN-FT-1|";
        private readonly object sync = new object();
        private readonly Dictionary<string, SharedFile> files = new Dictionary<string, SharedFile>();
        private readonly Dictionary<string, Peer> peers = new Dictionary<string, Peer>();
        private readonly string instanceId = Guid.NewGuid().ToString("N");
        private readonly TcpListener server;
        private readonly UdpClient discovery;
        private readonly System.Threading.Timer announceTimer;
        private volatile bool running;

        public event Action<string> Error;

        public ShareService()
        {
            server = new TcpListener(IPAddress.Any, 0);
            server.Start();

            try
            {
                discovery = new UdpClient(AddressFamily.InterNetwork);
                discovery.ExclusiveAddressUse = false;
                discovery.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                discovery.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
                discovery.EnableBroadcast = true;
            }
            catch
            {
                server.Stop();
                throw;
            }

            running = true;
            StartThread(AcceptLoop);
            StartThread(ReceiveLoop);
            announceTimer = new System.Threading.Timer(Announce, null, 0, 3000);
        }

        public int Port
        {
            get { return ((IPEndPoint)server.LocalEndpoint).Port; }
        }

        private static void StartThread(ThreadStart action)
        {
            Thread thread = new Thread(action);
            thread.IsBackground = true;
            thread.Start();
        }

        private void Report(string message)
        {
            Action<string> handler = Error;
            if (running && handler != null) handler(message);
        }

        public FileRecord Add(string path)
        {
            FileInfo info = new FileInfo(path);

            using (FileStream stream = info.Open(FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                FileRecord record = new FileRecord { Id = Guid.NewGuid().ToString("N"), Name = info.Name, Size = stream.Length };
                lock (sync) files.Add(record.Id, new SharedFile { Record = record, Path = info.FullName });

                return record;
            }
        }

        public void Remove(string id)
        {
            lock (sync) files.Remove(id);
        }

        public List<RemoteFile> GetRemoteFiles()
        {
            List<RemoteFile> result = new List<RemoteFile>();

            lock (sync)
            {
                foreach (Peer peer in peers.Values)
                {
                    if (peer.Catalog == null || peer.Catalog.Files == null) continue;

                    foreach (FileRecord file in peer.Catalog.Files)
                    {
                        if (file != null && !String.IsNullOrEmpty(file.Id) && !String.IsNullOrEmpty(file.Name) && file.Size >= 0)
                            result.Add(new RemoteFile { Computer = peer.Catalog.Computer, Address = peer.Address, Port = peer.Port, File = file });
                    }

                }
            }
            return result;
        }

        private void Announce(object state)
        {
            if (!running) return;

            try
            {
                byte[] message = Encoding.UTF8.GetBytes(DiscoveryPrefix + instanceId + "|" + Port);
                LocalNetwork.Announce(message, DiscoveryPort);

                lock (sync)
                {
                    List<string> expired = new List<string>();
                    foreach (KeyValuePair<string, Peer> peer in peers)
                        if ((DateTime.UtcNow - peer.Value.LastSeen).TotalSeconds > 12) expired.Add(peer.Key);
                    foreach (string id in expired) peers.Remove(id);
                }
            }
            catch (Exception ex)
            {
                Report("Could not search for devices: " + ex.Message);
            }
        }

        private void ReceiveLoop()
        {
            while (running)
            {
                try
                {
                    IPEndPoint sender = new IPEndPoint(IPAddress.Any, 0);
                    string message = Encoding.UTF8.GetString(discovery.Receive(ref sender));
                    if (!LocalNetwork.IsAllowed(sender.Address)) continue;
                    if (!message.StartsWith(DiscoveryPrefix, StringComparison.Ordinal)) continue;

                    string[] parts = message.Substring(DiscoveryPrefix.Length).Split('|');
                    int port;
                    Guid id;
                    if (parts.Length != 2 || !Guid.TryParseExact(parts[0], "N", out id) ||
                        parts[0] == instanceId || !Int32.TryParse(parts[1], out port) || port < 1 || port > 65535) continue;

                    Peer peer;
                    lock (sync)
                    {
                        if (!peers.TryGetValue(parts[0], out peer))
                        {
                            peer = new Peer();
                            peers.Add(parts[0], peer);
                        }

                        peer.Address = sender.Address;
                        peer.Port = port;
                        peer.LastSeen = DateTime.UtcNow;
                        if (peer.Loading) continue;
                        peer.Loading = true;
                    }

                    IPAddress peerAddress = sender.Address;
                    int peerPort = port;
                    ThreadPool.QueueUserWorkItem(delegate { LoadCatalog(peer, peerAddress, peerPort); });
                }
                catch (ObjectDisposedException)
                {
                    if (running) Report("Device discovery stopped unexpectedly.");
                }
                catch (SocketException ex)
                {
                    if (running) Report("Error searching for devices: " + ex.Message);
                }
                catch (Exception ex)
                {
                    Report("Error receiving an announcement: " + ex.Message);
                }
            }
        }

        private void LoadCatalog(Peer peer, IPAddress address, int port)
        {
            try
            {
                HttpWebRequest request = LocalNetwork.CreateRequest(address, port, "/catalog");
                request.Timeout = 4000;
                request.ReadWriteTimeout = 4000;

                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                {
                    if (response.StatusCode != HttpStatusCode.OK) throw new InvalidDataException("The device rejected the request.");
                    if (response.ContentLength < 0 || response.ContentLength > 1024 * 1024)
                        throw new InvalidDataException("The received list has an invalid size.");

                    Catalog catalog = new JavaScriptSerializer().Deserialize<Catalog>(reader.ReadToEnd());
                    if (catalog == null || catalog.Files == null) throw new InvalidDataException("The received list is invalid.");

                    lock (sync)
                    {
                        if (peer.Address.Equals(address) && peer.Port == port) peer.Catalog = catalog;
                    }
                }
            }
            catch (Exception ex)
            {
                lock (sync) peer.Catalog = null;
                Report("Could not contact " + address + ": " + ex.Message);
            }
            finally
            {
                lock (sync) peer.Loading = false;
            }
        }

        private void AcceptLoop()
        {
            while (running)
            {
                try
                {
                    TcpClient client = server.AcceptTcpClient();
                    ThreadPool.QueueUserWorkItem(delegate { HandleClient(client); });
                }
                catch (SocketException ex)
                {
                    if (running) Report("Error accepting connections: " + ex.Message);
                }
                catch (ObjectDisposedException)
                {
                    if (running) Report("The server stopped unexpectedly.");
                }
            }
        }

        private static void WriteHeaders(Stream stream, int status, string type, long length)
        {
            string reason = status == 200 ? "OK" : "Not Found";
            byte[] header = Encoding.ASCII.GetBytes("HTTP/1.1 " + status + " " + reason + "\r\nContent-Type: " + type +
                "\r\nContent-Length: " + length + "\r\nConnection: close\r\n\r\n");
            stream.Write(header, 0, header.Length);
        }

        private void HandleClient(TcpClient client)
        {
            try
            {
                using (client)
                {
                    IPEndPoint remote = (IPEndPoint)client.Client.RemoteEndPoint;
                    if (!LocalNetwork.IsAllowed(remote.Address)) return;

                    client.ReceiveTimeout = 10000;
                    client.SendTimeout = 30000;

                    using (NetworkStream stream = client.GetStream())
                    {
                        // Consume the headers before closing the connection, so TCP does not reset the response.
                        List<byte> headers = new List<byte>();

                        while (headers.Count < 16384)
                        {
                            int b = stream.ReadByte();
                            if (b < 0) break;
                            headers.Add((byte)b);
                            int count = headers.Count;
                            if (count >= 4 && headers[count - 4] == 13 && headers[count - 3] == 10 &&
                                headers[count - 2] == 13 && headers[count - 1] == 10) break;
                        }

                        string headerText = Encoding.ASCII.GetString(headers.ToArray());
                        int lineEnd = headerText.IndexOf("\r\n", StringComparison.Ordinal);
                        string[] request = lineEnd >= 0 ? headerText.Substring(0, lineEnd).Split(' ') : new string[0];
                        if (!headerText.EndsWith("\r\n\r\n", StringComparison.Ordinal) || request.Length < 2 || request[0] != "GET")
                        {
                            WriteHeaders(stream, 404, "text/plain", 0);
                            return;
                        }

                        if (request[1] == "/catalog")
                        {
                            Catalog catalog = new Catalog { Computer = Environment.MachineName, Files = new List<FileRecord>() };
                            lock (sync) foreach (SharedFile file in files.Values) catalog.Files.Add(file.Record);
                            byte[] data = Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(catalog));
                            WriteHeaders(stream, 200, "application/json; charset=utf-8", data.Length);
                            stream.Write(data, 0, data.Length);
                            return;
                        }

                        SharedFile shared;
                        string id = request[1].StartsWith("/file/", StringComparison.Ordinal) ? request[1].Substring(6) : "";
                        lock (sync) files.TryGetValue(id, out shared);

                        if (shared == null)
                        {
                            WriteHeaders(stream, 404, "text/plain", 0);
                            return;
                        }

                        using (FileStream source = new FileStream(shared.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        {
                            long remaining = source.Length;
                            WriteHeaders(stream, 200, "application/octet-stream", remaining);
                            byte[] buffer = new byte[81920];

                            while (remaining > 0)
                            {
                                int read = source.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                                if (read == 0) throw new EndOfStreamException("The shared file changed during transfer.");
                                stream.Write(buffer, 0, read);
                                remaining -= read;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Report("Could not send a file or respond to a device: " + ex.Message);
            }
        }

        public void Dispose()
        {
            running = false;
            announceTimer.Dispose();
            discovery.Close();
            server.Stop();
        }
    }
}
