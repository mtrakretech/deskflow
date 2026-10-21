// SPDX-FileCopyrightText: (C) 2026 Viola File Bridge contributors
// SPDX-License-Identifier: MIT
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
namespace ViolaSetup
{
    public static class Payload
    {
        public static void Extract(ZipArchive archive, string destination)
        {
            if (archive.Entries.Count > 20000)
                throw new InvalidDataException("Installer payload has too many entries.");
            string[] names = archive.Entries.Select(e => e.FullName.Replace('\\', '/')).ToArray();
            string prefix = names.Length == 0 ? "" : names[0].Split('/')[0] + "/";
            bool strip = names.All(n => n.StartsWith(prefix, StringComparison.Ordinal)) &&
                         names.Any(n => n == prefix + "deskflow.exe");
            long total = 0;
            var seen = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Directory.CreateDirectory(destination);
            // Validate the complete archive before replacing any files.
            foreach (var e in archive.Entries)
            {
                string relative = e.FullName.Replace('\\', '/');
                if (strip)
                    relative = relative.Substring(prefix.Length);
                relative = relative.TrimEnd('/');
                if (relative.Length == 0)
                    continue;
                string path = Viola.SafePath.Resolve(destination, relative);
                if (!seen.Add(path))
                    throw new InvalidDataException("Duplicate payload path.");
                total = checked(total + e.Length);
                if (total > 2L * 1024 * 1024 * 1024)
                    throw new InvalidDataException("Installer payload exceeds 2 GiB.");
            }
            foreach (var e in archive.Entries)
            {
                string relative = e.FullName.Replace('\\', '/');
                if (strip)
                    relative = relative.Substring(prefix.Length);
                bool dir = relative.EndsWith("/");
                relative = relative.TrimEnd('/');
                if (relative.Length == 0)
                    continue;
                string path = Viola.SafePath.Resolve(destination, relative);
                if (dir)
                {
                    Directory.CreateDirectory(path);
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                using (var input = e.Open()) using (
                    var output = new FileStream(path, FileMode.Create, FileAccess.Write)) input.CopyTo(output);
            }
        }
    }
}
