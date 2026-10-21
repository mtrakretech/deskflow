// SPDX-FileCopyrightText: (C) 2026 Viola File Bridge contributors
// SPDX-License-Identifier: MIT
using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Windows.Forms;
using System.IO.Compression;
using System.Threading;
namespace Viola
{
    public static class HistoryTests
    {
        public static void LiveClipboardPolicy(string root)
        {
            var original = ClipSnapshot.Capture(Clipboard.GetDataObject());
            if (original == null && Clipboard.GetDataObject() != null &&
                Clipboard.GetDataObject().GetFormats().Length > 0)
                throw new InvalidOperationException("Use an empty or standard clipboard for this manual test.");
            string home = Path.Combine(root, "live-clipboard-policy");
            Directory.CreateDirectory(home);
            File.WriteAllText(Path.Combine(home, "welcome.seen"), "test");
            string cache = Path.Combine(home, "Cache", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(cache);
            string file = Path.Combine(cache, "verified-receive.txt");
            File.WriteAllText(file, "Verified staged content");
            bool offline = Program.Offline;
            string oldLog = Log.Root;
            Program.Offline = true;
            TrayApp app = null;
            try
            {
                app = new TrayApp(home);
                var publish = typeof(TrayApp).GetMethod("Publish", System.Reflection.BindingFlags.Instance |
                                                                       System.Reflection.BindingFlags.NonPublic);
                uint beginning = ClipboardWindow.GetClipboardSequenceNumber();
                string newer = "Newer clipboard text must survive " + Guid.NewGuid().ToString("N");
                Clipboard.SetText(newer);
                var task = System.Threading.Tasks.Task.Run(
                    () => (bool)publish.Invoke(app, new object[] { new[] { file }, beginning }));
                Pump(task);
                Check(!task.Result && Clipboard.GetText() == newer, "Incoming files replaced newer text");
                Check(File.Exists(file), "Verified staged file disappeared");
                uint current = ClipboardWindow.GetClipboardSequenceNumber();
                task = System.Threading.Tasks.Task.Run(
                    () => (bool)publish.Invoke(app, new object[] { new[] { file }, current }));
                Pump(task);
                Check(task.Result && Clipboard.ContainsFileDropList() && Clipboard.GetFileDropList()[0] == file,
                      "Explicit restore did not make received files available");
            }
            finally
            {
                if (app != null)
                {
                    app.ExitThread();
                    app.Dispose();
                }
                Program.Offline = offline;
                Log.Root = oldLog;
                if (original != null)
                {
                    using (original) Clipboard.SetDataObject(original.DataObject(), true, 12, 80);
                }
                else
                    Clipboard.Clear();
            }
        }
        static void Pump(System.Threading.Tasks.Task task)
        {
            DateTime until = DateTime.UtcNow.AddSeconds(8);
            while (!task.IsCompleted && DateTime.UtcNow < until)
            {
                Application.DoEvents();
                Thread.Sleep(10);
            }
            if (!task.IsCompleted)
                throw new TimeoutException("Clipboard publication did not return");
            task.GetAwaiter().GetResult();
        }
        static void Check(bool value, string message)
        {
            if (!value)
                throw new Exception(message);
        }
        static void Reject(Action action)
        {
            try
            {
                action();
            }
            catch
            {
                return;
            }
            throw new Exception("Expected rejection");
        }
        public static void Live(string root)
        {
            string home = Path.Combine(root, "live-history");
            Directory.CreateDirectory(home);
            File.WriteAllText(Path.Combine(home, "welcome.seen"), "test");
            string exe = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location),
                                      "ViolaFileBridge.exe");
            var original = Clipboard.GetDataObject();
            using (var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                       exe, "--data-root \"" + home + "\" --offline --background") { UseShellExecute = true }))
            {
                try
                {
                    Thread.Sleep(2800);
                    Check(!process.HasExited, "History app failed to start");
                    string text = "Live-history-probe-مرحبا-" + Guid.NewGuid().ToString("N");
                    Clipboard.SetText(text);
                    WaitFor(home, store => store.List("").Any(i => i.Preview.Contains(text)));
                    using (var image = new Bitmap(80, 60))
                    {
                        using (var graphics = Graphics.FromImage(image)) graphics.Clear(Color.LimeGreen);
                        Clipboard.SetImage(image);
                        WaitFor(home, store => store.List("").Any(i => i.Kind == "Screenshot / image"));
                    }
                    var result = new HistoryStore(home);
                    result.Load();
                    Check(result.List("").Length == 2, "Expected exactly text and screenshot");
                    Check(Commands.Send(home, "--history"), "History command failed");
                    Thread.Sleep(400);
                    Check(!process.HasExited, "History window crashed");
                    Check(Commands.Send(home, "--exit"), "Exit command failed");
                    Check(process.WaitForExit(5000), "History app did not exit cleanly");
                }
                finally
                {
                    if (!process.HasExited)
                    {
                        process.Kill();
                        process.WaitForExit();
                    }
                    if (original != null)
                        Clipboard.SetDataObject(original, true, 12, 80);
                }
            }
        }
        static void WaitFor(string home, Func<HistoryStore, bool> predicate)
        {
            var until = DateTime.UtcNow.AddSeconds(8);
            while (DateTime.UtcNow < until)
            {
                Thread.Sleep(200);
                Application.DoEvents();
                var store = new HistoryStore(home);
                store.Load();
                if (predicate(store))
                    return;
            }
            throw new Exception("History event not persisted");
        }
        public static void Run(Action<string, Action> test, string root)
        {
            test("Qt helper exchanges framed JSON over redirected standard streams",
                 () =>
                 {
                     string home = Path.Combine(root, "qt-stdio");
                     var serverInfo = new System.Diagnostics.ProcessStartInfo(
                         Application.ExecutablePath, "--ui-fixture \"" + home + "\"") { UseShellExecute = false,
                                                                                        CreateNoWindow = true };
                     using (var server = System.Diagnostics.Process.Start(serverInfo))
                     {
                         try
                         {
                             var info = new System.Diagnostics.ProcessStartInfo(
                                 Path.Combine(Application.StartupPath, "ViolaFileBridge.exe"),
                                 "--ui --data-root \"" + home + "\"") { UseShellExecute = false, CreateNoWindow = true,
                                                                        RedirectStandardInput = true,
                                                                        RedirectStandardOutput = true,
                                                                        RedirectStandardError = true };
                             using (var helper = System.Diagnostics.Process.Start(info))
                             {
                                 try
                                 {
                                     Commands.WriteMessage(helper.StandardInput.BaseStream,
                                                           "{\"op\":\"list\",\"search\":\"Welcome\"}");
                                     helper.StandardInput.Close();
                                     string response =
                                         Commands.ReadMessage(helper.StandardOutput.BaseStream, 32 * 1024 * 1024);
                                     Check(helper.WaitForExit(5000) && helper.ExitCode == 0,
                                           "Helper did not exit cleanly");
                                     Check(response.Contains("\"ok\":true") &&
                                               response.Contains("Welcome to your clipboard"),
                                           "Invalid helper response: " + response);
                                 }
                                 finally
                                 {
                                     if (!helper.HasExited)
                                         helper.Kill();
                                 }
                             }
                         }
                         finally
                         {
                             Commands.Send(home, "--exit");
                             if (!server.WaitForExit(5000))
                             {
                                 server.Kill();
                                 server.WaitForExit(5000);
                             }
                         }
                     }
                 });
            test("Qt UI channel frames Unicode responses and rejects invalid lengths",
                 () =>
                 {
                     using (var memory = new MemoryStream())
                     {
                         Commands.WriteMessage(memory, "{\"text\":\"Viola — مرحبا\"}");
                         memory.Position = 0;
                         Check(Commands.ReadMessage(memory, 1024).Contains("مرحبا"), "UTF-8 response damaged");
                     }
                     using (var memory = new MemoryStream(BitConverter.GetBytes(-1)))
                         Reject(() => Commands.ReadMessage(memory, 1024));
                     using (var memory = new MemoryStream(BitConverter.GetBytes(1025)))
                         Reject(() => Commands.ReadMessage(memory, 1024));
                 });
            test("Qt UI actions use the running encrypted store and validate pairing confirmation",
                 () =>
                 {
                     string home = Path.Combine(root, "qt-ui-api");
                     Directory.CreateDirectory(home);
                     File.WriteAllText(Path.Combine(home, "welcome.seen"), "test");
                     var seed = new HistoryStore(home);
                     using (var clip = new ClipSnapshot { Text = "Native Qt clipboard preview مرحبا" }) seed.Add(clip);
                     bool offline = Program.Offline;
                     string oldLog = Log.Root;
                     Program.Offline = true;
                     TrayApp app = null;
                     try
                     {
                         app = new TrayApp(home);
                         Func<object, System.Collections.Generic.Dictionary<string, object>> query = request =>
                         {
                             var task = System.Threading.Tasks.Task.Run(
                                 () => Commands.Query(home, UiJson.Serializer().Serialize(request)));
                             Pump(task);
                             return UiJson.Serializer()
                                 .Deserialize<System.Collections.Generic.Dictionary<string, object>>(task.Result);
                         };
                         var listing = query(new { op = "list", search = "Native Qt" });
                         Check((bool)listing["ok"], "List failed");
                         var entries = (System.Collections.ArrayList)listing["items"];
                         Check(entries.Count == 1, "Wrong history list");
                         string id = (string)((System.Collections.Generic.Dictionary<string, object>)entries[0])["id"];
                         var preview = query(new { op = "preview", id = id });
                         Check(((string)preview["text"]).Contains("مرحبا"), "Preview damaged");
                         Check(!(bool)query(new { op = "preview", id = "../escape" })["ok"], "Unsafe ID accepted");
                         Check(!(bool)query(new { op = "shell", command = "anything" })["ok"],
                               "Unknown operation accepted");
                         Check((bool)query(new { op = "pin", id = id })["ok"], "Pin failed");
                         Check((bool)query(new { op = "pause" })["ok"] &&
                                   File.Exists(Path.Combine(home, "history.paused")),
                               "Pause not persisted");
                         Check((bool)query(new { op = "pause" })["ok"] &&
                                   !File.Exists(Path.Combine(home, "history.paused")),
                               "Resume not persisted");
                         Check(!(bool)query(new { op = "pair-export", password = "short" })["ok"],
                               "Weak password accepted");
                         string password = "Isolated Qt API test password";
                         var exported = query(new { op = "pair-export", password = password });
                         string data = (string)exported["data"];
                         var inspected = query(new { op = "pair-inspect", data = data, password = password });
                         Check((bool)inspected["ok"] && ((string)inspected["fingerprint"]).Length == 64,
                               "Inspect failed");
                         Check(!(bool)query(new { op = "pair-import", data = data, password = password,
                                                  confirmedFingerprint = "wrong" })["ok"],
                               "Unconfirmed pairing accepted");
                         Check((bool)query(new { op = "pair-import", data = data, password = password,
                                                 confirmedFingerprint = inspected["fingerprint"] })["ok"],
                               "Confirmed pairing failed");
                         Check((bool)query(new { op = "delete", id = id })["ok"], "Delete failed");
                         Check((bool)query(new { op = "clear" })["ok"], "Clear failed");
                     }
                     finally
                     {
                         if (app != null)
                         {
                             app.ExitThread();
                             app.Dispose();
                         }
                         Program.Offline = offline;
                         Log.Root = oldLog;
                     }
                 });
            test("Incoming files preserve newer text, image and file selections",
                 () =>
                 {
                     foreach (string format in new[] { DataFormats.UnicodeText, DataFormats.Html, DataFormats.Rtf,
                                                       DataFormats.Bitmap, DataFormats.FileDrop, "PNG" })
                     {
                         var data = new DataObject();
                         data.SetData(format, "newer selection");
                         Check(!ClipboardPolicy.ShouldPublish(10, 11, data),
                               "Newer clipboard format overwritten: " + format);
                         Check(ClipboardPolicy.ShouldPublish(10, 10, data), "Unchanged clipboard wrongly deferred");
                     }
                     var empty = new DataObject();
                     empty.SetData("Deskflow Ownership", new MemoryStream(new byte[1]));
                     Check(ClipboardPolicy.ShouldPublish(10, 11, empty),
                           "Deskflow's empty ownership marker blocked file publication");
                 });
            test("History preserves Unicode text, HTML and RTF; encrypted on disk",
                 () =>
                 {
                     var store = new HistoryStore(Path.Combine(root, "history-text"));
                     string secret = "Private-history-مرحبا-" + Guid.NewGuid().ToString("N");
                     var d = new DataObject();
                     d.SetData(DataFormats.UnicodeText, secret);
                     d.SetData(DataFormats.Html, "<b>" + secret + "</b>");
                     d.SetData(DataFormats.Rtf, @"{\rtf1\ansi hello}");
                     using (var clip = ClipSnapshot.Capture(d))
                     {
                         var item = store.Add(clip);
                         using (var saved = store.Get(item.Id))
                         {
                             Check(saved.Text == secret && saved.Html.Contains(secret) && saved.Rtf.Contains("rtf1"),
                                   "Lost text formats");
                             var restored = saved.DataObject();
                             Check((string)restored.GetData(DataFormats.UnicodeText) == secret, "Restore text differs");
                         }
                         byte[] disk = File.ReadAllBytes(
                             Directory.GetFiles(Path.Combine(root, "history-text", "History"), "*.vfh").Single());
                         Check(!System.Text.Encoding.UTF8.GetString(disk).Contains(secret),
                               "Plaintext history on disk");
                     }
                     var reload = new HistoryStore(Path.Combine(root, "history-text"));
                     reload.Load();
                     Check(reload.List("").Length == 1 && reload.List("مرحبا").Length == 1, "Reload/search failed");
                 });
            test("Screenshot history encodes PNG and restores bitmap",
                 () =>
                 {
                     var store = new HistoryStore(Path.Combine(root, "history-image"));
                     using (var image = new Bitmap(48, 32))
                     {
                         using (var g = Graphics.FromImage(image)) g.Clear(Color.Coral);
                         var d = new DataObject();
                         d.SetImage(image);
                         using (var clip = ClipSnapshot.Capture(d))
                         {
                             var item = store.Add(clip);
                             Check(item.Kind == "Screenshot / image", "Wrong kind");
                             using (var saved = store.Get(item.Id))
                             {
                                 Check(saved.Png.Length > 8 && saved.Png[0] == 137 && saved.Png[1] == 80, "Not PNG");
                                 using (var restored = (Bitmap)saved.DataObject().GetData(DataFormats.Bitmap))
                                     Check(restored.Width == 48 && restored.Height == 32 &&
                                               restored.GetPixel(10, 10).ToArgb() == Color.Coral.ToArgb(),
                                           "Pixels differ");
                             }
                         }
                     }
                 });
            test("History respects clipboard exclusion and size limits",
                 () =>
                 {
                     var d = new DataObject();
                     d.SetText("do not save");
                     d.SetData("ExcludeClipboardContentFromMonitorProcessing", new MemoryStream(new byte[4]));
                     Check(ClipSnapshot.Capture(d) == null, "Excluded data captured");
                     d = new DataObject();
                     d.SetText("private");
                     d.SetData("CanIncludeInClipboardHistory", new MemoryStream(new byte[4]));
                     Check(ClipSnapshot.Capture(d) == null, "History opt-out ignored");
                     d = new DataObject();
                     d.SetText(new string('x', 1024 * 1024 + 1));
                     Check(ClipSnapshot.Capture(d) == null, "Oversized text captured");
                     Reject(() => ClipSnapshot.Decode(new byte[4]));
                 });
            test("PNG-only and HTML-only clipboard formats are captured",
                 () =>
                 {
                     using (var image = new Bitmap(12, 10)) using (var memory = new MemoryStream())
                     {
                         image.Save(memory, System.Drawing.Imaging.ImageFormat.Png);
                         var data = new DataObject();
                         data.SetData("PNG", new MemoryStream(memory.ToArray()));
                         using (var clip = ClipSnapshot.Capture(data))
                         {
                             Check(clip != null && clip.Png != null, "PNG-only image missing");
                         }
                     }
                     var html = new DataObject();
                     html.SetData(DataFormats.Html, "<p>rich only</p>");
                     using (var clip = ClipSnapshot.Capture(html))
                     {
                         Check(clip != null && clip.Html.Contains("rich only"), "HTML-only item missing");
                     }
                 });
            test("History deduplicates, pins, bounds retention and clears",
                 () =>
                 {
                     string home = Path.Combine(root, "history-bounds");
                     var store = new HistoryStore(home) { MaxItems = 2 };
                     HistoryItem first;
                     using (var clip = new ClipSnapshot { Text = "one" }) first = store.Add(clip);
                     store.Pin(first.Id);
                     using (var clip = new ClipSnapshot { Text = "one" }) store.Add(clip);
                     Check(store.List("").Length == 1, "Duplicate retained");
                     using (var clip = new ClipSnapshot { Text = "two" }) store.Add(clip);
                     using (var clip = new ClipSnapshot { Text = "three" }) store.Add(clip);
                     Check(store.List("").Length == 2 && store.List("").Any(i => i.Id == first.Id && i.Pinned),
                           "Pin/limit failed");
                     var reload = new HistoryStore(home);
                     reload.Load();
                     Check(reload.List("").Single(i => i.Id == first.Id).Pinned, "Pin lost");
                     reload.Delete(first.Id);
                     Check(reload.List("").Length == 1, "Delete failed");
                     reload.Clear();
                     Check(reload.List("").Length == 0, "Clear failed");
                 });
            test("History file references use COPY and reject stale paths",
                 () =>
                 {
                     string path = Path.Combine(root, "history-file.txt");
                     File.WriteAllText(path, "keep source");
                     using (var clip = new ClipSnapshot { Files = new[] { path } })
                     {
                         byte[] bytes = clip.Encode();
                         using (var saved = ClipSnapshot.Decode(bytes))
                         {
                             var data = saved.DataObject();
                             Check(((string[])data.GetData(DataFormats.FileDrop))[0] == path, "File path changed");
                             var effect = (MemoryStream)data.GetData("Preferred DropEffect");
                             Check(BitConverter.ToInt32(effect.ToArray(), 0) == 1, "Not COPY");
                             File.Delete(path);
                             Reject(() => saved.DataObject());
                         }
                     }
                 });
            test("Corrupted encrypted history is rejected without crashing load",
                 () =>
                 {
                     string home = Path.Combine(root, "history-corrupt");
                     var store = new HistoryStore(home);
                     using (var clip = new ClipSnapshot { Text = "abc" }) store.Add(clip);
                     string file = Directory.GetFiles(Path.Combine(home, "History"), "*.vfh").Single();
                     byte[] bytes = File.ReadAllBytes(file);
                     bytes[bytes.Length / 2] ^= 1;
                     File.WriteAllBytes(file, bytes);
                     store.Load();
                     Check(store.List("").Length == 0, "Corrupt entry accepted");
                 });
            test("Clear all waits for queued history captures",
                 () =>
                 {
                     var service = new HistoryService(Path.Combine(root, "history-clear-queue"));
                     var data = new DataObject();
                     data.SetText("queued before clear");
                     service.Capture(data);
                     service.Clear().GetAwaiter().GetResult();
                     Check(service.Store.List("").Length == 0, "A pending capture reappeared after clear");
                 });
            test("Local command channel delivers history command",
                 () =>
                 {
                     string key = Commands.Name(Path.Combine(root, "ipc")), received = null;
                     using (var signal = new ManualResetEvent(false)) using (var commands =
                                                                                 new Commands(key, cmd =>
                                                                                                   {
                                                                                                       received = cmd;
                                                                                                       signal.Set();
                                                                                                   }))
                     {
                         Check(Commands.Send(Path.Combine(root, "ipc"), "--history"), "Cannot send command");
                         Check(signal.WaitOne(3000) && received == "--history", "Command not delivered");
                     }
                 });
            test("Installer archive rejects traversal before writing",
                 () =>
                 {
                     using (var memory = new MemoryStream())
                     {
                         using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
                         {
                             var entry = zip.CreateEntry("../escape.txt");
                             using (var w = new StreamWriter(entry.Open())) w.Write("bad");
                         }
                         memory.Position = 0;
                         using (var zip = new ZipArchive(memory, ZipArchiveMode.Read, true))
                             Reject(() => ViolaSetup.Payload.Extract(zip, Path.Combine(root, "payload-reject")));
                     }
                     Check(!File.Exists(Path.Combine(root, "escape.txt")), "Traversal wrote outside extraction");
                 });
            test("Installer archive extracts a single CPack root safely",
                 () =>
                 {
                     using (var memory = new MemoryStream())
                     {
                         using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
                         {
                             foreach (string name in new[] { "deskflow-test/deskflow.exe",
                                                             "deskflow-test/plugins/platforms/qwindows.dll" })
                             {
                                 var entry = zip.CreateEntry(name);
                                 using (var w = new StreamWriter(entry.Open())) w.Write("test fixture");
                             }
                         }
                         memory.Position = 0;
                         using (var zip = new ZipArchive(memory, ZipArchiveMode.Read, true))
                             ViolaSetup.Payload.Extract(zip, Path.Combine(root, "payload-ok"));
                     }
                     Check(File.Exists(Path.Combine(root, "payload-ok", "deskflow.exe")) &&
                               File.Exists(Path.Combine(root, "payload-ok", "plugins", "platforms", "qwindows.dll")),
                           "Payload layout wrong");
                 });
        }
    }
}
