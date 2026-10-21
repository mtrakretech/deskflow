using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Management;

namespace Viola {
public sealed class LanAddress {
    public IPAddress Address,Mask;
    public int InterfaceIndex;
    public bool Contains(IPAddress peer) {byte[] p=peer.GetAddressBytes(),a=Address.GetAddressBytes(),m=Mask.GetAddressBytes();if(p.Length!=4)return false;for(int i=0;i<4;i++)if((p[i]&m[i])!=(a[i]&m[i]))return false;return true;}
    public IPAddress Broadcast {get {byte[] a=Address.GetAddressBytes(),m=Mask.GetAddressBytes();for(int i=0;i<4;i++)a[i]=(byte)(a[i]|~m[i]);return new IPAddress(a);}}
    public static LanAddress[] Eligible() {
        var indices=new HashSet<int>();
        // WMI exposes the actual network category; Public and unidentified networks fail closed.
        using(var q=new ManagementObjectSearcher("root\\StandardCimv2","SELECT InterfaceIndex, NetworkCategory FROM MSFT_NetConnectionProfile"))
        using(var rows=q.Get())foreach(ManagementObject row in rows)if(Convert.ToInt32(row["NetworkCategory"])==1)indices.Add(Convert.ToInt32(row["InterfaceIndex"]));
        var list=new List<LanAddress>();
        foreach(var nic in NetworkInterface.GetAllNetworkInterfaces()) {
            if(nic.OperationalStatus!=OperationalStatus.Up||(nic.NetworkInterfaceType!=NetworkInterfaceType.Ethernet&&nic.NetworkInterfaceType!=NetworkInterfaceType.Wireless80211))continue;
            string description=(nic.Description+" "+nic.Name).ToLowerInvariant();
            if(new[]{"virtual","hyper-v","vmware","docker","vpn","tap-","tun ","tailscale","wireguard","zerotier"}.Any(description.Contains))continue;
            var props=nic.GetIPProperties();var ipv4=props.GetIPv4Properties();if(ipv4==null||!indices.Contains(ipv4.Index))continue;
            foreach(var ua in props.UnicastAddresses)if(ua.Address.AddressFamily==AddressFamily.InterNetwork&&!IPAddress.IsLoopback(ua.Address)&&ua.IPv4Mask!=null&&ua.Address.GetAddressBytes()[0]!=169)
                list.Add(new LanAddress{Address=ua.Address,Mask=ua.IPv4Mask,InterfaceIndex=ipv4.Index});
        }
        return list.ToArray();
    }
}
public sealed class Bridge : IDisposable {
    public const int TcpPort=48751,UdpPort=48752;
    readonly Identity identity;readonly string root,node;readonly Action<string> status;readonly Action<string[]> publish;readonly Action<Progress> progress;
    readonly object gate=new object(),queueGate=new object();readonly CancellationTokenSource stop=new CancellationTokenSource();
    List<TcpListener> listeners=new List<TcpListener>();List<UdpClient> udp=new List<UdpClient>();HashSet<TcpClient> clients=new HashSet<TcpClient>();
    LanAddress[] addresses=new LanAddress[0];string adapterKey="";System.Threading.Timer timer;int ticking,sending,receiving,probing;string[] pending;DateTime queuedAt,lastSeen=DateTime.MinValue;
    readonly Dictionary<string,long> nonces=new Dictionary<string,long>();
    IPAddress peer;string peerNode;int ticks;
    public string Peer {get {lock(gate)return peer==null?"None authenticated":peer+" / "+(peerNode??"awaiting TLS");}}
    public bool Busy {get{return sending!=0||receiving!=0;}}
    public Bridge(Identity id,string dir,string nodeId,Action<string> state,Action<string[]> clipboard,Action<Progress> update){identity=id;root=dir;node=nodeId;status=state;publish=clipboard;progress=update;}
    void State(string text){if(!stop.IsCancellationRequested)status(text);}
    public void Start(){timer=new System.Threading.Timer(Tick,null,0,10000);}
    void Tick(object unused){if(Interlocked.Exchange(ref ticking,1)!=0)return;try{
        if(stop.IsCancellationRequested)return;
        if(ticks++%3==0)Refresh();
        LanAddress[] local;List<UdpClient> sockets;lock(gate){local=addresses;sockets=udp.ToList();}
        byte[] packet=Discovery.Packet(identity.DiscoveryKey,node,TcpPort,Discovery.Seconds,Guid.NewGuid().ToString("N"));
        foreach(var a in local)try{using(var sender=new UdpClient(new IPEndPoint(a.Address,0))){sender.EnableBroadcast=true;sender.Send(packet,packet.Length,new IPEndPoint(a.Broadcast,UdpPort));}}catch(Exception ex){Log.Write("Discovery send: "+ex.Message);}
        IPAddress current;lock(gate){current=peer;if((DateTime.UtcNow-lastSeen).TotalSeconds>40){peer=null;peerNode=null;current=null;}}
        if(current!=null){Probe(current,null);Pump();}else if(!Busy)State(local.Length==0?"Searching: trusted network must be Private":"Searching for paired PC");
    }catch(Exception ex){Log.Write("Network refresh: "+ex);State("Error: network setup; see logs");}finally{Interlocked.Exchange(ref ticking,0);}}
    void Refresh(){
        var next=LanAddress.Eligible();string key=string.Join(";",next.Select(a=>a.Address.ToString()).OrderBy(s=>s));
        lock(gate){if(key==adapterKey&&addresses.Length==next.Length)return;
            foreach(var l in listeners)l.Stop();foreach(var u in udp)u.Close();listeners.Clear();udp.Clear();addresses=next;adapterKey="retry-"+Guid.NewGuid().ToString("N");peer=null;peerNode=null;
            foreach(var a in next){
                var l=new TcpListener(a.Address,TcpPort);l.ExclusiveAddressUse=true;l.Start(4);listeners.Add(l);Task.Run(()=>Accept(l,a));
                Log.Write("Listening Private adapter "+a.Address+" TCP "+TcpPort+" UDP "+UdpPort);
            }
            // Broadcast datagrams require INADDR_ANY on Windows. Check the ingress interface
            // through IP_PKTINFO before parsing. TCP stays bound only to Private addresses.
            if(next.Length>0){var u=new UdpClient(AddressFamily.InterNetwork);u.ExclusiveAddressUse=true;u.Client.SetSocketOption(SocketOptionLevel.IP,SocketOptionName.PacketInformation,true);u.Client.Bind(new IPEndPoint(IPAddress.Any,UdpPort));udp.Add(u);Task.Run(()=>Discover(u));}adapterKey=key;
        }
    }
    bool Allowed(IPAddress remote){lock(gate)return addresses.Any(a=>a.Contains(remote));}
    void Discover(UdpClient u){try{while(!stop.IsCancellationRequested){
        byte[] buffer=new byte[1024];EndPoint endpoint=new IPEndPoint(IPAddress.Any,0);SocketFlags flags=SocketFlags.None;IPPacketInformation info;
        int length=u.Client.ReceiveMessageFrom(buffer,0,buffer.Length,ref flags,ref endpoint,out info);var remote=(IPEndPoint)endpoint;string n,nonce;int port;
        bool ingress;lock(gate)ingress=addresses.Any(a=>a.InterfaceIndex==info.Interface&&a.Contains(remote.Address));
        if(!ingress||!Discovery.Parse(buffer.Take(length).ToArray(),identity.DiscoveryKey,Discovery.Seconds,out n,out port,out nonce)||n==node)continue;
        lock(gate){long now=Discovery.Seconds;foreach(string old in nonces.Where(kv=>kv.Value<now-120).Select(kv=>kv.Key).ToArray())nonces.Remove(old);if(nonces.Count>4096||nonces.ContainsKey(nonce))continue;nonces[nonce]=now;}
        Probe(remote.Address,n);
    }}catch(ObjectDisposedException){}catch(SocketException ex){if(!stop.IsCancellationRequested)Log.Write("Discovery socket closed: "+ex.SocketErrorCode);}catch(Exception ex){Log.Write("Discovery receive: "+ex);}}
    public void Manual(string ip){IPAddress address;if(!IPAddress.TryParse(ip,out address)||address.AddressFamily!=AddressFamily.InterNetwork||!Allowed(address))throw new IOException("Enter an IPv4 address on the same Private physical LAN.");Probe(address,null);}
    void Probe(IPAddress ip,string expectedNode){if(Interlocked.Exchange(ref probing,1)!=0)return;Task.Run(()=>{try{
        using(var c=Connect(ip))try{using(var s=identity.Secure(c,false)){
            var w=new BinaryWriter(s);var r=new BinaryReader(s);Wire.Header(w,node);w.Write((byte)0);w.Flush();string remote=Wire.Header(r);if(remote==node||(expectedNode!=null&&remote!=expectedNode))throw new IOException("Peer identity mismatch.");
            lock(gate){bool changed=peer==null||!peer.Equals(ip);peer=ip;peerNode=remote;lastSeen=DateTime.UtcNow;if(changed)Log.Write("Authenticated peer "+ip+" node "+remote);}
            if(!Busy)State("Connected");Pump();
        }}finally{lock(gate)clients.Remove(c);}
    }catch(Exception ex){Log.Write("Peer authentication/connect: "+ex.Message);}finally{Interlocked.Exchange(ref probing,0);}});}
    TcpClient Connect(IPAddress ip){var c=new TcpClient(AddressFamily.InterNetwork);try{
        lock(gate){var a=addresses.FirstOrDefault(x=>x.Contains(ip));if(a==null)throw new IOException("Peer is no longer on a Private LAN.");c.Client.Bind(new IPEndPoint(a.Address,0));clients.Add(c);}
        var ar=c.BeginConnect(ip,TcpPort,null,null);using(ar.AsyncWaitHandle){if(!ar.AsyncWaitHandle.WaitOne(5000))throw new IOException("Peer connection timed out.");c.EndConnect(ar);}return c;
    }catch{lock(gate)clients.Remove(c);c.Close();throw;}}
    async Task Accept(TcpListener listener,LanAddress adapter){try{while(!stop.IsCancellationRequested){
        var c=await listener.AcceptTcpClientAsync().ConfigureAwait(false);var address=((IPEndPoint)c.Client.RemoteEndPoint).Address;
        lock(gate){clients.RemoveWhere(x=>!x.Connected);if(!adapter.Contains(address)||!Allowed(address)||clients.Count>=8){c.Close();continue;}clients.Add(c);}
        StartReceiver(c,address);
    }}catch(ObjectDisposedException){}catch(SocketException ex){if(!stop.IsCancellationRequested)Log.Write("TCP listener closed: "+ex.SocketErrorCode);}catch(Exception ex){Log.Write("Accept: "+ex);}}
    void StartReceiver(TcpClient c,IPAddress address){Task.Run(()=>Receive(c,address));}
    void Receive(TcpClient c,IPAddress ip){bool acquired=false;try{
        using(c)using(var s=identity.Secure(c,true)){
            var r=new BinaryReader(s);var w=new BinaryWriter(s);string remote=Wire.Header(r);if(remote==node)throw new IOException("Self connection refused.");byte op=r.ReadByte();
            if(op==0){Wire.Header(w,node);w.Flush();lock(gate){peer=ip;peerNode=remote;lastSeen=DateTime.UtcNow;}if(!Busy)State("Connected");return;}
            if(op!=1)throw new IOException("Unknown operation.");
            if(Interlocked.CompareExchange(ref receiving,1,0)!=0)throw new IOException("Receiver busy; sender will retry.");acquired=true;
            Log.Write("Transfer receive start from "+ip);State("Receiving");
            Transfer.Receive(s,Path.Combine(root,"Cache"),progress,publish);Log.Write("Transfer receive completed with SHA-256 verification");State("Ready to paste");
        }
    }catch(Exception ex){Log.Write("Receive/authentication failure: "+ex);if(acquired)State("Error: receive failed; see logs");}
    finally{if(acquired)Interlocked.Exchange(ref receiving,0);lock(gate)clients.Remove(c);}}
    public void Queue(string[] paths){lock(queueGate){pending=paths;queuedAt=DateTime.UtcNow;}Pump();}
    public void CancelPending(){lock(queueGate)pending=null;}
    void Pump(){
        IPAddress target;lock(gate)target=peer;if(target==null||stop.IsCancellationRequested)return;
        lock(queueGate){if(pending==null)return;}
        if(Interlocked.CompareExchange(ref sending,1,0)!=0)return;
        Task.Run(()=>{string[] selected=null;try{
            lock(queueGate){selected=pending;pending=null;if((DateTime.UtcNow-queuedAt).TotalMinutes>15)throw new IOException("Queued clipboard selection expired; copy again.");}
            if(selected==null)return;var m=Manifest.Build(selected);Log.Write("Transfer send start: "+m.Entries.Count+" entries / "+m.Total+" bytes");
            Exception failure=null;
            for(int attempt=0;attempt<3&&!stop.IsCancellationRequested;attempt++){
                try{State(attempt==0?"Sending":"Sending: retry "+(attempt+1));using(var c=Connect(target))try{using(var s=identity.Secure(c,false)){var w=new BinaryWriter(s);Wire.Header(w,node);w.Write((byte)1);w.Flush();Transfer.Send(s,m,progress);}}finally{lock(gate)clients.Remove(c);}failure=null;break;}
                catch(Exception ex){failure=ex;Log.Write("Send attempt "+(attempt+1)+": "+ex);if(attempt<2&&stop.Token.WaitHandle.WaitOne(1000*(attempt+1)))break;}
            }
            if(failure!=null)throw failure;if(stop.IsCancellationRequested)return;Log.Write("Transfer send acknowledged");State("Connected: transfer delivered");
        }catch(Exception ex){Log.Write("Transfer send failure: "+ex);State("Error: send failed; copy again or see logs");}
        finally{Interlocked.Exchange(ref sending,0);Pump();}});
    }
    public void Dispose(){stop.Cancel();if(timer!=null)timer.Dispose();lock(gate){foreach(var l in listeners)l.Stop();foreach(var u in udp)u.Close();foreach(var c in clients)c.Close();clients.Clear();} }
}
}
