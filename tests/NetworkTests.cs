using System;
using System.Collections.Generic;
using System.Net;
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
                controller.DownloadProgress += delegate(int percent) { progress.Add(percent); };

                controller.Download(remote, destination);
                if (!completed.WaitOne(TimeSpan.FromSeconds(15)))
                    throw new TimeoutException("The download did not finish.");

                while (controller.IsDownloading) Thread.Sleep(10);
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
                controller.DownloadFinished += delegate { completed.Set(); };
                controller.DownloadProgress += delegate(int percent)
                {
                    progress.Add(percent);
                    if ((immediately && percent == 0) || (!immediately && percent > 0 && percent < 100))
                        controller.CancelDownload();
                };

                controller.Download(remote, destination);
                if (!completed.WaitOne(TimeSpan.FromSeconds(15)))
                    throw new TimeoutException("The cancelled download did not finish.");
            }

            return error ?? status;
        }
    }
}
