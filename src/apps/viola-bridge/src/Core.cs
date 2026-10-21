using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Net;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Authentication;
using System.Threading;
using System.Diagnostics;

namespace Viola
{
    public static class Log
    {
        static readonly object gate = new object();
        public static string Root =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ViolaFileBridge");
        public static bool Debug;
        public static void Write(string message)
        {
            lock (gate)
            {
                try
                {
                    string dir = Path.Combine(Root, "Logs");
                    Directory.CreateDirectory(dir);
                    string path = Path.Combine(dir, DateTime.UtcNow.ToString("yyyy-MM-dd") + ".log");
                    if (!File.Exists(path) || new FileInfo(path).Length < 8 * 1024 * 1024)
                        File.AppendAllText(path, DateTime.UtcNow.ToString("o") + " " + message + Environment.NewLine,
                                           Encoding.UTF8);
                    if (Debug)
                        System.Diagnostics.Debug.WriteLine(message);
                }
                catch
                {
                }
            }
        }
    }

    public sealed class Identity : IDisposable
    {
        public X509Certificate2 Certificate;
        public byte[] DiscoveryKey;
        public string Pin;
        public static byte[] Hash(byte[] data)
        {
            using (var h = SHA256.Create()) return h.ComputeHash(data);
        }
        public static string Hex(byte[] data)
        {
            return BitConverter.ToString(data).Replace("-", "").ToLowerInvariant();
        }
        public static bool Equal(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length)
                return false;
            int v = 0;
            for (int i = 0; i < a.Length; i++)
                v |= a[i] ^ b[i];
            return v == 0;
        }
        public static Identity FromPfx(byte[] pfx, string password)
        {
            var id = new Identity();
            // Schannel on .NET Framework requires a Windows key container. Without
            // PersistKeySet, the temporary import container is removed on disposal.
            id.Certificate =
                new X509Certificate2(pfx, password, X509KeyStorageFlags.Exportable | X509KeyStorageFlags.UserKeySet);
            if (!id.Certificate.HasPrivateKey)
                throw new InvalidDataException("Pairing file has no private key.");
            if (id.Certificate.NotAfter.ToUniversalTime() <= DateTime.UtcNow)
                throw new InvalidDataException("Pairing certificate expired.");
            id.Pin = Hex(Hash(id.Certificate.RawData));
            using (var rsa = id.Certificate.GetRSAPrivateKey())
            {
                if (rsa == null)
                    throw new InvalidDataException("RSA pairing certificate required.");
                id.DiscoveryKey = Hash(rsa.SignData(Encoding.UTF8.GetBytes("ViolaFileBridge discovery v2"),
                                                    HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
            }
            return id;
        }
        public static Identity Create()
        {
            using (var rsa = RSA.Create(2048))
            {
                var request = new CertificateRequest("CN=ViolaFileBridge-" + Guid.NewGuid().ToString("N"), rsa,
                                                     HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                request.CertificateExtensions.Add(new X509KeyUsageExtension(
                    X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
                using (var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),
                                                           DateTimeOffset.UtcNow.AddYears(10)))
                {
                    return FromPfx(cert.Export(X509ContentType.Pfx, ""), "");
                }
            }
        }
        public static Identity LoadOrCreate(string root)
        {
            Directory.CreateDirectory(root);
            string path = Path.Combine(root, "pairing.dpapi");
            if (File.Exists(path))
                return FromPfx(ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.CurrentUser),
                               "");
            var id = Create();
            id.Save(root);
            return id;
        }
        public void Save(string root)
        {
            Directory.CreateDirectory(root);
            byte[] plain = Certificate.Export(X509ContentType.Pfx, "");
            try
            {
                string path = Path.Combine(root, "pairing.dpapi"), tmp = path + ".new";
                File.WriteAllBytes(tmp, ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser));
                if (File.Exists(path))
                    File.Replace(tmp, path, null);
                else
                    File.Move(tmp, path);
            }
            finally
            {
                Array.Clear(plain, 0, plain.Length);
            }
        }
        public bool Validate(object sender, X509Certificate cert, X509Chain chain, SslPolicyErrors errors)
        {
            return cert != null && Equal(Hash(cert.GetRawCertData()), Hash(Certificate.RawData)) &&
                   DateTime.UtcNow >= Certificate.NotBefore.ToUniversalTime() &&
                   DateTime.UtcNow < Certificate.NotAfter.ToUniversalTime();
        }
        public SslStream Secure(TcpClient client, bool server)
        {
            client.NoDelay = true;
            client.ReceiveTimeout = 30000;
            client.SendTimeout = 30000;
            var s = new SslStream(client.GetStream(), false, Validate);
            s.ReadTimeout = 30000;
            s.WriteTimeout = 30000;
            try
            {
                if (server)
                    s.AuthenticateAsServer(Certificate, true, SslProtocols.Tls12, false);
                else
                    s.AuthenticateAsClient("ViolaFileBridge", new X509CertificateCollection(new[] { Certificate }),
                                           SslProtocols.Tls12, false);
                if (!s.IsMutuallyAuthenticated || !s.IsEncrypted || !s.IsSigned)
                    throw new AuthenticationException("Mutual TLS required.");
                return s;
            }
            catch
            {
                s.Dispose();
                throw;
            }
        }
        public void Dispose()
        {
            if (Certificate != null)
                Certificate.Dispose();
            if (DiscoveryKey != null)
                Array.Clear(DiscoveryKey, 0, DiscoveryKey.Length);
        }
    }

