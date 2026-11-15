using System;
using System.Net;
using System.Threading;

namespace WlanFt
{
    public static class NetworkTests
    {
        public static bool IsAllowed(string address)
        {
            return LocalNetwork.IsAllowed(IPAddress.Parse(address));
        }

        public static HttpWebRequest Request(string address, int port)
        {
            return LocalNetwork.CreateRequest(IPAddress.Parse(address), port, "/");
        }

        public static string Download(RemoteFile remote, string destination)
        {
            string error = null;
            using (ManualResetEvent completed = new ManualResetEvent(false))
            using (ShareController controller = new ShareController(new ShareService()))
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

                controller.Download(remote, destination);
                if (!completed.WaitOne(TimeSpan.FromSeconds(15)))
                    throw new TimeoutException("The download did not finish.");

                while (controller.IsDownloading) Thread.Sleep(10);
            }

            return error;
        }
    }
}
