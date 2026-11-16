using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;

namespace WlanFt
{
    internal sealed class ShareController : IDisposable
    {
        private readonly ShareService service;
        private readonly object downloadLock = new object();
        private readonly Dictionary<string, DownloadTransfer> downloads = new Dictionary<string, DownloadTransfer>();
        private bool disposed;

        public event Action<string> StatusChanged;
        public event Action<string> DownloadFailed;
        public event Action<RemoteFile, int> DownloadProgress;
        public event Action<RemoteFile> DownloadFinished;

        public ShareController(ShareService service)
        {
            this.service = service;
            service.Error += ReportStatus;
        }

        public static string DownloadKey(RemoteFile remote)
        {
            return remote.Address + ":" + remote.Port + ":" + remote.File.Id;
        }

        public List<DownloadTransfer> GetDownloads()
        {
            lock (downloadLock) return new List<DownloadTransfer>(downloads.Values);
        }

        public FileRecord Share(string path)
        {
            if (Directory.Exists(path)) throw new IOException("Select files, not folders.");
            return service.Add(path);
        }

        public void Remove(string id)
        {
            service.Remove(id);
        }

        public List<RemoteFile> GetRemoteFiles()
        {
            return service.GetRemoteFiles();
        }

        public void Download(RemoteFile remote, string destination)
        {
            string key = DownloadKey(remote);
            string fullDestination = Path.GetFullPath(destination);
            DownloadTransfer transfer;
            lock (downloadLock)
            {
                if (disposed) throw new ObjectDisposedException("ShareController");
                if (downloads.ContainsKey(key)) throw new InvalidOperationException("This file is already downloading.");
                foreach (DownloadTransfer active in downloads.Values)
                    if (String.Equals(active.Destination, fullDestination, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Another download is using this destination.");

                transfer = new DownloadTransfer(remote, fullDestination);
                downloads.Add(key, transfer);
            }

            ReportStatus("Downloading " + Path.GetFileName(destination) + "...");
            ReportProgress(transfer, 0);
            ThreadPool.QueueUserWorkItem(delegate { DownloadFile(key, transfer); });
        }

        public bool CancelDownload(RemoteFile remote)
        {
            lock (downloadLock)
            {
                DownloadTransfer transfer;
                if (!downloads.TryGetValue(DownloadKey(remote), out transfer) ||
                    transfer.Cancellation.IsCancellationRequested) return false;
                transfer.Cancellation.Cancel();
                return true;
            }
        }

        private void ReportStatus(string message)
        {
            Action<string> handler = StatusChanged;
            if (handler != null) handler(message);
        }

        private void ReportProgress(DownloadTransfer transfer, int percent)
        {
            transfer.Progress = percent;
            Action<RemoteFile, int> handler = DownloadProgress;
            if (handler != null) handler(transfer.Remote, percent);
        }

        private void DownloadFile(string key, DownloadTransfer transfer)
        {
            RemoteFile remote = transfer.Remote;
            string destination = transfer.Destination;
            CancellationTokenSource cancellation = transfer.Cancellation;
            string temporary = Path.Combine(Path.GetDirectoryName(destination), "." + Guid.NewGuid().ToString("N") + ".wlanft");

            try
            {
                Guid fileId;
                if (!Guid.TryParseExact(remote.File.Id, "N", out fileId)) throw new InvalidDataException("The file identifier is invalid.");

                cancellation.Token.ThrowIfCancellationRequested();
                HttpWebRequest request = LocalNetwork.CreateRequest(remote.Address, remote.Port, "/file/" + remote.File.Id);
                request.Timeout = 10000;
                request.ReadWriteTimeout = 30000;
                request.ServicePoint.ConnectionLimit = Int32.MaxValue;

                using (cancellation.Token.Register(delegate { request.Abort(); }))
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                {
                    if (response.StatusCode != HttpStatusCode.OK) throw new InvalidDataException("The device rejected the download.");
                    if (response.ContentLength < 0 || response.ContentLength != remote.File.Size)
                        throw new InvalidDataException("The file has changed since it was shared. Ask the sender to add it again.");

                    using (Stream input = response.GetResponseStream())
                    using (FileStream output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        byte[] buffer = new byte[81920];
                        long total = 0;
                        int lastPercent = 0;
                        int read;

                        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            cancellation.Token.ThrowIfCancellationRequested();
                            output.Write(buffer, 0, read);
                            total += read;
                            int percent = (int)Math.Min(99, total * 100.0 / response.ContentLength);
                            if (percent > lastPercent)
                            {
                                lastPercent = percent;
                                ReportProgress(transfer, percent);
                            }
                        }

                        if (total != response.ContentLength) throw new EndOfStreamException("The download was incomplete.");
                    }
                }

                lock (downloadLock)
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    if (File.Exists(destination)) File.Replace(temporary, destination, null);
                    else File.Move(temporary, destination);
                    downloads.Remove(key);
                }

                ReportProgress(transfer, 100);
                ReportStatus("Download complete: " + Path.GetFileName(destination));
            }
            catch (Exception ex)
            {
                bool cancelled = cancellation.IsCancellationRequested;
                string message = cancelled ? "Download cancelled: " + Path.GetFileName(destination) : "Could not download " + Path.GetFileName(destination) + ": " + ex.Message;

                try
                {
                    if (File.Exists(temporary)) File.Delete(temporary);
                }
                catch (Exception cleanup)
                {
                    message += " Could not delete the temporary file " + temporary + ": " + cleanup.Message;
                    cancelled = false;
                }

                if (cancelled) ReportStatus(message);
                else
                {
                    Action<string> handler = DownloadFailed;
                    if (handler != null) handler(message);
                }
            }
            finally
            {
                lock (downloadLock)
                {
                    DownloadTransfer current;
                    if (downloads.TryGetValue(key, out current) && Object.ReferenceEquals(current, transfer))
                        downloads.Remove(key);
                }
                cancellation.Dispose();
                Action<RemoteFile> handler = DownloadFinished;
                if (handler != null) handler(remote);
            }
        }

        public void Dispose()
        {
            lock (downloadLock)
            {
                disposed = true;
                foreach (DownloadTransfer transfer in downloads.Values)
                    transfer.Cancellation.Cancel();
            }
            service.Error -= ReportStatus;
            service.Dispose();
        }
    }
}
