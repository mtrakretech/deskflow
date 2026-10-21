using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Drawing;
using System.Windows.Forms;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Threading;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using System.Reflection;

[assembly:AssemblyTitle("Viola File Bridge")]
[assembly:AssemblyVersion("0.3.0.0")]
[assembly:AssemblyFileVersion("0.3.0.0")]
[assembly:AssemblyCopyright("Copyright 2026 Viola File Bridge contributors")]
namespace Viola
{
    public sealed class ClipboardWindow : Form
    {
        [DllImport("user32.dll", SetLastError = true)]
        static extern bool AddClipboardFormatListener(IntPtr hwnd);
        [DllImport("user32.dll")]
        static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
        [DllImport("user32.dll")]
        public static extern uint GetClipboardSequenceNumber();
        public event Action Changed;
        public ClipboardWindow()
        {
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.None;
            Size = new Size(1, 1);
            Location = new Point(-32000, -32000);
        }
        protected override void SetVisibleCore(bool value)
        {
            base.SetVisibleCore(false);
        }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (!AddClipboardFormatListener(Handle))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x031D && Changed != null)
                Changed();
            base.WndProc(ref m);
        }
        protected override void OnHandleDestroyed(EventArgs e)
        {
            RemoveClipboardFormatListener(Handle);
            base.OnHandleDestroyed(e);
        }
    }
    public sealed class TrayApp : ApplicationContext
    {
        readonly ClipboardWindow window = new ClipboardWindow();
        readonly NotifyIcon tray = new NotifyIcon();
        readonly System.Windows.Forms.Timer debounce = new System.Windows.Forms.Timer(),
                                            housekeeping = new System.Windows.Forms.Timer();
        readonly ToolStripMenuItem stateItem = new ToolStripMenuItem(), peerItem = new ToolStripMenuItem();
        Identity identity;
        Bridge bridge;
        string node;
        readonly string root;
        string[] received = new string[0];
        uint ownSequence;
        int clipboardAttempts;
        string status = "Starting";
        Form progressForm, settingsForm;
        Label progressLabel;
        ProgressBar progressBar;
        DateTime lastProgress = DateTime.MinValue;
        bool exiting;
        readonly HistoryService history;
        readonly Commands commands;
        public TrayApp(string path)
        {
            root = path;
            Log.Root = root;
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(Path.Combine(root, "Cache"));
            identity = Identity.LoadOrCreate(root);
            string nodePath = Path.Combine(root, "node.txt");
            node = File.Exists(nodePath) ? File.ReadAllText(nodePath).Trim() : Guid.NewGuid().ToString("N");
            Guid.ParseExact(node, "N");
            File.WriteAllText(nodePath, node);
            history = new HistoryService(root);
            history.Paused = File.Exists(Path.Combine(root, "history.paused"));
            var handle = window.Handle;
            window.Changed += ClipboardChanged;
            commands = new Commands(Commands.Name(root), command => UI(() => Command(command)));
            debounce.Interval = 160;
            debounce.Tick += (s, e) => ReadClipboard();
            var menu = new ContextMenuStrip();
            stateItem.Enabled = false;
            peerItem.Enabled = false;
            menu.Items.Add(stateItem);
            menu.Items.Add(peerItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Clipboard history — text and screenshots", null, (s, e) => ShowHistory());
            var pause =
                new ToolStripMenuItem("Pause clipboard history") { CheckOnClick = true, Checked = history.Paused };
            pause.CheckedChanged += (s, e) =>
            {
                history.Paused = pause.Checked;
                string p = Path.Combine(root, "history.paused");
                if (history.Paused)
                    File.WriteAllText(p, "");
                else
                    File.Delete(p);
            };
            menu.Items.Add(pause);
            menu.Items.Add("Add another PC…", null, (s, e) => AddComputer());
            menu.Items.Add("Open received cache", null, (s, e) => Open(Path.Combine(root, "Cache")));
            menu.Items.Add("Open Logs", null, (s, e) => Open(Path.Combine(root, "Logs")));
            menu.Items.Add("Settings and pairing", null, (s, e) => Settings());
            menu.Items.Add("Network settings", null, (s, e) => Process.Start("ms-settings:network-status"));
            menu.Items.Add("Restart", null, (s, e) => Restart());
            menu.Items.Add("Exit", null, (s, e) => ExitThread());
            tray.Icon = SystemIcons.Application;
            tray.ContextMenuStrip = menu;
            tray.Text = "Viola File Bridge";
            tray.Visible = true;
            tray.DoubleClick += (s, e) => Settings();
            housekeeping.Interval = 60000;
            int minutes = 0;
            housekeeping.Tick += (s, e) =>
            {
                peerItem.Text = "Peer: " + bridge.Peer;
                if (++minutes % 60 == 0)
                {
                    Cleanup();
                    System.Threading.Tasks.Task.Run(() =>
                                                    {
                                                        try
                                                        {
                                                            history.Store.Cleanup();
                                                        }
                                                        catch (Exception ex)
                                                        {
                                                            Log.Write("History retention: " + ex.GetType().Name);
                                                        }
                                                    });
                }
            };
            housekeeping.Start();
            StartBridge();
            Cleanup();
            if (!File.Exists(Path.Combine(root, "welcome.seen")))
            {
                File.WriteAllText(Path.Combine(root, "welcome.seen"), "0.3.0");
                window.BeginInvoke((Action)(() => Settings()));
            }
            SystemEvents.PowerModeChanged += PowerChanged;
            SetState("Searching for paired PC");
        }
        public void Command(string command)
        {
            if (command == "--history")
                ShowHistory();
            else if (command == "--exit")
                ExitThread();
            else if (command == "--settings")
                Settings();
        }
        void ShowHistory()
        {
            history.Show(data =>
                         {
                             Clipboard.SetDataObject(
                                 data, true, 12,
                                 80); /* Clipboard event deliberately allows restored files to be sent again. */
                         });
        }
        void AddComputer()
        {
            try
            {
                string setup =
                    Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "ViolaFileBridge-Setup.exe");
                if (!File.Exists(setup))
                    throw new IOException(
                        "Install the full setup package first so an installer can be included for the new PC.");
                using (var dialog = new SaveFileDialog { Filter = "Private PC setup bundle (*.zip)|*.zip",
                                                         FileName = "Viola-Add-PC.zip" })
                {
                    if (dialog.ShowDialog() != DialogResult.OK)
                        return;
                    string password = Password("Add another PC", true);
                    if (password == null)
                        return;
                    if (password.Length < 12)
                        throw new IOException("Choose a password with at least 12 characters.");
                    using (var file = new FileStream(dialog.FileName, FileMode.Create, FileAccess.Write)) using (
                        var zip =
                            new System.IO.Compression.ZipArchive(file, System.IO.Compression.ZipArchiveMode.Create))
                    {
                        System.IO.Compression.ZipFileExtensions.CreateEntryFromFile(zip, setup,
                                                                                    "ViolaFileBridge-Setup.exe");
                        var entry = zip.CreateEntry("Pairing.vfbpair");
                        byte[] pfx = identity.Certificate.Export(
                            System.Security.Cryptography.X509Certificates.X509ContentType.Pfx, password);
                        using (var stream = entry.Open()) stream.Write(pfx, 0, pfx.Length);
                        Array.Clear(pfx, 0, pfx.Length);
                        var note = zip.CreateEntry("START-HERE.txt");
                        using (var writer = new StreamWriter(note.Open()))
                            writer.Write("PRIVATE PAIRING BUNDLE — do not publish.\r\nExtract this ZIP on the other " +
                                         "PC and run ViolaFileBridge-Setup.exe.\r\nEnter the password you chose on " +
                                         "the first PC. Compare fingerprint:\r\n" +
                                         identity.Pin +
                                         ("\r\nDelete this bundle after installation. Existing pairing must be " +
                                          "replaced explicitly.\r\nFor mouse/keyboard configure the normal Deskflow " +
                                          "host and screen layout once.\r\n"));
                    }
                    MessageBox.Show(
                        "Private setup bundle created. Extract it on the other PC and run its installer. Enter your " +
                        "password there once. Share the password separately and delete the bundle afterward.",
                        "Add another PC");
                }
            }
            catch (Exception ex)
            {
                ShowError(ex);
            }
        }
        void StartBridge()
        {
            bridge = new Bridge(identity, root, node, SetState, Publish, UpdateProgress);
            if (!Program.Offline)
                bridge.Start();
        }
        void PowerChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume)
                Log.Write("Windows resumed; discovery refresh scheduled");
        }
        void UI(Action action)
        {
            if (!exiting && !window.IsDisposed)
                try
                {
                    window.BeginInvoke((Action)(() =>
                                                {
                                                    if (!exiting)
                                                        action();
                                                }));
                }
                catch (InvalidOperationException)
                {
                }
        }
        void SetState(string text)
        {
            UI(() =>
               {
                   if (text == "Connected" && (status == "Ready to paste" || bridge.Busy))
                       return;
                   status = text;
                   stateItem.Text = "Status: " + text;
                   peerItem.Text = "Peer: " + bridge.Peer;
                   tray.Text = ("Viola: " + text).Substring(0, Math.Min(63, ("Viola: " + text).Length));
                   tray.Icon = text.StartsWith("Error")   ? SystemIcons.Error
                               : text == "Ready to paste" ? SystemIcons.Information
                                                          : SystemIcons.Application;
                   if (text == "Ready to paste")
                   {
                       HideProgress();
                       Notify("Ready to paste",
                              "Verified files are ready. In Explorer press Ctrl+V. Cross-PC cut is treated as copy.",
                              false);
                   }
                   else if (text.StartsWith("Error"))
                   {
                       HideProgress();
                       Notify("Viola File Bridge", text, true);
                   }
                   else if (text.Contains("delivered"))
                   {
                       HideProgress();
                       Notify("Transfer delivered", "The other PC has verified the files and made them ready to paste.",
                              false);
                   }
               });
        }
        void ClipboardChanged()
        {
            debounce.Stop();
            clipboardAttempts = 0;
            debounce.Start();
        }
        void ReadClipboard()
        {
            debounce.Stop();
            if (ClipboardWindow.GetClipboardSequenceNumber() == ownSequence)
                return;
            try
            {
                var data = Clipboard.GetDataObject();
                history.Capture(data);
                if (data == null || !data.GetDataPresent(DataFormats.FileDrop))
                {
                    bridge.CancelPending();
                    return;
                }
                string[] paths = (string[])data.GetData(DataFormats.FileDrop);
                if (paths == null || paths.Length == 0)
                    return;
                if (paths.All(p => SafePath.IsInside(Path.Combine(root, "Cache"), p)))
                    return;
                if (paths.Any(p => p.StartsWith("\\\\", StringComparison.Ordinal) || SafePath.IsInside(root, p)))
                    throw new IOException("Select local user files outside Viola's own data directory.");
                // Never write the source clipboard, delete originals, or report a performed move.
                bridge.Queue(paths);
                Log.Write("Clipboard file selection queued (" + paths.Length + " roots)");
            }
            catch (ExternalException ex)
            {
                if (++clipboardAttempts < 12)
                {
                    debounce.Interval = 100;
                    debounce.Start();
                }
                else
                {
                    Log.Write("Clipboard read retries exhausted: " + ex);
                    SetState("Error: clipboard busy; copy again");
                }
            }
            catch (Exception ex)
            {
                Log.Write("Clipboard selection failure: " + ex);
                SetState("Error: clipboard selection; see logs");
            }
        }
        void Publish(string[] paths)
        {
            Exception error = null;
            using (var done = new ManualResetEvent(false))
            {
                UI(() =>
                   {
                       try
                       {
                           // Retry OLE clipboard locking without doing any disk/network work on the STA.
                           var data = new DataObject();
                           var list = new StringCollection();
                           list.AddRange(paths);
                           data.SetFileDropList(list);
                           data.SetData("Preferred DropEffect",
                                        new MemoryStream(BitConverter.GetBytes(1))); // DROPEFFECT_COPY
                           Clipboard.SetDataObject(data, true, 12, 80);
                           ownSequence = ClipboardWindow.GetClipboardSequenceNumber();
                           received = paths;
                           history.Capture(data);
                       }
                       catch (Exception ex)
                       {
                           error = ex;
                           Log.Write("Clipboard publish failure: " + ex);
                       }
                       finally
                       {
                           done.Set();
                       }
                   });
                // Closing the application terminates outstanding connections. Avoid a stale publish after shutdown.
                while (!done.WaitOne(500))
                {
                    if (exiting)
                        throw new OperationCanceledException("Application exiting.");
                }
            }
            if (error != null)
                throw new IOException("Verified files remain in cache, but Windows clipboard is busy.", error);
        }
        void Cleanup()
        {
            string[] current = null;
            try
            {
                if (Clipboard.ContainsFileDropList())
                    current = Clipboard.GetFileDropList().Cast<string>().ToArray();
            }
            catch
            {
                return;
            }
            string[] protectedPaths = (current ?? new string[0]).Concat(received).ToArray();
            System.Threading.Tasks.Task.Run(
                () =>
                {
                    if (bridge != null && !bridge.Busy)
                        Cache.Cleanup(Path.Combine(root, "Cache"), protectedPaths, DateTime.UtcNow);
                    try
                    {
                        foreach (string log in Directory.EnumerateFiles(Path.Combine(root, "Logs"), "*.log"))
                            if (File.GetLastWriteTimeUtc(log) < DateTime.UtcNow.AddDays(-14))
                                File.Delete(log);
                    }
                    catch (Exception ex)
                    {
                        Log.Write("Log cleanup: " + ex.Message);
                    }
                });
        }
        public static string Bytes(double b)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            int i = 0;
            while (b >= 1024 && i < units.Length - 1)
            {
                b /= 1024;
                i++;
            }
            return b.ToString(i == 0 ? "0" : "0.0") + " " + units[i];
        }
        void UpdateProgress(Progress p)
        {
            if (p.Total < 8 * 1024 * 1024 || p.Seconds < 0.6)
                return;
            lock (this)
            {
                if ((DateTime.UtcNow - lastProgress).TotalMilliseconds < 250)
                    return;
                lastProgress = DateTime.UtcNow;
            }
            UI(() =>
               {
                   if (progressForm == null)
                   {
                       progressForm =
                           new Form { Text = "Viola File Bridge", ClientSize = new Size(460, 130),
                                      FormBorderStyle = FormBorderStyle.FixedToolWindow, ShowInTaskbar = false,
                                      StartPosition = FormStartPosition.CenterScreen };
                       progressLabel = new Label { Left = 14, Top = 12, Width = 430, Height = 65 };
                       progressBar = new ProgressBar { Left = 14, Top = 86, Width = 430, Height = 22 };
                       progressForm.Controls.Add(progressLabel);
                       progressForm.Controls.Add(progressBar);
                       progressForm.FormClosing += (s, e) =>
                       {
                           if (!exiting)
                           {
                               e.Cancel = true;
                               progressForm.Hide();
                           }
                       };
                   }
                   double speed = p.Done / Math.Max(.01, p.Seconds), eta = (p.Total - p.Done) / Math.Max(1, speed);
                   int percent = (int)Math.Min(100, p.Done * 100.0 / Math.Max(1, p.Total));
                   progressLabel.Text = p.State + ": " + p.Name + "\r\n" + Bytes(p.Done) + " / " + Bytes(p.Total) +
                                        " (" + percent + "%)\r\n" + Bytes(speed) + "/s | ETA " +
                                        TimeSpan.FromSeconds(Math.Min(eta, 864000)).ToString(@"hh\:mm\:ss");
                   progressBar.Value = percent;
                   if (!progressForm.Visible)
                       progressForm.Show();
               });
        }
        void HideProgress()
        {
            if (progressForm != null)
                progressForm.Hide();
        }
        void Notify(string title, string message, bool error)
        {
            tray.ShowBalloonTip(4000, title, message, error ? ToolTipIcon.Error : ToolTipIcon.Info);
        }
        void Open(string path)
        {
            Directory.CreateDirectory(path);
            Process.Start("explorer.exe", "\"" + path + "\"");
        }
        public static bool StartupEnabled()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(
                       @"Software\Microsoft\Windows\CurrentVersion\Run")) return k != null &&
                k.GetValue("ViolaFileBridge") != null;
        }
        public static void SetStartup(bool enabled)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
            {
                if (enabled)
                    k.SetValue("ViolaFileBridge", "\"" + Application.ExecutablePath + "\"");
                else
                    k.DeleteValue("ViolaFileBridge", false);
            }
        }
        string Password(string title, bool exporting)
        {
            return Prompt(title,
                          exporting
                              ? "Choose a strong password (12+ characters). Share it separately from the pairing file."
                              : "Password for the pairing file:",
                          true);
        }
        public static string Prompt(string title, string label, bool secret)
        {
            using (var f = new Form { Text = title, ClientSize = new Size(470, 140),
                                      StartPosition = FormStartPosition.CenterScreen,
                                      FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false,
                                      MinimizeBox = false })
            {
                var l = new Label { Text = label, Left = 12, Top = 10, Width = 446, Height = 42 };
                var t = new TextBox { Left = 12, Top = 56, Width = 446, UseSystemPasswordChar = secret };
                var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 290, Top = 99 };
                var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 375, Top = 99 };
                f.Controls.AddRange(new Control[] { l, t, ok, cancel });
                f.AcceptButton = ok;
                f.CancelButton = cancel;
                return f.ShowDialog() == DialogResult.OK ? t.Text : null;
            }
        }
        void Settings()
        {
            if (settingsForm != null)
            {
                settingsForm.Activate();
                return;
            }
            using (var f = new Form { Text = "Deskflow + Viola 0.3.0 — Clipboard and pairing",
                                      ClientSize = new Size(560, 470), StartPosition = FormStartPosition.CenterScreen,
                                      FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false })
            {
                settingsForm = f;
                var label = new Label {
                    Left = 16, Top = 16, Width = 528, Height = 124,
                    Text = "New PC: use Add another PC in the tray menu to make one private\r\nsetup bundle. Extract " +
                           "and run it on the other PC, enter the password.\r\nExisting PCs can still export/import " +
                           "pairing below. Compare fingerprints.\r\nFiles: copy, wait for Ready to paste, then paste " +
                           "in Explorer.\r\nText, HTML and screenshots sync through Deskflow.\r\nHistory saves text, " +
                           "images and file references locally with encryption."
                };
                var pin = new TextBox { Left = 16, Top = 140, Width = 528, ReadOnly = true,
                                        Text = "Pair fingerprint: " + identity.Pin };
                var export = new Button { Left = 16, Top = 179, Width = 250, Text = "Export pairing file…" };
                export.Click += (s, e) =>
                {
                    try
                    {
                        if (bridge.Busy)
                            throw new IOException("Wait for the transfer to finish.");
                        using (var dialog = new SaveFileDialog { Filter = "Viola pairing (*.vfbpair)|*.vfbpair",
                                                                 FileName = "Viola-pairing.vfbpair" })
                        {
                            if (dialog.ShowDialog() != DialogResult.OK)
                                return;
                            string password = Password("Export pairing", true);
                            if (password == null)
                                return;
                            if (password.Length < 12)
                                throw new IOException("Use at least 12 characters.");
                            File.WriteAllBytes(
                                dialog.FileName,
                                identity.Certificate.Export(
                                    System.Security.Cryptography.X509Certificates.X509ContentType.Pfx, password));
                            MessageBox.Show("Pairing file exported. Share its password separately, import it on the " +
                                            "other PC, and delete the exported file afterward.",
                                            "Viola pairing");
                        }
                    }
                    catch (Exception ex)
                    {
                        ShowError(ex);
                    }
                };
                var import = new Button { Left = 282, Top = 179, Width = 262, Text = "Import pairing file…" };
                import.Click += (s, e) =>
                {
                    try
                    {
                        if (bridge.Busy)
                            throw new IOException("Wait for the transfer to finish.");
                        using (var dialog = new OpenFileDialog { Filter = "Viola pairing (*.vfbpair)|*.vfbpair" })
                        {
                            if (dialog.ShowDialog() != DialogResult.OK)
                                return;
                            if (new FileInfo(dialog.FileName).Length > 65536)
                                throw new IOException("Pairing file too large.");
                            string password = Password("Import pairing", false);
                            if (password == null)
                                return;
                            using (var next = Identity.FromPfx(File.ReadAllBytes(dialog.FileName), password))
                            {
                                if (MessageBox.Show("Replace the current pairing?\r\nNew fingerprint:\r\n" + next.Pin,
                                                    "Confirm pairing", MessageBoxButtons.OKCancel) != DialogResult.OK)
                                    return;
                                next.Save(root);
                            }
                            Log.Write("Pairing replaced by explicit import");
                            Restart();
                        }
                    }
                    catch (Exception ex)
                    {
                        ShowError(ex);
                    }
                };
                var startup = new CheckBox { Left = 16, Top = 226, Width = 320,
                                             Text = "Start with Windows (current user)", Checked = StartupEnabled() };
                startup.CheckedChanged += (s, e) =>
                {
                    try
                    {
                        SetStartup(startup.Checked);
                    }
                    catch (Exception ex)
                    {
                        ShowError(ex);
                    }
                };
                var manual = new Button { Left = 16, Top = 264, Width = 250, Text = "Fallback: connect by IP…" };
                manual.Click += (s, e) =>
                {
                    try
                    {
                        string ip = Prompt("Manual LAN fallback",
                                           "Other PC's IPv4 address (only if automatic discovery fails):", false);
                        if (ip != null)
                            bridge.Manual(ip.Trim());
                    }
                    catch (Exception ex)
                    {
                        ShowError(ex);
                    }
                };
                var firewall =
                    new Button { Left = 282, Top = 264, Width = 262, Text = "Configure Private firewall rules…" };
                firewall.Click += (s, e) =>
                {
                    try
                    {
                        string script = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "Firewall.ps1");
                        if (!File.Exists(script))
                            throw new IOException("Firewall.ps1 missing; use the installer package.");
                        var info = new ProcessStartInfo("powershell.exe",
                                                        "-NoProfile -ExecutionPolicy Bypass -File \"" + script +
                                                            "\" -ProgramPath \"" + Application.ExecutablePath +
                                                            "\"") { Verb = "runas", UseShellExecute = true,
                                                                      WindowStyle = ProcessWindowStyle.Hidden };
                        Process.Start(info);
                    }
                    catch (Exception ex)
                    {
                        Log.Write("Firewall/bootstrap: " + ex);
                        ShowError(ex);
                    }
                };
                var note = new Label { Left = 16, Top = 317, Width = 528, Height = 55 };
                using (var refresh = new System.Windows.Forms.Timer { Interval = 1000 })
                {
                    Action update = () => note.Text = "Status: " + status + "\r\nPeer: " + bridge.Peer;
                    refresh.Tick += (s, e) => update();
                    update();
                    refresh.Start();
                    var trusted =
                        new Button { Left = 16, Top = 385, Width = 250, Text = "Trust this LAN and fix firewall…" };
                    trusted.Click += (s, e) => ConfigureTrustedNetwork();
                    var saved = new Button { Left = 282, Top = 385, Width = 262, Text = "Open clipboard history" };
                    saved.Click += (s, e) => ShowHistory();
                    f.Controls.AddRange(
                        new Control[] { label, pin, export, import, startup, manual, firewall, note, trusted, saved });
                    try
                    {
                        f.ShowDialog();
                    }
                    finally
                    {
                        settingsForm = null;
                    }
                }
            }
        }
        void ConfigureTrustedNetwork()
        {
            try
            {
                string script = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "NetworkSetup.ps1");
                if (!File.Exists(script))
                    throw new IOException("NetworkSetup.ps1 is missing. Reinstall the package.");
                Process.Start(
                    new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -File \"" + script +
                                                               "\" -ProgramPath \"" + Application.ExecutablePath +
                                                               "\"") { UseShellExecute = true, Verb = "runas",
                                                                         WindowStyle = ProcessWindowStyle.Hidden });
            }
            catch (Exception ex)
            {
                ShowError(ex);
            }
        }
        void ShowError(Exception ex)
        {
            Log.Write("Settings: " + ex);
            MessageBox.Show(ex.Message, "Viola File Bridge", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        void Restart()
        {
            Process.Start(new ProcessStartInfo(
                Application.ExecutablePath, "--wait-for " + Process.GetCurrentProcess().Id) { UseShellExecute = true });
            ExitThread();
        }
        protected override void ExitThreadCore()
        {
            exiting = true;
            commands.Dispose();
            SystemEvents.PowerModeChanged -= PowerChanged;
            debounce.Stop();
            housekeeping.Stop();
            if (bridge != null)
                bridge.Dispose();
            tray.Visible = false;
            tray.Dispose();
            if (progressForm != null)
                progressForm.Dispose();
            window.Dispose();
            Log.Write("Application exit");
            base.ExitThreadCore();
        }
    }
    public static class Program
    {
        public static bool Offline;
        [STAThread]
        public static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Log.Debug = args.Contains("--debug");
            Offline = args.Contains("--offline");
            if (args.Length == 2 && args[0] == "--wait-for")
            {
                int pid;
                if (int.TryParse(args[1], out pid))
                    try
                    {
                        Process.GetProcessById(pid).WaitForExit(10000);
                    }
                    catch (ArgumentException)
                    {
                    }
            }
            int rootIndex = Array.IndexOf(args, "--data-root");
            if (rootIndex >= 0 && rootIndex + 1 < args.Length)
                Log.Root = Path.GetFullPath(args[rootIndex + 1]);
            string command = args.Contains("--history")    ? "--history"
                             : args.Contains("--exit")     ? "--exit"
                             : args.Contains("--settings") ? "--settings"
                                                           : "--background";
            if (Commands.Send(Log.Root, command))
                return 0;
            if (command == "--exit")
                return 0;
            bool first;
            using (var mutex = new Mutex(true, "Local\\" + Commands.Name(Log.Root), out first))
            {
                if (!first)
                {
                    if (command != "--background")
                        Commands.Send(Log.Root, command);
                    return 0;
                }
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += (s, e) =>
                {
                    Log.Write("UI exception: " + e.Exception);
                    MessageBox.Show("Viola encountered an error. Details are in:\r\n" + Path.Combine(Log.Root, "Logs") +
                                        "\r\n\r\n" + e.Exception.Message,
                                    "Viola File Bridge", MessageBoxButtons.OK, MessageBoxIcon.Error);
                };
                AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                    Log.Write("Fatal exception: " + e.ExceptionObject);
                try
                {
                    Log.Write("Startup version 0.3.0; OS " + Environment.OSVersion + "; CLR " + Environment.Version +
                              "; STA " + Thread.CurrentThread.GetApartmentState());
                    var app = new TrayApp(Log.Root);
                    if (command != "--background")
                        app.Command(command);
                    Application.Run(app);
                    return 0;
                }
                catch (Exception ex)
                {
                    Log.Write("Startup failure: " + ex);
                    MessageBox.Show("Viola could not start.\r\n" + ex.Message +
                                        "\r\nLogs: " + Path.Combine(Log.Root, "Logs"),
                                    "Viola File Bridge", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 1;
                }
            }
        }
    }
}
