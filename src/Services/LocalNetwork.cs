using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace WlanFt
{
    internal static class LocalNetwork
    {
        private static bool IsPrivate(IPAddress address)
        {
            if (address == null || address.AddressFamily != AddressFamily.InterNetwork) return false;

            byte[] b = address.GetAddressBytes();

            return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) ||
                (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254);
        }

        private static bool SameSubnet(IPAddress local, IPAddress mask, IPAddress remote)
        {
            if (!IsPrivate(local) || !IsPrivate(remote) || mask == null) return false;

            byte[] a = local.GetAddressBytes();
            byte[] b = remote.GetAddressBytes();
            byte[] m = mask.GetAddressBytes();

            if (m.Length != 4 || m[0] != 255) return false;

            for (int i = 0; i < 4; i++)
                if ((a[i] & m[i]) != (b[i] & m[i])) return false;

            return true;
        }

        public static bool IsAllowed(IPAddress address)
        {
            if (address == null || address.AddressFamily != AddressFamily.InterNetwork) return false;
            if (IPAddress.IsLoopback(address)) return true;

            foreach (NetworkInterface network in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (network.OperationalStatus != OperationalStatus.Up || network.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                foreach (UnicastIPAddressInformation local in network.GetIPProperties().UnicastAddresses)
                    if (local.Address.AddressFamily == AddressFamily.InterNetwork && SameSubnet(local.Address, local.IPv4Mask, address)) return true;
            }

            return false;
        }

        public static HttpWebRequest CreateRequest(IPAddress address, int port, string path)
        {
            if (!IsAllowed(address)) throw new InvalidOperationException("Only connections to the same local network are allowed.");

            HttpWebRequest request = (HttpWebRequest)WebRequest.Create("http://" + address + ":" + port + path);
            request.Proxy = null;
            request.AllowAutoRedirect = false;

            return request;
        }

        public static void Announce(byte[] message, int port)
        {
            foreach (NetworkInterface network in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (network.OperationalStatus != OperationalStatus.Up || network.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                foreach (UnicastIPAddressInformation address in network.GetIPProperties().UnicastAddresses)
                {
                    if (!IsPrivate(address.Address) || address.IPv4Mask == null) continue;

                    byte[] ip = address.Address.GetAddressBytes();
                    byte[] mask = address.IPv4Mask.GetAddressBytes();
                    if (mask[0] != 255) continue;

                    byte[] broadcast = new byte[4];
                    for (int i = 0; i < 4; i++) broadcast[i] = (byte)(ip[i] | ~mask[i]);
                    if (!IsPrivate(new IPAddress(broadcast))) continue;

                    using (UdpClient sender = new UdpClient(new IPEndPoint(address.Address, 0)))
                    {
                        sender.EnableBroadcast = true;
                        sender.Ttl = 1;
                        sender.Send(message, message.Length, new IPEndPoint(new IPAddress(broadcast), port));
                    }
                }
            }
        }
    }
}
