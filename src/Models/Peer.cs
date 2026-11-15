using System;
using System.Net;

namespace WlanFt
{
    internal sealed class Peer
    {
        public IPAddress Address;
        public int Port;
        public DateTime LastSeen;
        public bool Loading;
        public Catalog Catalog;
    }
}
