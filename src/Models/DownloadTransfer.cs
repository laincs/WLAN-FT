using System.Threading;

namespace WlanFt
{
    internal sealed class DownloadTransfer
    {
        public readonly RemoteFile Remote;
        public readonly string Destination;
        public readonly CancellationTokenSource Cancellation = new CancellationTokenSource();
        public volatile int Progress;

        public DownloadTransfer(RemoteFile remote, string destination)
        {
            Remote = remote;
            Destination = destination;
        }
    }
}
