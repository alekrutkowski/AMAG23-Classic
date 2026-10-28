using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Collections.Generic;
public sealed class ClassicRequest : IDisposable {
    private TcpClient client;
    public string Method {get;private set;}
    public string Path {get;private set;}
    public string Body {get;private set;}
    public Dictionary<string,string> Headers {get;private set;}=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
    public ClassicRequest(TcpClient socket) {
        client=socket; client.ReceiveTimeout=5000; client.SendTimeout=10000;
        var s=client.GetStream(); var bytes=new List<byte>();
        while(bytes.Count<16384) {
            int n=s.ReadByte(); if(n<0) throw new IOException("Incomplete HTTP request."); bytes.Add((byte)n);
            int k=bytes.Count; if(k>=4 && bytes[k-4]==13 && bytes[k-3]==10 && bytes[k-2]==13 && bytes[k-1]==10) break;
        }
        if(bytes.Count>=16384) throw new IOException("Headers too large.");
        string[] lines=Encoding.ASCII.GetString(bytes.ToArray()).Split(new[]{"\r\n"},StringSplitOptions.None);
        string[] start=lines[0].Split(' '); if(start.Length!=3 || start[2]!="HTTP/1.1") throw new IOException("Unsupported HTTP request.");
        Method=start[0]; Path=start[1];
        foreach(string line in lines) { int k=line.IndexOf(':'); if(k>0) {string key=line.Substring(0,k).Trim(); if(Headers.ContainsKey(key)) throw new IOException("Duplicate header."); Headers[key]=line.Substring(k+1).Trim();}}
        if(Headers.ContainsKey("Transfer-Encoding")) throw new IOException("Chunked requests unsupported.");
        int length=0;
        if(Headers.ContainsKey("Content-Length") && (!Int32.TryParse(Headers["Content-Length"],out length) || length<0 || length>131072)) throw new IOException("Invalid body size.");
        byte[] body=new byte[length]; int off=0;
        while(off<length) {int n=s.Read(body,off,length-off); if(n<=0) throw new IOException("Incomplete body.");off+=n;}
        Body=new UTF8Encoding(false,true).GetString(body);
    }
    public string Header(string key) {return Headers.ContainsKey(key)?Headers[key]:"";}
    public void Reply(int code,string mime,byte[] body) {
        string header="HTTP/1.1 "+code+" "+(code==200?"OK":"Error")+"\r\nContent-Type: "+mime+"\r\nContent-Length: "+body.Length+"\r\nConnection: close\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nX-Frame-Options: DENY\r\nReferrer-Policy: no-referrer\r\nContent-Security-Policy: default-src 'self'; script-src 'self'; style-src 'self'; connect-src 'self'; img-src 'self' data:; object-src 'none'; frame-ancestors 'none'; base-uri 'none'\r\n\r\n";
        byte[] h=Encoding.ASCII.GetBytes(header); var s=client.GetStream();s.Write(h,0,h.Length);s.Write(body,0,body.Length);s.Flush();
    }
    public void Dispose(){client.Dispose();}
}
public sealed class ClassicServer : IDisposable {
    private TcpListener listener;
    public int Port {get;private set;}
    public ClassicServer(int port) {listener=new TcpListener(IPAddress.Loopback,port);listener.Start();Port=((IPEndPoint)listener.LocalEndpoint).Port;}
    public bool Pending(){return listener.Pending();}
    public ClassicRequest Accept(){var c=listener.AcceptTcpClient();try{return new ClassicRequest(c);}catch{c.Dispose();throw;}}
    public void Dispose(){listener.Stop();}
}
