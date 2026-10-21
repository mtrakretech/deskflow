using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Windows.Forms;
using System.Diagnostics;
using System.Reflection;
using Microsoft.Win32;
using System.Threading;
using System.IO.Compression;
using Viola;
namespace ViolaSetup {
public static class Setup {
    static string data=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ViolaFileBridge");
    static string app=Path.Combine(data,"App");
    static string[] files={"ViolaFileBridge.exe","ViolaFileBridge.exe.config","Firewall.ps1","NetworkSetup.ps1","README.md","SECURITY.md","CHANGELOG.md","LICENSE"};
    static void Log(string message){Directory.CreateDirectory(Path.Combine(data,"Logs"));File.AppendAllText(Path.Combine(data,"Logs","installer.log"),DateTime.UtcNow.ToString("o")+" "+message+Environment.NewLine);}
    static void Stop(){foreach(var p in Process.GetProcessesByName("ViolaFileBridge"))try{if(string.Equals(Path.GetFullPath(p.MainModule.FileName),Path.Combine(app,"ViolaFileBridge.exe"),StringComparison.OrdinalIgnoreCase)){p.CloseMainWindow();if(!p.WaitForExit(1500)){p.Kill();p.WaitForExit(5000);}}}catch(Exception ex){throw new IOException("Could not stop installed Viola. Exit its tray icon and retry.",ex);}}
    static void Startup(bool enabled){using(var key=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")){if(enabled)key.SetValue("ViolaFileBridge","\""+Path.Combine(app,"ViolaFileBridge.exe")+"\"");else key.DeleteValue("ViolaFileBridge",false);}}
    static bool Firewall(bool remove){string script=Path.Combine(app,"Firewall.ps1");var info=new ProcessStartInfo("powershell.exe","-NoProfile -ExecutionPolicy Bypass -File \""+script+"\" -ProgramPath \""+Path.Combine(app,"ViolaFileBridge.exe")+"\""+(remove?" -Remove":"")){UseShellExecute=true,Verb="runas",WindowStyle=ProcessWindowStyle.Hidden};try{using(var p=Process.Start(info)){p.WaitForExit();if(p.ExitCode!=0)throw new IOException("Firewall helper failed; inspect firewall.log.");}return true;}catch(Exception ex){Log("Firewall bootstrap: "+ex);MessageBox.Show("Windows did not configure Viola's firewall rules.\r\n"+ex.Message+"\r\nYou can retry from Settings. The app runs without administrator rights.","Viola firewall",MessageBoxButtons.OK,MessageBoxIcon.Warning);return false;}}
    static void Install(bool start,bool startup,bool firewall){
        using(var framework=Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full")){if(framework==null||Convert.ToInt32(framework.GetValue("Release",0))<528040)throw new IOException("Install .NET Framework 4.8 from Windows Update before continuing.");}
        Identity invitation=null;string pairing=Path.Combine(Path.GetDirectoryName(Application.ExecutablePath),"Pairing.vfbpair");
        if(File.Exists(pairing)){
            if(new FileInfo(pairing).Length>65536)throw new IOException("Pairing file too large.");
            string password=PromptPassword();if(password==null)throw new OperationCanceledException("Pairing cancelled; nothing installed.");
            invitation=Identity.FromPfx(File.ReadAllBytes(pairing),password);
            string prompt="Join the PC that supplied this private setup bundle?\r\nCompare this fingerprint with that PC:\r\n"+invitation.Pin;
            if(File.Exists(Path.Combine(data,"pairing.dpapi")))prompt+="\r\nThis replaces your existing Viola pairing.";
            if(MessageBox.Show(prompt,"Confirm pairing",MessageBoxButtons.OKCancel)!=DialogResult.OK){invitation.Dispose();throw new OperationCanceledException("Pairing cancelled.");}
        }
        try {
        Directory.CreateDirectory(data);Stop();
        if(Directory.Exists(app)){string backup=Path.Combine(data,"Backups",DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));Directory.CreateDirectory(backup);foreach(string f in files)if(File.Exists(Path.Combine(app,f)))File.Copy(Path.Combine(app,f),Path.Combine(backup,f));}
        Directory.CreateDirectory(app);Directory.CreateDirectory(Path.Combine(data,"Cache"));Directory.CreateDirectory(Path.Combine(data,"Logs"));
        var assembly=Assembly.GetExecutingAssembly();
        foreach(string file in files)using(var stream=assembly.GetManifestResourceStream(file)){if(stream==null)throw new IOException("Installer resource missing: "+file);using(var output=new FileStream(Path.Combine(app,file),FileMode.Create,FileAccess.Write))stream.CopyTo(output);}
        using(var payload=assembly.GetManifestResourceStream("DeskflowPayload.zip"))if(payload!=null){
            string target=Path.Combine(app,"Deskflow");foreach(var p in Process.GetProcessesByName("deskflow").Concat(Process.GetProcessesByName("deskflow-core")))using(p){if(SafePath.IsInside(target,p.MainModule.FileName))throw new IOException("Exit the forked Deskflow before updating its files. Your original Deskflow is not stopped.");}
            using(var archive=new ZipArchive(payload,ZipArchiveMode.Read))Payload.Extract(archive,target);
        }
        File.Copy(Application.ExecutablePath,Path.Combine(app,"Uninstall.exe"),true);File.Copy(Application.ExecutablePath,Path.Combine(app,"ViolaFileBridge-Setup.exe"),true);Startup(startup);
        if(invitation!=null){string old=Path.Combine(data,"pairing.dpapi");if(File.Exists(old)){string backup=Path.Combine(data,"Backups","pairing-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));Directory.CreateDirectory(backup);File.Copy(old,Path.Combine(backup,"pairing.dpapi"));}invitation.Save(data);}
        using(var key=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\ViolaFileBridge")){key.SetValue("DisplayName","Deskflow + Viola Clipboard");key.SetValue("DisplayVersion","0.3.0");key.SetValue("Publisher","Viola File Bridge contributors");key.SetValue("InstallLocation",app);key.SetValue("DisplayIcon",Path.Combine(app,"ViolaFileBridge.exe"));key.SetValue("UninstallString","\""+Path.Combine(app,"Uninstall.exe")+"\" --uninstall");key.SetValue("NoModify",1);key.SetValue("NoRepair",1);}
        // Remove only this application's known legacy shortcuts; original files were backed up during diagnosis.
        foreach(string folder in new[]{Environment.GetFolderPath(Environment.SpecialFolder.Startup),Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)}){string old=Path.Combine(folder,"Viola File Bridge.lnk");if(File.Exists(old))File.Delete(old);}
        if(firewall)Firewall(false);Log("Installed 0.3.0, startup="+startup);
        string deskflow=Path.Combine(app,"Deskflow","deskflow.exe");
        using(var key=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")){if(startup&&File.Exists(deskflow))key.SetValue("DeskflowViola","\""+deskflow+"\"");else key.DeleteValue("DeskflowViola",false);}
        if(start){Process.Start(new ProcessStartInfo(Path.Combine(app,"ViolaFileBridge.exe")){UseShellExecute=true});if(File.Exists(deskflow)&&Process.GetProcessesByName("deskflow").Length==0)Process.Start(new ProcessStartInfo(deskflow){UseShellExecute=true});}
        } finally {if(invitation!=null)invitation.Dispose();}
    }
    static string PromptPassword(){using(var f=new Form{Text="Join your existing PC",ClientSize=new Size(460,155),StartPosition=FormStartPosition.CenterScreen,FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false}){
        var label=new Label{Left=14,Top=12,Width=430,Height=42,Text="Enter the password chosen on the first PC when creating this private setup bundle."};var text=new TextBox{Left=14,Top=60,Width=430,UseSystemPasswordChar=true};var ok=new Button{Text="Join",DialogResult=DialogResult.OK,Left=275,Top=106};var cancel=new Button{Text="Cancel",DialogResult=DialogResult.Cancel,Left=365,Top=106};f.Controls.AddRange(new Control[]{label,text,ok,cancel});f.AcceptButton=ok;f.CancelButton=cancel;return f.ShowDialog()==DialogResult.OK?text.Text:null;}}
    static void Uninstall(){
        if(MessageBox.Show("Remove Viola File Bridge from this Windows user?","Uninstall Viola",MessageBoxButtons.OKCancel)!=DialogResult.OK)return;
        string bundled=Path.Combine(app,"Deskflow");foreach(var p in Process.GetProcessesByName("deskflow").Concat(Process.GetProcessesByName("deskflow-core")))using(p){if(SafePath.IsInside(bundled,p.MainModule.FileName))throw new IOException("Exit the forked Deskflow first, then retry uninstall.");}
        Stop();Startup(false);if(!Firewall(true)){MessageBox.Show("Uninstall stopped so the firewall rules can be removed first. Retry and approve Windows' firewall helper.","Viola uninstall");return;}
        using(var key=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))key.DeleteValue("DeskflowViola",false);
        Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\ViolaFileBridge",false);
        foreach(string file in files){string p=Path.Combine(app,file);if(File.Exists(p))File.Delete(p);}
        bool purge=MessageBox.Show("Also delete pairing configuration, cache, logs and application backups?\r\nChoose No to preserve them.","Keep Viola data?",MessageBoxButtons.YesNo,MessageBoxIcon.Question)==DialogResult.Yes;
        // A copied uninstaller runs outside App, so its installed copy is unlocked.
        string uninstaller=Path.Combine(app,"Uninstall.exe");if(File.Exists(uninstaller))File.Delete(uninstaller);string template=Path.Combine(app,"ViolaFileBridge-Setup.exe");if(File.Exists(template))File.Delete(template);if(Directory.Exists(bundled))DeleteData(bundled);if(Directory.Exists(app)&&!Directory.EnumerateFileSystemEntries(app).Any())Directory.Delete(app);
        if(purge){string resolved=Path.GetFullPath(data);string expected=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ViolaFileBridge");if(!string.Equals(resolved,expected,StringComparison.OrdinalIgnoreCase))throw new IOException("Unexpected data path.");DeleteData(resolved);}
        MessageBox.Show("Viola File Bridge removed."+(purge?"":" Configuration and cache were preserved."),"Viola uninstall");
    }
    static void DeleteData(string dir){if(!Directory.Exists(dir))return;if((File.GetAttributes(dir)&FileAttributes.ReparsePoint)!=0)throw new IOException("Refusing to delete a reparse directory.");foreach(string p in Directory.EnumerateFileSystemEntries(dir)){var a=File.GetAttributes(p);if((a&FileAttributes.ReparsePoint)!=0)throw new IOException("Refusing to delete reparse data.");if((a&FileAttributes.Directory)!=0)DeleteData(p);else{File.SetAttributes(p,FileAttributes.Normal);File.Delete(p);}}Directory.Delete(dir);}
    [STAThread]public static int Main(string[] args){Application.EnableVisualStyles();try{
        if(args.Contains("--uninstall")&&!args.Contains("--worker")){string tmp=Path.Combine(Path.GetTempPath(),"Viola-Uninstall-"+Guid.NewGuid().ToString("N")+".exe");File.Copy(Application.ExecutablePath,tmp);Process.Start(new ProcessStartInfo(tmp,"--uninstall --worker"){UseShellExecute=true});return 0;}
        if(args.Contains("--uninstall")){Uninstall();return 0;}
        if(args.Contains("--install-local")){Install(false,true,false);return 0;}
        using(var f=new Form{Text="Install Deskflow + Viola Clipboard 0.3.0",ClientSize=new Size(490,330),StartPosition=FormStartPosition.CenterScreen,FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false}){
            var label=new Label{Left=18,Top=16,Width=452,Height=85,Text="Files, clipboard history, text and screenshots beside Deskflow.\r\nInstalls for your user. Firewall setup alone requests elevation.\r\nAn included pairing file is imported with your password once.\r\nText/screenshot history is encrypted locally; pause it from the tray."};
            var startup=new CheckBox{Left=18,Top=104,Width=440,Text="Start with Windows",Checked=true};var firewall=new CheckBox{Left=18,Top=138,Width=440,Text="Configure application firewall rules (Private LAN only)",Checked=true};var start=new CheckBox{Left=18,Top=172,Width=440,Text="Start Viola after installation",Checked=true};
            var install=new Button{Left=278,Top=234,Width=90,Text="Install"};var cancel=new Button{Left=380,Top=234,Width=90,Text="Cancel",DialogResult=DialogResult.Cancel};
            install.Click+=(s,e)=>{install.Enabled=false;try{Install(start.Checked,startup.Checked,firewall.Checked);f.DialogResult=DialogResult.OK;f.Close();}catch(Exception ex){Log("Install failure: "+ex);MessageBox.Show(ex.Message,"Viola installation failed",MessageBoxButtons.OK,MessageBoxIcon.Error);install.Enabled=true;}};
            f.Controls.AddRange(new Control[]{label,startup,firewall,start,install,cancel});f.CancelButton=cancel;f.ShowDialog();
        }return 0;
    }catch(Exception ex){Log(ex.ToString());MessageBox.Show(ex.Message,"Viola setup error",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1;}}
}
}
