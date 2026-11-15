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
        private CancellationTokenSource downloadCancellation;
        private volatile bool downloading;

        public event Action<string> StatusChanged;
        public event Action<string> DownloadFailed;
        public event Action<int> DownloadProgress;
        public event Action DownloadFinished;

        public ShareController(ShareService service)
        {
            this.service = service;
            service.Error += ReportStatus;
        }

        public bool IsDownloading
        {
            get { return downloading; }
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
            CancellationTokenSource cancellation;
            lock (downloadLock)
            {
                if (downloading) throw new InvalidOperationException("A download is already in progress.");
                cancellation = new CancellationTokenSource();
                downloadCancellation = cancellation;
                downloading = true;
            }

            ReportStatus("Downloading " + Path.GetFileName(destination) + "...");
            ReportProgress(0);
            ThreadPool.QueueUserWorkItem(delegate { DownloadFile(remote, destination, cancellation); });
        }

        public bool CancelDownload()
        {
            lock (downloadLock)
            {
                if (downloadCancellation == null || downloadCancellation.IsCancellationRequested) return false;
                downloadCancellation.Cancel();
                return true;
            }
        }

        private void ReportStatus(string message)
        {
            Action<string> handler = StatusChanged;
            if (handler != null) handler(message);
        }

        private void ReportProgress(int percent)
        {
            Action<int> handler = DownloadProgress;
            if (handler != null) handler(percent);
        }

        private void DownloadFile(RemoteFile remote, string destination, CancellationTokenSource cancellation)
        {
            string temporary = Path.Combine(Path.GetDirectoryName(destination), "." + Guid.NewGuid().ToString("N") + ".wlanft");

            try
            {
                Guid fileId;
                if (!Guid.TryParseExact(remote.File.Id, "N", out fileId)) throw new InvalidDataException("The file identifier is invalid.");

                cancellation.Token.ThrowIfCancellationRequested();
                HttpWebRequest request = LocalNetwork.CreateRequest(remote.Address, remote.Port, "/file/" + remote.File.Id);
                request.Timeout = 10000;
                request.ReadWriteTimeout = 30000;

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
                                ReportProgress(percent);
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
                    downloadCancellation = null;
                }

                ReportProgress(100);
                ReportStatus("Download complete: " + Path.GetFileName(destination));
            }
            catch (Exception ex)
            {
                bool cancelled = cancellation.IsCancellationRequested;
                string message = cancelled ? "Download cancelled." : "Could not download the file: " + ex.Message;

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
                    downloadCancellation = null;
                    downloading = false;
                }
                cancellation.Dispose();
                Action handler = DownloadFinished;
                if (handler != null) handler();
            }
        }

        public void Dispose()
        {
            service.Error -= ReportStatus;
            service.Dispose();
        }
    }
}
