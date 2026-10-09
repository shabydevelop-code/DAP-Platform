using System.Net;
using System.Net.Sockets;

namespace DAP.Testing;

internal static class E2ePortGuard
{
    public static void EnsurePortFree(int port, string occupiedMessage)
    {
        TcpListener? listener = null;
        try
        {
            listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
        }
        catch (SocketException ex)
        {
            throw new InvalidOperationException(occupiedMessage, ex);
        }
        finally
        {
            listener?.Stop();
        }
    }
}
