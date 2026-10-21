// SPDX-FileCopyrightText: (C) 2026 Viola File Bridge contributors
// SPDX-License-Identifier: MIT
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Web.Script.Serialization;

namespace Viola
{
    public static class UiJson
    {
        public static JavaScriptSerializer Serializer()
        {
            return new JavaScriptSerializer { MaxJsonLength = 32 * 1024 * 1024, RecursionLimit = 16 };
        }
        public static string Value(Dictionary<string, object> request, string key)
        {
            object value;
            return request.TryGetValue(key, out value) && value is string ? (string)value : "";
        }
        public static string Failure(Exception error)
        {
            return Serializer().Serialize(new { ok = false, error = error.Message });
        }
    }
    public sealed partial class TrayApp
    {
        T OnUi<T>(Func<T> action)
        {
            if (exiting || window.IsDisposed)
                throw new IOException("Clipboard tools are shutting down.");
            return window.InvokeRequired ? (T)window.Invoke(action) : action();
        }
        string QueryUi(string json)
        {
            try
            {
                var request = UiJson.Serializer().Deserialize<Dictionary<string, object>>(json);
                if (request == null)
                    throw new InvalidDataException("Missing clipboard tools request.");
                string op = UiJson.Value(request, "op"), id = UiJson.Value(request, "id");
                object result = new { ok = true };
                switch (op)
                {
                case "status":
                    result =
                        OnUi(() => new { ok = true, status = status, peer = bridge.Peer, fingerprint = identity.Pin,
                                         paused = history.Paused, startup = StartupEnabled() });
                    break;
                case "list":
                    result =
                        new { ok = true,
                              items = history.Store.List(UiJson.Value(request, "search"))
                                          .Select(i => new { id = i.Id, kind = i.Kind, preview = i.Preview,
                                                             created = i.Created.ToString("o"), pinned = i.Pinned })
                                          .ToArray() };
                    break;
                case "preview":
                    using (var clip = history.Store.Get(id)) result =
                        new { ok = true,
                              text = clip.Text ?? (clip.Files != null ? string.Join("\r\n", clip.Files)
                                                                      : clip.Html ?? clip.Rtf ?? ""),
                              png = clip.Png == null ? "" : Convert.ToBase64String(clip.Png),
                              files = clip.Files != null };
                    break;
                case "copy":
                    using (var clip = history.Store.Get(id))
                        OnUi(() =>
                             {
                                 Clipboard.SetDataObject(clip.DataObject(), true, 12, 80);
                                 return true;
                             });
                    break;
                case "pin":
                    history.Store.Pin(id);
                    break;
                case "delete":
                    history.Store.Delete(id);
                    break;
                case "clear":
                    history.Clear().GetAwaiter().GetResult();
                    break;
                case "pause":
                    OnUi(() =>
                         {
                             pauseItem.Checked = !history.Paused;
                             return true;
                         });
                    break;
                case "startup":
                    OnUi(() =>
                         {
                             SetStartup(UiJson.Value(request, "value") == "true");
                             return true;
                         });
                    break;
                case "manual":
                    OnUi(() =>
                         {
                             bridge.Manual(UiJson.Value(request, "address"));
                             return true;
                         });
                    break;
                case "pair-export":
                    string password = UiJson.Value(request, "password");
                    if (password.Length < 12)
                        throw new IOException("Use a password of at least 12 characters.");
                    result =
                        OnUi(() =>
                             {
                                 if (bridge.Busy)
                                     throw new IOException("Wait for the transfer to finish.");
                                 return new { ok = true,
                                              data = Convert.ToBase64String(identity.Certificate.Export(
                                                  System.Security.Cryptography.X509Certificates.X509ContentType.Pfx,
                                                  password)) };
                             });
                    break;
                case "pair-inspect":
                case "pair-import":
                    byte[] pair = Convert.FromBase64String(UiJson.Value(request, "data"));
                    if (pair.Length > 65536)
                        throw new InvalidDataException("Pairing file too large.");
                    using (var next = Identity.FromPfx(pair, UiJson.Value(request, "password")))
                    {
                        result = new { ok = true, fingerprint = next.Pin };
                        if (op == "pair-import")
                        {
                            if (UiJson.Value(request, "confirmedFingerprint") != next.Pin)
                                throw new IOException("Confirm the pairing fingerprint first.");
                            OnUi(() =>
                                 {
                                     if (bridge.Busy)
                                         throw new IOException("Wait for the transfer to finish.");
                                     next.Save(root);
                                     bridge.Dispose();
                                     identity.Dispose();
                                     identity = Identity.LoadOrCreate(root);
                                     StartBridge();
                                     return true;
                                 });
                        }
                    }
                    break;
                case "network":
                    UI(ConfigureTrustedNetwork);
                    break;
                case "add-pc":
                    UI(AddComputer);
                    break;
                case "cache":
                    UI(() => Open(Path.Combine(root, "Cache")));
                    break;
                case "logs":
                    UI(() => Open(Path.Combine(root, "Logs")));
                    break;
                case "received":
                    OnUi(() =>
                         {
                             CopyLatestReceived();
                             return true;
                         });
                    break;
                default:
                    throw new InvalidDataException("Unsupported clipboard tools action.");
                }
                return UiJson.Serializer().Serialize(result);
            }
            catch (Exception error)
            {
                var wrapped = error as System.Reflection.TargetInvocationException;
                return UiJson.Failure(wrapped == null ? error : wrapped.InnerException ?? error);
            }
        }
    }
}
