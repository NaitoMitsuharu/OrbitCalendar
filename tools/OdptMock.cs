using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

/// <summary>
/// A tiny local stand-in for api.odpt.org / api-challenge.odpt.org used by tools/verify-odpt.ps1.
/// Answers "type|filter=value" routes with fixture JSON and returns 401 unless acl:consumerKey matches.
/// </summary>
public sealed class OdptMock : IDisposable
{
    readonly TcpListener listener;
    readonly Thread thread;
    public readonly Dictionary<string, string> Routes = new Dictionary<string, string>();
    public string Token = "test-token";
    public readonly List<string> Requests = new List<string>();
    public int Port { get { return ((IPEndPoint)listener.LocalEndpoint).Port; } }

    public OdptMock()
    {
        listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        thread = new Thread(Loop) { IsBackground = true };
        thread.Start();
    }

    void Loop()
    {
        while (true)
        {
            TcpClient client;
            try { client = listener.AcceptTcpClient(); } catch { return; }
            using (client)
            {
                try { Serve(client.GetStream()); } catch { }
            }
        }
    }

    void Serve(NetworkStream stream)
    {
        var header = new StringBuilder();
        var buffer = new byte[1];
        while (!header.ToString().EndsWith("\r\n\r\n") && stream.Read(buffer, 0, 1) == 1) header.Append((char)buffer[0]);
        string line = header.ToString().Split(new[] { "\r\n" }, StringSplitOptions.None)[0];
        string target = line.Split(' ')[1];
        string path = target.Split('?')[0];
        string query = target.Contains("?") ? target.Substring(target.IndexOf('?') + 1) : "";
        string type = Uri.UnescapeDataString(path.Substring(path.LastIndexOf('/') + 1));
        string key = null, filter = "";
        foreach (var part in query.Split('&'))
        {
            var kv = part.Split(new[] { '=' }, 2);
            string name = Uri.UnescapeDataString(kv[0]), value = kv.Length > 1 ? Uri.UnescapeDataString(kv[1]) : "";
            if (name == "acl:consumerKey") key = value; else if (filter.Length == 0 && name.Length > 0) filter = name + "=" + value;
        }
        string route = type + (filter.Length > 0 ? "|" + filter : "");
        lock (Requests) Requests.Add((path.Contains("challenge") ? "challenge " : "standard ") + route);
        int status = 200; string body;
        if (key != Token) { status = 401; body = "{\"title\":\"Unauthorized\"}"; }
        else if (!Routes.TryGetValue((path.Contains("challenge") ? "challenge " : "standard ") + route, out body)) body = "[]";
        var bytes = Encoding.UTF8.GetBytes(body);
        var head = Encoding.ASCII.GetBytes("HTTP/1.1 " + status + (status == 200 ? " OK" : " Unauthorized") + "\r\nContent-Type: application/json; charset=utf-8\r\nContent-Length: " + bytes.Length + "\r\nConnection: close\r\n\r\n");
        stream.Write(head, 0, head.Length);
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush();
    }

    public void Dispose() { listener.Stop(); }
}
