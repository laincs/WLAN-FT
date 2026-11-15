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
        private volatile bool downloading;

        public event Action<string> StatusChanged;
        public event Action<string> DownloadFailed;

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
            downloading = true;
            ReportStatus("Downloading " + Path.GetFileName(destination) + "...");
            ThreadPool.QueueUserWorkItem(delegate { DownloadFile(remote, destination); });
        }

        private void ReportStatus(string message)
        {
            Action<string> handler = StatusChanged;
            if (handler != null) handler(message);
        }

        private void DownloadFile(RemoteFile remote, string destination)
        {
            string temporary = Path.Combine(Path.GetDirectoryName(destination), "." + Guid.NewGuid().ToString("N") + ".wlanft");

            try
            {
                Guid fileId;
                if (!Guid.TryParseExact(remote.File.Id, "N", out fileId)) throw new InvalidDataException("The file identifier is invalid.");

                HttpWebRequest request = LocalNetwork.CreateRequest(remote.Address, remote.Port, "/file/" + remote.File.Id);
                request.Timeout = 10000;
                request.ReadWriteTimeout = 30000;

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
                        int read;

                        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            output.Write(buffer, 0, read);
                            total += read;
                        }

                        if (total != response.ContentLength) throw new EndOfStreamException("The download was incomplete.");
                    }
                }

                if (File.Exists(destination)) File.Replace(temporary, destination, null);
                else File.Move(temporary, destination);
                ReportStatus("Download complete: " + Path.GetFileName(destination));
            }
            catch (Exception ex)
            {
                string message = "Could not download the file: " + ex.Message;

                try
                {
                    if (File.Exists(temporary)) File.Delete(temporary);
                }
                catch (Exception cleanup)
                {
                    message += " Could not delete the temporary file " + temporary + ": " + cleanup.Message;
                }

                Action<string> handler = DownloadFailed;
                if (handler != null) handler(message);
            }
            finally
            {
                downloading = false;
            }
        }

        public void Dispose()
        {
            service.Error -= ReportStatus;
            service.Dispose();
        }
    }
}
