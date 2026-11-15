using System.Net;

namespace WlanFt
{
    public sealed class RemoteFile
    {
        public string Computer;
        public IPAddress Address;
        public int Port;
        public FileRecord File;
    }
}
