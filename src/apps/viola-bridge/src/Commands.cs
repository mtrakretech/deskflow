// SPDX-FileCopyrightText: (C) 2026 Viola File Bridge contributors
// SPDX-License-Identifier: MIT
using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading.Tasks;

namespace Viola
{
    public sealed class Commands : IDisposable
    {
        volatile bool stopping;
        NamedPipeServerStream server;
        readonly string name;
        public Commands(string pipe, Action<string> execute)
        {
            name = pipe;
            var thread = new System.Threading.Thread(() => Listen(execute)) { IsBackground = true,
                                                                              Name = "Viola local commands" };
            thread.Start();
        }
        public static string Name(string root)
        {
            return "Viola-" +
                   Identity
                       .Hex(Identity.Hash(Encoding.UTF8.GetBytes(WindowsIdentity.GetCurrent().User.Value + "|" +
                                                                 Path.GetFullPath(root).ToLowerInvariant())))
                       .Substring(0, 24);
        }
        void Listen(Action<string> execute)
        {
            while (!stopping)
                try
                {
                    var security = new PipeSecurity();
                    security.SetAccessRuleProtection(true, false);
                    security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User,
                                                              PipeAccessRights.FullControl, AccessControlType.Allow));
                    using (var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                                                                PipeOptions.Asynchronous, 128, 128, security))
                    {
                        server = pipe;
                        if (stopping)
                            return;
                        pipe.WaitForConnection();
                        byte[] b = new byte[1];
                        var read = pipe.BeginRead(b, 0, 1, null, null);
                        int n;
                        using (read.AsyncWaitHandle)
                        {
                            if (!read.AsyncWaitHandle.WaitOne(2000))
                                continue;
                            n = pipe.EndRead(read);
                        }
                        if (n != 1 || b[0] > 3)
                            continue;
                        string[] commands = { "--background", "--history", "--settings", "--exit" };
                        pipe.WriteByte(1);
                        pipe.Flush();
                        execute(commands[b[0]]);
                    }
                }
                catch (ObjectDisposedException)
                {
                    if (stopping)
                        return;
                }
                catch (Exception ex)
                {
                    if (stopping)
                        return;
                    Log.Write("Local command: " + ex.GetType().Name);
                    System.Threading.Thread.Sleep(500);
                }
        }
        public static bool Send(string root, string command)
        {
            string[] commands = { "--background", "--history", "--settings", "--exit" };
            int code = Array.IndexOf(commands, command);
            if (code < 0)
                return false;
            try
            {
                using (var pipe =
                           new NamedPipeClientStream(".", Name(root), PipeDirection.InOut, PipeOptions.Asynchronous))
                {
                    pipe.Connect(2000);
                    pipe.WriteByte((byte)code);
                    pipe.Flush();
                    byte[] ack = new byte[1];
                    var read = pipe.BeginRead(ack, 0, 1, null, null);
                    using (read.AsyncWaitHandle)
                    {
                        if (!read.AsyncWaitHandle.WaitOne(5000))
                            return false;
                        return pipe.EndRead(read) == 1 && ack[0] == 1;
                    }
                }
            }
            catch (TimeoutException)
            {
                return false;
            }
            catch (Exception ex)
            {
                Log.Write("Local command send: " + ex.GetType().Name);
                return false;
            }
        }
        public void Dispose()
        {
            stopping = true;
            var pipe = server;
            if (pipe != null)
                pipe.Dispose();
        }
    }
}
