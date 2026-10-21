using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Windows.Forms;
using System.Diagnostics;
using System.Collections.Generic;
namespace Viola
{
    public static class Tests
    {
        static int passed, failed;
        static string root;
        static bool liveOnly, noClipboard, liveHistoryOnly;
        static void Check(bool ok, string why)
        {
            if (!ok)
                throw new Exception(why);
        }
        static void Reject(Action a)
        {
            try
            {
                a();
            }
            catch
            {
                return;
            }
            throw new Exception("Expected rejection");
        }
        static void Test(string name, Action action)
        {
            if (liveHistoryOnly && !name.StartsWith("Live history"))
                return;
            if (liveOnly && !name.StartsWith("Installed tray"))
                return;
            if (noClipboard && name.StartsWith("CF_HDROP"))
            {
                Console.WriteLine("SKIP " + name + " (non-interactive runner)");
                return;
            }
            try
            {
                action();
                passed++;
                Console.WriteLine("PASS " + name);
            }
            catch (Exception ex)
            {
                failed++;
                Console.WriteLine("FAIL " + name + ": " + ex);
            }
        }
        static byte[] Digest(string p)
        {
            using (var s = File.OpenRead(p)) using (var h = SHA256.Create()) return h.ComputeHash(s);
        }
        static MemoryStream Input(byte[] bytes)
        {
            var s = new MemoryStream();
            s.Write(bytes, 0, bytes.Length);
            s.Position = 0;
            return s;
        }
        static byte[] Serialize(Manifest m)
        {
            using (var s = new MemoryStream())
            { // Send needs an ACK after payload; duplex wrapper supplies it.
                Transfer.Send(new AckStream(s), m, null);
                return s.ToArray();
            }
        }
        sealed class AckStream : Stream
        {
            Stream inner;
            public AckStream(Stream s)
            {
                inner = s;
            }
            public override bool CanRead
            {
                get {
                    return true;
                }
            }
            public override bool CanWrite
            {
                get {
                    return true;
                }
            }
            public override bool CanSeek
            {
                get {
                    return false;
                }
            }
            public override long Length
            {
                get {
                    return inner.Length;
                }
            }
            public override long Position
            {
                get {
                    return inner.Position;
                }
                set {
                    throw new NotSupportedException();
                }
            }
            public override int Read(byte[] b, int o, int n)
            {
                b[o] = 1;
                return 1;
            }
            public override void Write(byte[] b, int o, int n)
            {
                inner.Write(b, o, n);
            }
            public override void Flush()
            {
                inner.Flush();
            }
            public override long Seek(long a, SeekOrigin b)
            {
                throw new NotSupportedException();
            }
            public override void SetLength(long n)
            {
                inner.SetLength(n);
            }
        }
        static void Roundtrip(Identity a, Identity b, Manifest m, string cache)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            Exception error = null;
            var task = Task.Run(() =>
                                {
                                    try
                                    {
                                        using (var c = listener.AcceptTcpClient()) using (var s = b.Secure(c, true))
                                        {
                                            Transfer.Receive(s, cache, null, null);
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        error = ex;
                                    }
                                });
            try
            {
                using (var c = new TcpClient())
                {
                    c.Connect(IPAddress.Loopback, port);
                    using (var s = a.Secure(c, false)) Transfer.Send(s, m, null);
                }
                if (!task.Wait(60000))
                    throw new Exception("Receiver timeout");
                if (error != null)
                    throw error;
            }
            finally
            {
                listener.Stop();
            }
        }
        [STAThread]
        public static int Main(string[] args)
        {
            liveOnly = args.Contains("--live-only");
            liveHistoryOnly = args.Contains("--live-history-only");
            noClipboard = args.Contains("--no-clipboard");
            root = Path.Combine(Path.GetTempPath(), "Viola-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            Log.Root = root;
            Console.WriteLine("Evidence directory: " + root);
            string source = Path.Combine(root, "source");
            Directory.CreateDirectory(source);
            Directory.CreateDirectory(Path.Combine(source, "subfolder"));
            File.WriteAllText(Path.Combine(source, "hello.txt"), "Viola protocol test");
            File.WriteAllText(Path.Combine(source, "subfolder", "مرحبا 日本語.txt"), "Unicode content ✔",
                              Encoding.UTF8);
            File.WriteAllText(Path.Combine(source, "with spaces.txt"), "space");
            Directory.CreateDirectory(Path.Combine(source, "empty"));
            Identity identity = null, other = null;
            Test("Generate identity, DPAPI save/reload, PFX password",
                 () =>
                 {
                     identity = Identity.LoadOrCreate(Path.Combine(root, "identity"));
                     using (var loaded = Identity.LoadOrCreate(Path.Combine(root, "identity")))
                         Check(loaded.Pin == identity.Pin && Identity.Equal(loaded.DiscoveryKey, identity.DiscoveryKey),
                               "Identity mismatch");
                     byte[] pfx = identity.Certificate.Export(
                         System.Security.Cryptography.X509Certificates.X509ContentType.Pfx, "twelve-chars-password");
                     Reject(() => Identity.FromPfx(pfx, "wrong"));
                     using (var loaded = Identity.FromPfx(pfx, "twelve-chars-password"))
                         Check(loaded.Pin == identity.Pin, "PFX mismatch");
                     other = Identity.Create();
                 });
            Test("Metadata, recursive folders, Unicode, spaces, empty dirs",
                 () =>
                 {
                     var m = Manifest.Build(new[] { source });
                     Check(m.Entries.Count == 6 && m.Total > 0, "Wrong manifest count " + m.Entries.Count);
                     Check(m.Entries.Any(e => e.Relative.Contains("مرحبا")), "Missing Unicode");
                     Check(m.Entries.Any(e => e.Directory && e.Relative.EndsWith("empty")), "Missing empty directory");
                 });
            Test("Duplicate top names disambiguated",
                 () =>
                 {
                     string p = Path.Combine(root, "other", "source");
                     Directory.CreateDirectory(p);
                     var m = Manifest.Build(new[] { source, p });
                     Check(m.Tops.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 2, "Name collision");
                 });
            Test("Traversal, absolute paths, ADS, devices, malformed filenames rejected",
                 () =>
                 {
                     foreach (string rel in new[] { "../evil", "a/../../evil", "C:\\evil", "/evil", "\\\\server\\share",
                                                    "a:b", "CON", "aux.txt", "a/./b", "a//b", "tail.", "tail ", "a\0b",
                                                    "COM1.txt" })
                         Reject(() => SafePath.Resolve(root, rel));
                     SafePath.Resolve(root, "مرحبا/with spaces.txt");
                 });
            Test("Authenticated discovery, corruption, wrong secret, freshness",
                 () =>
                 {
                     string n = Guid.NewGuid().ToString("N"), nonce = Guid.NewGuid().ToString("N"), outNode, outNonce;
                     int port;
                     byte[] p = Discovery.Packet(identity.DiscoveryKey, n, 48751, Discovery.Seconds, nonce);
                     Check(Discovery.Parse(p, identity.DiscoveryKey, Discovery.Seconds, out outNode, out port,
                                           out outNonce),
                           "Valid packet refused");
                     Check(!Discovery.Parse(p, other.DiscoveryKey, Discovery.Seconds, out outNode, out port,
                                            out outNonce),
                           "Wrong secret accepted");
                     p[p.Length - 1] ^= 1;
                     Check(!Discovery.Parse(p, identity.DiscoveryKey, Discovery.Seconds, out outNode, out port,
                                            out outNonce),
                           "Tamper accepted");
                     p = Discovery.Packet(identity.DiscoveryKey, n, 48751, Discovery.Seconds - 120, nonce);
                     Check(!Discovery.Parse(p, identity.DiscoveryKey, Discovery.Seconds, out outNode, out port,
                                            out outNonce),
                           "Stale accepted");
                 });
            Test("Framing truncation, negative/oversized lengths rejected",
                 () =>
                 {
                     Reject(() => Wire.ReadExact(new MemoryStream(new byte[2]), 3));
                     Reject(() => Wire.ReadExact(new MemoryStream(), Wire.BufferSize + 1));
                     Reject(() => Wire.Text(new BinaryReader(new MemoryStream(BitConverter.GetBytes(-1)))));
                     Reject(() => Wire.Header(new BinaryReader(new MemoryStream(new byte[4]))));
                 });
            Test("Verified folder roundtrip over mutual TLS",
                 () =>
                 {
                     string cache = Path.Combine(root, "tls-cache");
                     Roundtrip(identity, identity, Manifest.Build(new[] { source }), cache);
                     string dest = Path.Combine(Directory.GetDirectories(cache).Single(), "source");
                     foreach (string p in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
                         Check(Identity.Equal(Digest(p), Digest(Path.Combine(dest, p.Substring(source.Length + 1)))),
                               "File content differs");
                     Check(Directory.Exists(Path.Combine(dest, "empty")), "Empty directory missing");
                 });
            Test("Authentication rejects unrelated pairing certificate",
                 () => Reject(() => Roundtrip(other, identity, Manifest.Build(new[] { source }),
                                              Path.Combine(root, "reject-cache"))));
            Test("Corrupt file hash rejected, partial cache removed",
                 () =>
                 {
                     byte[] payload = Serialize(Manifest.Build(new[] { Path.Combine(source, "hello.txt") }));
                     payload[payload.Length - 1] ^= 1;
                     string cache = Path.Combine(root, "corrupt-cache");
                     Reject(() => Transfer.Receive(new MemoryStream(payload), cache, null, null));
                     Check(!Directory.EnumerateDirectories(cache).Any(), "Partial remains");
                 });
            Test("Connection loss removes partial transfer; retry succeeds",
                 () =>
                 {
                     byte[] payload = Serialize(Manifest.Build(new[] { source }));
                     string cache = Path.Combine(root, "loss-cache");
                     Reject(() =>
                                Transfer.Receive(Input(payload.Take(payload.Length / 2).ToArray()), cache, null, null));
                     Check(!Directory.EnumerateDirectories(cache).Any(), "Partial remains");
                     Transfer.Receive(Input(payload), cache, null, null);
                     Check(Directory.GetDirectories(cache).Length == 1, "Retry failed");
                 });
            Test("Receive collision creates unique cache objects",
                 () =>
                 {
                     byte[] payload = Serialize(Manifest.Build(new[] { source }));
                     string cache = Path.Combine(root, "collision-cache");
                     Transfer.Receive(Input(payload), cache, null, null);
                     Transfer.Receive(Input(payload), cache, null, null);
                     Check(Directory.GetDirectories(cache).Length == 2, "Objects overwritten");
                 });
            Test("Cleanup protects clipboard data and unrelated cache directory",
                 () =>
                 {
                     string cache = Path.Combine(root, "cleanup");
                     Directory.CreateDirectory(cache);
                     string keep = Path.Combine(cache, Guid.NewGuid().ToString("N")),
                            old = Path.Combine(cache, Guid.NewGuid().ToString("N")),
                            unrelated = Path.Combine(cache, "user-stuff");
                     foreach (string p in new[] { keep, old, unrelated })
                     {
                         Directory.CreateDirectory(p);
                         File.WriteAllText(Path.Combine(p, "a.txt"), "a");
                         Directory.SetLastWriteTimeUtc(p, DateTime.UtcNow.AddDays(-5));
                     }
                     Cache.Cleanup(cache, new[] { Path.Combine(keep, "a.txt") }, DateTime.UtcNow);
                     Check(Directory.Exists(keep) && Directory.Exists(unrelated) && !Directory.Exists(old),
                           "Cleanup boundary failed");
                     Reject(() => Cache.DeleteOwned(cache, source));
                 });
            Test("CF_HDROP, STA listener, copy effect, text preservation",
                 () =>
                 {
                     // Save common clipboard formats, then restore the original IDataObject if possible.
                     var original = Clipboard.GetDataObject();
                     bool notified = false;
                     using (var window = new ClipboardWindow())
                     {
                         var hwnd = window.Handle;
                         window.Changed += () => notified = true;
                         try
                         {
                             var d = new DataObject();
                             var list = new System.Collections.Specialized.StringCollection();
                             list.Add(Path.Combine(source, "hello.txt"));
                             d.SetFileDropList(list);
                             d.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(1)));
                             Clipboard.SetDataObject(d, true, 12, 80);
                             var end = DateTime.UtcNow.AddSeconds(3);
                             while (!notified && DateTime.UtcNow < end)
                             {
                                 Application.DoEvents();
                                 Thread.Sleep(20);
                             }
                             Check(notified, "No WM_CLIPBOARDUPDATE");
                             Check(Clipboard.ContainsFileDropList() && Clipboard.GetFileDropList()[0] == list[0],
                                   "Invalid CF_HDROP");
                             using (var effect = (MemoryStream)Clipboard.GetData("Preferred DropEffect"))
                                 Check(BitConverter.ToInt32(effect.ToArray(), 0) == 1, "Not COPY");
                             Clipboard.SetText("Viola text preservation probe");
                             Application.DoEvents();
                             Check(Clipboard.GetText() == "Viola text preservation probe" &&
                                       !Clipboard.ContainsFileDropList(),
                                   "Text altered");
                         }
                         finally
                         {
                             if (original != null)
                                 try
                                 {
                                     Clipboard.SetDataObject(original, true, 12, 80);
                                 }
                                 catch
                                 {
                                 }
                         }
                     }
                 });
            if (args.Contains("--large"))
                Test("Stream >4 GiB file over mutual TLS with bounded memory",
                     () =>
                     {
                         string path = Path.Combine(root, "large-video-test.bin");
                         long length = 4L * 1024 * 1024 * 1024 + 12345;
                         using (var f = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
                         {
                             f.SetLength(length);
                             f.Position = length - 16;
                             f.Write(Encoding.ASCII.GetBytes("Viola-large-tail!"), 0, 16);
                         }
                         var watch = Stopwatch.StartNew();
                         string cache = Path.Combine(root, "large-cache");
                         Roundtrip(identity, identity, Manifest.Build(new[] { path }), cache);
                         string dest = Path.Combine(Directory.GetDirectories(cache).Single(), Path.GetFileName(path));
                         Check(new FileInfo(dest).Length == length && Identity.Equal(Digest(path), Digest(dest)),
                               "Large file mismatch");
                         Console.WriteLine("Large bytes=" + length + " elapsed=" + watch.Elapsed +
                                           " peakWorkingSet=" + Process.GetCurrentProcess().PeakWorkingSet64);
                         File.Delete(path);
                         Cache.DeleteOwned(cache, Directory.GetDirectories(cache).Single());
                     });
            if (args.Contains("--live"))
                Test("Installed tray receives TLS transfer and publishes valid CF_HDROP",
                     () =>
                     {
                         string installed =
                             Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                          "ViolaFileBridge");
                         var original = Clipboard.GetDataObject();
                         using (var id = Identity.LoadOrCreate(installed)) using (var client = new TcpClient())
                         {
                             var address = LanAddress.Eligible().First().Address;
                             client.Connect(address, Bridge.TcpPort);
                             using (var secure = id.Secure(client, false))
                             {
                                 var w = new BinaryWriter(secure);
                                 Wire.Header(w, Guid.NewGuid().ToString("N"));
                                 w.Write((byte)1);
                                 w.Flush();
                                 Transfer.Send(secure, Manifest.Build(new[] { source }), null);
                             }
                             try
                             {
                                 Check(Clipboard.ContainsFileDropList(), "Tray did not publish FileDrop");
                                 string received = Clipboard.GetFileDropList()[0];
                                 Check(SafePath.IsInside(Path.Combine(installed, "Cache"), received),
                                       "Clipboard outside cache");
                                 Check(Identity.Equal(Digest(Path.Combine(source, "hello.txt")),
                                                      Digest(Path.Combine(received, "hello.txt"))),
                                       "Live content differs");
                             }
                             finally
                             {
                                 if (original != null)
                                     Clipboard.SetDataObject(original, true, 12, 80);
                             }
                         }
                     });
            HistoryTests.Run(Test, root);
            if (liveHistoryOnly)
                Test("Live history records real text and bitmap clipboard events", () => HistoryTests.Live(root));
            if (identity != null)
                identity.Dispose();
            if (other != null)
                other.Dispose();
            Console.WriteLine("RESULT " + passed + " passed / " + failed + " failed");
            return failed == 0 ? 0 : 1;
        }
    }
}