    public static class Wire
    {
        public const int BufferSize = 256 * 1024;
        public static byte[] ReadExact(Stream s, int length)
        {
            if (length < 0 || length > BufferSize)
                throw new InvalidDataException("Frame length limit.");
            byte[] b = new byte[length];
            int n = 0;
            while (n < length)
            {
                int k = s.Read(b, n, length - n);
                if (k == 0)
                    throw new EndOfStreamException("Connection lost.");
                n += k;
            }
            return b;
        }
        public static void Text(BinaryWriter w, string text)
        {
            byte[] b = Encoding.UTF8.GetBytes(text);
            if (b.Length > 32768)
                throw new InvalidDataException("Text limit.");
            w.Write(b.Length);
            w.Write(b);
        }
        public static string Text(BinaryReader r)
        {
            int n = r.ReadInt32();
            if (n < 0 || n > 32768)
                throw new InvalidDataException("Text frame limit.");
            return new UTF8Encoding(false, true).GetString(ReadExact(r.BaseStream, n));
        }
        public static void Header(BinaryWriter w, string node)
        {
            w.Write(0x32424656);
            Text(w, node);
        }
        public static string Header(BinaryReader r)
        {
            if (r.ReadInt32() != 0x32424656)
                throw new InvalidDataException("Protocol version mismatch.");
            string n = Text(r);
            Guid.ParseExact(n, "N");
            return n;
        }
    }

