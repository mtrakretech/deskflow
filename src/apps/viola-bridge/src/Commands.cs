// SPDX-FileCopyrightText: (C) 2026 Viola File Bridge contributors
// SPDX-License-Identifier: MIT
using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading.Tasks;

namespace Viola {
public sealed class Commands : IDisposable {
    volatile bool stopping;NamedPipeServerStream server;readonly string name;
    public Commands(string pipe,Action<string> execute){name=pipe;Task.Run(()=>Listen(execute));}
    public static string Name(string root){return "Viola-"+Identity.Hex(Identity.Hash(Encoding.UTF8.GetBytes(WindowsIdentity.GetCurrent().User.Value+"|"+Path.GetFullPath(root).ToLowerInvariant()))).Substring(0,24);}
    void Listen(Action<string> execute){while(!stopping)try{
        var security=new PipeSecurity();security.SetAccessRuleProtection(true,false);security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User,PipeAccessRights.FullControl,AccessControlType.Allow));
        using(var pipe=new NamedPipeServerStream(name,PipeDirection.In,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous,128,128,security)){
            server=pipe;if(stopping)return;pipe.WaitForConnection();byte[] b=new byte[32];var read=pipe.BeginRead(b,0,b.Length,null,null);if(!read.AsyncWaitHandle.WaitOne(2000))continue;int n=pipe.EndRead(read);string command=Encoding.ASCII.GetString(b,0,n);if(command=="--history"||command=="--settings"||command=="--exit")execute(command);
        }
    }catch(ObjectDisposedException){if(stopping)return;}catch(Exception ex){if(stopping)return;Log.Write("Local command: "+ex.GetType().Name);System.Threading.Thread.Sleep(500);}}
    public static bool Send(string root,string command){try{using(var pipe=new NamedPipeClientStream(".",Name(root),PipeDirection.Out)){pipe.Connect(2000);byte[] b=Encoding.ASCII.GetBytes(command);pipe.Write(b,0,b.Length);return true;}}catch{return false;}}
    public void Dispose(){stopping=true;var pipe=server;if(pipe!=null)pipe.Dispose();}
}
}
