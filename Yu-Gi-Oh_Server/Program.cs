using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Yu_Gi_Oh_Server.Server;

namespace Yu_Gi_Oh_Server;

internal class YuGiOhServer
{
    private static readonly TcpListener Server = new(IPAddress.Parse("127.0.0.1"), 1234);
    private static Settings? _settings;

    public static void Main()
    {
        _settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText("server.properties"));
        Server.Start();

        Console.WriteLine("It's Time To Duel");
        Console.WriteLine($"My IP Is {Server.LocalEndpoint}");
        Console.WriteLine("---------- PROPERTIES ----------");
        Console.WriteLine(_settings?.ToString());

        while (true)
        {
            var RivalDuelistClient = Server.AcceptTcpClient();
            ThreadPool.QueueUserWorkItem(RivalDuelistHandler, RivalDuelistClient);
        }
    }

    private static void RivalDuelistHandler(object? client)
    {
        var Client = (TcpClient)client!;
        if (State.CurrentNumberOfDuelists > 2)
        {
            Client.Client.Send(Encoding.ASCII.GetBytes("Server Full."));
            Client.Client.Disconnect(false);
            return;
        }

        State.CurrentNumberOfDuelists++;

        Console.WriteLine($"Duelist Connected {Client.Client.RemoteEndPoint}");
        Client.Client.Send(Encoding.ASCII.GetBytes(JsonSerializer.Serialize(_settings)));
        Console.WriteLine($"Sent server.properties to {Client.Client.RemoteEndPoint}");
        while (Client.Client.Connected)
        {
            if (Client.SendBufferSize > 0)
            {
            }

            Client.Client.Send(Encoding.ASCII.GetBytes($"#HEART-{new Random().Next()}"));
            Thread.Sleep(TimeSpan.FromMilliseconds(new Random().Next(1024, 4096)));
        }
    }
}