    public sealed class Entry
    {
        public string Source, Relative;
        public bool Directory;
        public long Size, Ticks;
    }
    public sealed class Manifest
    {
        public List<Entry> Entries = new List<Entry>();
        public string[] Tops;
        public long Total;
        public static Manifest Build(string[] paths)
        {
            if (paths.Length == 0 || paths.Length > 4096)
                throw new InvalidDataException("Select between 1 and 4096 items.");
            var m = new Manifest();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var tops = new List<string>();
            foreach (string p in paths)
            {
                string full = Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar), name = Path.GetFileName(full);
                if (string.IsNullOrEmpty(name))
                    throw new InvalidDataException("Copy a folder rather than an entire drive.");
                string candidate = name;
                int suffix = 2;
                while (!names.Add(candidate))
                    candidate =
                        Path.GetFileNameWithoutExtension(name) + " (" + (suffix++) + ")" + Path.GetExtension(name);
                SafePath.Component(candidate);
                tops.Add(candidate);
                m.Add(full, candidate, 0);
            }
            m.Tops = tops.ToArray();
            return m;
        }
        void Add(string physical, string relative, int depth)
        {
            if (depth > 128 || Entries.Count >= 100000)
                throw new InvalidDataException("Folder depth or item count limit exceeded.");
            FileAttributes a = File.GetAttributes(physical);
            if ((a & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Symbolic links/junctions are not transferred: " + physical);
            bool dir = (a & FileAttributes.Directory) != 0;
            var e = new Entry { Source = physical, Relative = relative, Directory = dir,
                                Ticks = dir ? System.IO.Directory.GetLastWriteTimeUtc(physical).Ticks
                                            : File.GetLastWriteTimeUtc(physical).Ticks,
                                Size = dir ? 0 : new FileInfo(physical).Length };
            Entries.Add(e);
            Total = checked(Total + e.Size);
            if (dir)
                foreach (string child in System.IO.Directory.EnumerateFileSystemEntries(physical))
                {
                    string name = Path.GetFileName(child);
                    SafePath.Component(name);
                    Add(child, relative + "/" + name, depth + 1);
                }
        }
    }
    public static class SafePath
    {
        public static void Component(string c)
        {
            if (string.IsNullOrEmpty(c) || c == "." || c == ".." || c.Length > 255 || c.EndsWith(".") ||
                c.EndsWith(" ") || c.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || c.Contains("/") ||
                c.Contains("\\"))
                throw new InvalidDataException("Invalid path component.");
            string stem = c.Split('.') [0].TrimEnd(' ').ToUpperInvariant();
            if (stem == "CON" || stem == "PRN" || stem == "AUX" || stem == "NUL" || stem == "CONIN$" ||
                stem == "CONOUT$" || System.Text.RegularExpressions.Regex.IsMatch(stem, @"^(COM|LPT)[1-9¹²³]$"))
                throw new InvalidDataException("Reserved Windows filename.");
        }
        public static string Resolve(string root, string relative)
        {
            if (relative == null || relative.Length > 30000 || Path.IsPathRooted(relative) || relative.Contains(":"))
                throw new InvalidDataException("Unsafe path.");
            string[] parts = relative.Split(new[] { '/', '\\' });
            if (parts.Length > 129)
                throw new InvalidDataException("Path depth limit.");
            foreach (string p in parts)
                Component(p);
            string full = Path.GetFullPath(Path.Combine(root, Path.Combine(parts)));
            string prefix = Path.GetFullPath(root).TrimEnd('\\') + "\\";
            if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Path escapes cache.");
            // Refuse reparse ancestors even if cache was modified by another local process.
            string at = root;
            if (System.IO.Directory.Exists(at) && (File.GetAttributes(at) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Reparse cache root.");
            foreach (string part in parts)
            {
                at = Path.Combine(at, part);
                if ((File.Exists(at) || System.IO.Directory.Exists(at)) &&
                    (File.GetAttributes(at) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Reparse destination.");
            }
            return full;
        }
        public static bool IsInside(string root, string path)
        {
            try
            {
                return Path.GetFullPath(path).StartsWith(Path.GetFullPath(root).TrimEnd('\\') + "\\",
                                                         StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
    }

    public sealed class Progress
    {
        public string State, Name;
        public long Done, Total;
        public double Seconds;
    }
    public static class Transfer
    {
        public static void Send(Stream stream, Manifest m, Action<Progress> progress)
        {
            var w = new BinaryWriter(stream, Encoding.UTF8, true);
            var r = new BinaryReader(stream, Encoding.UTF8, true);
            w.Write(m.Entries.Count);
            w.Write(m.Tops.Length);
            foreach (string top in m.Tops)
                Wire.Text(w, top);
            w.Write(m.Total);
            w.Flush();
            byte[] buffer = new byte[Wire.BufferSize];
            long done = 0;
            var watch = Stopwatch.StartNew();
            foreach (var e in m.Entries)
            {
                w.Write(e.Directory);
                Wire.Text(w, e.Relative);
                w.Write(e.Size);
                w.Write(e.Ticks);
                w.Flush();
                if (!e.Directory)
                {
                    // FileShare.Read prevents changes/deletion for the duration of the read.
                    using (var f =
                               new FileStream(e.Source, FileMode.Open, FileAccess.Read, FileShare.Read, Wire.BufferSize,
                                              FileOptions.SequentialScan)) using (var h = SHA256.Create())
                    {
                        if (f.Length != e.Size || File.GetLastWriteTimeUtc(e.Source).Ticks != e.Ticks)
                            throw new IOException("Source changed after selection. Copy it again.");
                        long remaining = e.Size;
                        while (remaining > 0)
                        {
                            int n = f.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                            if (n == 0)
                                throw new EndOfStreamException("Source truncated.");
                            stream.Write(buffer, 0, n);
                            h.TransformBlock(buffer, 0, n, null, 0);
                            remaining -= n;
                            done += n;
                            if (progress != null)
                                progress(new Progress { State = "Sending", Name = Path.GetFileName(e.Relative),
                                                        Done = done, Total = m.Total,
                                                        Seconds = watch.Elapsed.TotalSeconds });
                        }
                        h.TransformFinalBlock(new byte[0], 0, 0);
                        w.Write(h.Hash);
                        w.Flush();
                    }
                }
            }
            w.Flush();
            if (r.ReadByte() != 1)
                throw new IOException("Peer did not confirm verified transfer.");
        }
        public static string[] Receive(Stream stream, string cache, Action<Progress> progress, Action<string[]> publish)
        {
            var r = new BinaryReader(stream, Encoding.UTF8, true);
            var w = new BinaryWriter(stream, Encoding.UTF8, true);
            int count = r.ReadInt32(), topCount = r.ReadInt32();
            if (count < 1 || count > 100000 || topCount < 1 || topCount > 4096 || topCount > count)
                throw new InvalidDataException("Manifest limits.");
            string[] tops = new string[topCount];
            var topSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < tops.Length; i++)
            {
                tops[i] = Wire.Text(r);
                SafePath.Component(tops[i]);
                if (!topSet.Add(tops[i]))
                    throw new InvalidDataException("Duplicate top item.");
            }
            long total = r.ReadInt64();
            if (total < 0)
                throw new InvalidDataException("Negative transfer size.");
            Directory.CreateDirectory(cache);
            var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(cache)));
            if (total > Math.Max(0, drive.AvailableFreeSpace - 64 * 1024 * 1024))
                throw new IOException("Insufficient cache disk space.");
            string id = Guid.NewGuid().ToString("N"), stage = Path.Combine(cache, id + ".partial"),
                   final = Path.Combine(cache, id);
            Directory.CreateDirectory(stage);
            bool committed = false;
            long done = 0;
            var watch = Stopwatch.StartNew();
            byte[] buffer = new byte[Wire.BufferSize];
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var dirs = new List<Entry>();
            try
            {
                for (int i = 0; i < count; i++)
                {
                    bool dir = r.ReadBoolean();
                    string rel = Wire.Text(r), dest = SafePath.Resolve(stage, rel);
                    long size = r.ReadInt64(), ticks = r.ReadInt64();
                    if (!seen.Add(dest) || size < 0 || (dir && size != 0) || size > total - done ||
                        ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks)
                        throw new InvalidDataException("Invalid entry metadata.");
                    string top = rel.Split('/', '\\')[0];
                    if (!topSet.Contains(top))
                        throw new InvalidDataException("Entry outside selected roots.");
                    string parent = Path.GetDirectoryName(dest);
                    if (!string.Equals(parent, stage, StringComparison.OrdinalIgnoreCase) && !seen.Contains(parent))
                        throw new InvalidDataException("Missing parent directory.");
                    if (dir)
                    {
                        Directory.CreateDirectory(dest);
                        dirs.Add(new Entry { Source = dest, Ticks = ticks });
                        continue;
                    }
                    using (var f = new FileStream(dest, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                                                  Wire.BufferSize,
                                                  FileOptions.SequentialScan)) using (var h = SHA256.Create())
                    {
                        long left = size;
                        while (left > 0)
                        {
                            int n = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, left));
                            if (n == 0)
                                throw new EndOfStreamException("Connection lost during file.");
                            f.Write(buffer, 0, n);
                            h.TransformBlock(buffer, 0, n, null, 0);
                            left -= n;
                            done += n;
                            if (progress != null)
                                progress(new Progress { State = "Receiving", Name = Path.GetFileName(rel), Done = done,
                                                        Total = total, Seconds = watch.Elapsed.TotalSeconds });
                        }
                        h.TransformFinalBlock(new byte[0], 0, 0);
                        if (!Identity.Equal(h.Hash, Wire.ReadExact(stream, 32)))
                            throw new InvalidDataException("SHA-256 verification failed.");
                        f.Flush(true);
                    }
                    File.SetLastWriteTimeUtc(dest, new DateTime(ticks, DateTimeKind.Utc));
                }
                if (done != total)
                    throw new InvalidDataException("Total byte mismatch.");
                foreach (string top in tops)
                {
                    string p = SafePath.Resolve(stage, top);
                    if (!File.Exists(p) && !Directory.Exists(p))
                        throw new InvalidDataException("Missing root item.");
                }
                for (int i = dirs.Count - 1; i >= 0; i--)
                    Directory.SetLastWriteTimeUtc(dirs[i].Source, new DateTime(dirs[i].Ticks, DateTimeKind.Utc));
                Directory.Move(stage, final);
                committed = true;
                Directory.SetLastWriteTimeUtc(final, DateTime.UtcNow);
                string[] paths = tops.Select(t => SafePath.Resolve(final, t)).ToArray();
                // ACK only after data is verified and the clipboard accepts the local paths.
                if (publish != null)
                    publish(paths);
                w.Write((byte)1);
                w.Flush();
                return paths;
            }
            finally
            {
                if (!committed)
                    Cache.DeleteOwned(cache, stage);
            }
        }
    }

    public static class Cache
    {
        public static void DeleteOwned(string root, string path)
        {
            if (!SafePath.IsInside(root, path) ||
                !string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), Path.GetFullPath(root).TrimEnd('\\'),
                               StringComparison.OrdinalIgnoreCase))
                throw new IOException("Cleanup outside owned cache refused.");
            if (Directory.Exists(path))
            {
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Cleanup reparse root refused.");
                DeleteTree(path);
            }
        }
        static void DeleteTree(string dir)
        {
            foreach (string p in Directory.EnumerateFileSystemEntries(dir))
            {
                var a = File.GetAttributes(p);
                if ((a & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Cleanup reparse entry refused.");
                if ((a & FileAttributes.Directory) != 0)
                    DeleteTree(p);
                else
                {
                    File.SetAttributes(p, FileAttributes.Normal);
                    File.Delete(p);
                }
            }
            Directory.Delete(dir);
        }
        public static void Cleanup(string cache, string[] clipboard, DateTime now)
        {
            if (!Directory.Exists(cache))
                return;
            foreach (string dir in Directory.EnumerateDirectories(cache))
            {
                string name = Path.GetFileName(dir),
                       raw = name.EndsWith(".partial") ? name.Substring(0, name.Length - 8) : name;
                Guid parsed;
                if (!Guid.TryParseExact(raw, "N", out parsed))
                    continue;
                if (clipboard != null && clipboard.Any(p => SafePath.IsInside(dir, p)))
                    continue;
                double hours = (now - Directory.GetLastWriteTimeUtc(dir)).TotalHours;
                if (hours > (name.EndsWith(".partial") ? 24 : 72))
                    try
                    {
                        DeleteOwned(cache, dir);
                    }
                    catch (Exception ex)
                    {
                        Log.Write("Cache cleanup: " + ex.Message);
                    }
            }
        }
    }

    public static class Discovery
    {
        public static byte[] Packet(byte[] key, string node, int port, long seconds, string nonce)
        {
            string body = "VFB2|" + node + "|" + port + "|" + seconds + "|" + nonce;
            using (var h = new HMACSHA256(key)) return Encoding.ASCII.GetBytes(
                body + "|" + Identity.Hex(h.ComputeHash(Encoding.ASCII.GetBytes(body))));
        }
        public static bool Parse(byte[] packet, byte[] key, long now, out string node, out int port, out string nonce)
        {
            node = null;
            port = 0;
            nonce = null;
            if (packet.Length > 512)
                return false;
            string[] p = Encoding.ASCII.GetString(packet).Split('|');
            long time;
            Guid id, ng;
            if (p.Length != 6 || p[0] != "VFB2" || !Guid.TryParseExact(p[1], "N", out id) ||
                !int.TryParse(p[2], out port) || port != 48751 || !long.TryParse(p[3], out time) || time < now - 60 ||
                time > now + 60 || !Guid.TryParseExact(p[4], "N", out ng))
                return false;
            string body = string.Join("|", p.Take(5));
            byte[] expected;
            using (var h = new HMACSHA256(key)) expected =
                Encoding.ASCII.GetBytes(Identity.Hex(h.ComputeHash(Encoding.ASCII.GetBytes(body))));
            if (!Identity.Equal(expected, Encoding.ASCII.GetBytes(p[5])))
                return false;
            node = p[1];
            nonce = p[4];
            return true;
        }
        public static long Seconds
        {
            get {
                return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
            }
        }
    }
}
