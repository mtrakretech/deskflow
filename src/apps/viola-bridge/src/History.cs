// SPDX-FileCopyrightText: (C) 2026 Viola File Bridge contributors
// SPDX-License-Identifier: MIT
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace Viola {
public sealed class ClipSnapshot : IDisposable {
    public string Text,Html,Rtf;public string[] Files;public byte[] Png;public Bitmap Image;
    public string Kind {get{return Image!=null||Png!=null?"Screenshot / image":Files!=null?"Files":"Text";}}
    public string Preview {get{return Text!=null?Text.Replace('\r',' ').Replace('\n',' '):Files!=null?string.Join(", ",Files.Select(Path.GetFileName)):"Screenshot / image";}}
    static bool Zero(object value) {if(value is int)return (int)value==0;var s=value as MemoryStream;return s!=null&&s.Length>=4&&BitConverter.ToInt32(s.ToArray(),0)==0;}
    public static ClipSnapshot Capture(IDataObject data) {
        if(data==null||data.GetDataPresent("ExcludeClipboardContentFromMonitorProcessing"))return null;
        if(data.GetDataPresent("CanIncludeInClipboardHistory")&&Zero(data.GetData("CanIncludeInClipboardHistory")))return null;
        var item=new ClipSnapshot();
        try {
            if(data.GetDataPresent(DataFormats.FileDrop)){item.Files=data.GetData(DataFormats.FileDrop) as string[];if(item.Files==null||item.Files.Length==0||item.Files.Length>4096)return null;}
            else {
                if(data.GetDataPresent(DataFormats.UnicodeText))item.Text=data.GetData(DataFormats.UnicodeText) as string;
                else if(data.GetDataPresent(DataFormats.Text))item.Text=data.GetData(DataFormats.Text) as string;
                if(item.Text!=null&&item.Text.Length>1024*1024)return null;
                if(data.GetDataPresent(DataFormats.Html))item.Html=data.GetData(DataFormats.Html) as string;
                if(data.GetDataPresent(DataFormats.Rtf))item.Rtf=data.GetData(DataFormats.Rtf) as string;
                if((item.Html!=null&&item.Html.Length>1024*1024)||(item.Rtf!=null&&item.Rtf.Length>1024*1024))return null;
                if(data.GetDataPresent(DataFormats.Bitmap)){var image=data.GetData(DataFormats.Bitmap) as Image;if(image!=null){if((long)image.Width*image.Height>20000000)return null;item.Image=new Bitmap(image);}}
            }
            return item.Text==null&&item.Image==null&&item.Files==null?null:item;
        }catch{item.Dispose();throw;}
    }
    public void Dispose(){if(Image!=null){Image.Dispose();Image=null;}if(Png!=null){Array.Clear(Png,0,Png.Length);Png=null;}}
    public IDataObject DataObject() {
        var data=new DataObject();if(Text!=null)data.SetData(DataFormats.UnicodeText,Text);if(Html!=null)data.SetData(DataFormats.Html,Html);if(Rtf!=null)data.SetData(DataFormats.Rtf,Rtf);
        if(Png!=null){using(var s=new MemoryStream(Png))using(var image=System.Drawing.Image.FromStream(s)){data.SetImage(new Bitmap(image));}data.SetData("PNG",new MemoryStream(Png,false));}
        if(Files!=null){if(Files.Any(p=>!File.Exists(p)&&!Directory.Exists(p)))throw new IOException("These original files are no longer available. History keeps file references, not a second copy.");var list=new System.Collections.Specialized.StringCollection();list.AddRange(Files);data.SetFileDropList(list);data.SetData("Preferred DropEffect",new MemoryStream(BitConverter.GetBytes(1)));}
        return data;
    }
    public byte[] Encode(){
        if(Image!=null){using(var s=new MemoryStream()){Image.Save(s,ImageFormat.Png);if(s.Length>16*1024*1024)throw new InvalidDataException("History image exceeds 16 MiB.");Png=s.ToArray();}Image.Dispose();Image=null;}
        using(var s=new MemoryStream())using(var w=new BinaryWriter(s,Encoding.UTF8,true)){
            w.Write(0x31484656);Write(w,Text);Write(w,Html);Write(w,Rtf);w.Write(Files==null?0:Files.Length);if(Files!=null)foreach(string p in Files)Write(w,p);w.Write(Png==null?0:Png.Length);if(Png!=null)w.Write(Png);w.Flush();if(s.Length>20*1024*1024)throw new InvalidDataException("History item exceeds limit.");return s.ToArray();
        }
    }
    static void Write(BinaryWriter w,string s){if(s==null){w.Write(-1);return;}byte[] b=Encoding.UTF8.GetBytes(s);if(b.Length>4*1024*1024)throw new InvalidDataException("History text limit.");w.Write(b.Length);w.Write(b);}
    static string Read(BinaryReader r){int n=r.ReadInt32();if(n==-1)return null;if(n<0||n>4*1024*1024||n>r.BaseStream.Length-r.BaseStream.Position)throw new InvalidDataException("Invalid history text length.");return new UTF8Encoding(false,true).GetString(r.ReadBytes(n));}
    public static ClipSnapshot Decode(byte[] bytes){using(var s=new MemoryStream(bytes))using(var r=new BinaryReader(s)){if(r.ReadInt32()!=0x31484656)throw new InvalidDataException("History format.");var item=new ClipSnapshot{Text=Read(r),Html=Read(r),Rtf=Read(r)};int count=r.ReadInt32();if(count<0||count>4096)throw new InvalidDataException("History files limit.");if(count>0){item.Files=new string[count];for(int i=0;i<count;i++)item.Files[i]=Read(r);}int n=r.ReadInt32();if(n<0||n>16*1024*1024||n!=s.Length-s.Position)throw new InvalidDataException("History image framing.");if(n>0)item.Png=r.ReadBytes(n);return item;}}
}
public sealed class HistoryItem {
    public string Id,Kind,Preview;public DateTime Created;public bool Pinned;public long Bytes;
    public override string ToString(){return (Pinned?"★ ":"")+Created.ToLocalTime().ToString("g")+"  "+Kind+"  "+Preview;}
}
public sealed class HistoryStore {
    readonly object gate=new object();readonly string directory;readonly List<HistoryItem> items=new List<HistoryItem>();
    public int MaxItems=100;public long MaxBytes=64L*1024*1024;public TimeSpan MaxAge=TimeSpan.FromDays(7);
    public HistoryStore(string root){directory=Path.Combine(root,"History");Directory.CreateDirectory(directory);}
    static bool IsId(string s){Guid id;return Guid.TryParseExact(s,"N",out id);}
    string PathFor(string id){if(!IsId(id))throw new InvalidDataException("Invalid history ID.");return Path.Combine(directory,id+".vfh");}
    public void Load(){lock(gate){items.Clear();foreach(string path in Directory.EnumerateFiles(directory,"*.vfh").Take(1000))try{
        var f=new FileInfo(path);if(!IsId(Path.GetFileNameWithoutExtension(path))||f.Length>21*1024*1024)continue;
        using(var clip=ReadFile(path)){string id=Path.GetFileNameWithoutExtension(path);items.Add(new HistoryItem{Id=id,Kind=clip.Kind,Preview=Short(clip.Preview),Created=f.LastWriteTimeUtc,Pinned=File.Exists(path+".pin"),Bytes=f.Length});}
    }catch(Exception ex){Log.Write("History skipped unreadable entry: "+ex.GetType().Name);}Trim();}}
    static string Short(string text){return text==null?"":text.Substring(0,Math.Min(text.Length,180));}
    static ClipSnapshot ReadFile(string path){byte[] encrypted=File.ReadAllBytes(path),plain=null;try{plain=ProtectedData.Unprotect(encrypted,null,DataProtectionScope.CurrentUser);return ClipSnapshot.Decode(plain);}finally{if(plain!=null)Array.Clear(plain,0,plain.Length);}}
    public HistoryItem Add(ClipSnapshot clip){byte[] plain=clip.Encode();try{string id=Identity.Hex(Identity.Hash(plain)).Substring(0,32);lock(gate){string path=PathFor(id);var existing=items.FirstOrDefault(i=>i.Id==id);if(existing!=null){existing.Created=DateTime.UtcNow;File.SetLastWriteTimeUtc(path,existing.Created);return existing;}
        byte[] encrypted=ProtectedData.Protect(plain,null,DataProtectionScope.CurrentUser);string tmp=path+".new";File.WriteAllBytes(tmp,encrypted);if(File.Exists(path))File.Delete(path);File.Move(tmp,path);
        var item=new HistoryItem{Id=id,Kind=clip.Kind,Preview=Short(clip.Preview),Created=DateTime.UtcNow,Bytes=encrypted.Length};items.Add(item);Trim();return item;
    }}finally{Array.Clear(plain,0,plain.Length);}}
    public HistoryItem[] List(string query){lock(gate)return items.Where(i=>string.IsNullOrEmpty(query)||i.Preview.IndexOf(query,StringComparison.CurrentCultureIgnoreCase)>=0||i.Kind.IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0).OrderByDescending(i=>i.Pinned).ThenByDescending(i=>i.Created).Select(i=>new HistoryItem{Id=i.Id,Kind=i.Kind,Preview=i.Preview,Created=i.Created,Pinned=i.Pinned,Bytes=i.Bytes}).ToArray();}
    public ClipSnapshot Get(string id){lock(gate){string path=PathFor(id);if(new FileInfo(path).Length>21*1024*1024)throw new InvalidDataException("History item size.");return ReadFile(path);}}
    public void Pin(string id){lock(gate){var item=items.Single(i=>i.Id==id);item.Pinned=!item.Pinned;string p=PathFor(id)+".pin";if(item.Pinned)File.WriteAllText(p,"");else File.Delete(p);}}
    public void Delete(string id){lock(gate){string p=PathFor(id);File.Delete(p);File.Delete(p+".pin");items.RemoveAll(i=>i.Id==id);}}
    public void Clear(){lock(gate)foreach(var item in items.ToArray())Delete(item.Id);}
    void Trim(){foreach(var item in items.Where(i=>!i.Pinned&&DateTime.UtcNow-i.Created>MaxAge).ToArray())Delete(item.Id);while(items.Count>MaxItems||items.Sum(i=>i.Bytes)>MaxBytes){var victim=items.OrderBy(i=>i.Pinned).ThenBy(i=>i.Created).First();Delete(victim.Id);}}
}
public sealed class HistoryService {
    public readonly HistoryStore Store;readonly object gate=new object();Task chain=Task.FromResult(0);int queued;public bool Paused;
    public HistoryService(string root){Store=new HistoryStore(root);chain=Task.Run(()=>{try{Store.Load();}catch(Exception ex){Log.Write("History load: "+ex.GetType().Name);}});}
    public void Capture(IDataObject data){if(Paused)return;lock(gate){if(queued>=2)return;var clip=ClipSnapshot.Capture(data);if(clip==null)return;queued++;chain=chain.ContinueWith(t=>{try{using(clip)Store.Add(clip);}catch(Exception ex){Log.Write("History capture: "+ex.GetType().Name);}finally{lock(gate)queued--;}});}}
    public void Show(Action<IDataObject> restore){var form=new HistoryForm(Store,restore);form.Show();}
}
public sealed class HistoryForm : Form {
    readonly HistoryStore store;readonly Action<IDataObject> restore;readonly ListBox list;readonly TextBox search,preview;readonly PictureBox picture;int selectionVersion;
    public HistoryForm(HistoryStore s,Action<IDataObject> publish){store=s;restore=publish;Text="Clipboard history — text, screenshots and files";MinimumSize=new Size(760,520);Size=new Size(900,620);StartPosition=FormStartPosition.CenterScreen;
        var top=new FlowLayoutPanel{Dock=DockStyle.Top,Height=42};search=new TextBox{Width=240};top.Controls.Add(new Label{Text="Search",AutoSize=true,Padding=new Padding(0,6,0,0)});top.Controls.Add(search);
        Button copy=Button("Copy again",Copy),pin=Button("Pin / unpin",()=>RunSelected(i=>store.Pin(i.Id))),delete=Button("Delete",()=>RunSelected(i=>store.Delete(i.Id))),clear=Button("Clear all",()=>{if(MessageBox.Show("Delete all saved clipboard history, including pinned items?","Clear history",MessageBoxButtons.OKCancel)==DialogResult.OK){store.Clear();RefreshItems();}}),export=Button("Save as…",Export);top.Controls.AddRange(new Control[]{copy,pin,delete,export,clear});
        list=new ListBox{Dock=DockStyle.Left,Width=420,HorizontalScrollbar=true};preview=new TextBox{Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Both};picture=new PictureBox{Dock=DockStyle.Fill,SizeMode=PictureBoxSizeMode.Zoom,Visible=false};var content=new Panel{Dock=DockStyle.Fill};content.Controls.Add(preview);content.Controls.Add(picture);
        var note=new Label{Dock=DockStyle.Bottom,Height=42,Text="Saved locally with Windows user encryption. Up to 100 items / 64 MiB, 7 days for unpinned items.\r\nCopy again, then paste in your app. File history references the original files."};Controls.Add(content);Controls.Add(list);Controls.Add(top);Controls.Add(note);
        search.TextChanged+=(a,b)=>RefreshItems();list.SelectedIndexChanged+=(a,b)=>Preview();list.DoubleClick+=(a,b)=>Copy();Shown+=(a,b)=>RefreshItems();FormClosed+=(a,b)=>{if(picture.Image!=null)picture.Image.Dispose();};
    }
    static Button Button(string text,Action action){var b=new Button{Text=text,AutoSize=true};b.Click+=(s,e)=>{try{action();}catch(Exception ex){MessageBox.Show(ex.Message,"Clipboard history",MessageBoxButtons.OK,MessageBoxIcon.Error);}};return b;}
    void RefreshItems(){list.Items.Clear();list.Items.AddRange(store.List(search.Text));}
    HistoryItem Selected {get{return list.SelectedItem as HistoryItem;}}
    void RunSelected(Action<HistoryItem> action){var item=Selected;if(item!=null){action(item);RefreshItems();}}
    void Copy(){var item=Selected;if(item==null)return;using(var clip=store.Get(item.Id))restore(clip.DataObject());Close();}
    void Export(){var item=Selected;if(item==null)return;using(var clip=store.Get(item.Id))using(var dialog=new SaveFileDialog{Filter=clip.Png!=null?"PNG image|*.png":"UTF-8 text|*.txt",FileName=clip.Png!=null?"clipboard.png":"clipboard.txt"}){if(clip.Files!=null){MessageBox.Show("Use Copy again to copy the referenced files in Explorer.");return;}if(dialog.ShowDialog()!=DialogResult.OK)return;if(clip.Png!=null)File.WriteAllBytes(dialog.FileName,clip.Png);else File.WriteAllText(dialog.FileName,clip.Text??"",new UTF8Encoding(false));}}
    async void Preview(){int version=++selectionVersion;var item=Selected;if(item==null)return;try{var clip=await Task.Run(()=>store.Get(item.Id));using(clip){if(IsDisposed||version!=selectionVersion)return;if(picture.Image!=null){picture.Image.Dispose();picture.Image=null;}picture.Visible=clip.Png!=null;preview.Visible=clip.Png==null;if(clip.Png!=null){using(var stream=new MemoryStream(clip.Png))using(var image=Image.FromStream(stream))picture.Image=new Bitmap(image);}else preview.Text=clip.Text??(clip.Files!=null?string.Join(Environment.NewLine,clip.Files):"");}}catch(Exception ex){if(!IsDisposed)preview.Text=ex.Message;}}
}
}
