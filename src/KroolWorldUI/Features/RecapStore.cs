using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using HarmonyLib;

namespace KroolWorldUI.Features
{
    /// <summary>
    /// Keeps the Deaths and Bosses lists between sessions. One small file per character in
    /// BepInEx/config/KroolWorldUI, read each time that character enters a world and rewritten after
    /// each death or boss kill. The mod's own file: nothing of the game's is written.
    ///
    /// Each recap is stored with its length in front, so a later version can add fields at the
    /// end of one and this version still reads the part it knows.
    /// </summary>
    internal static class RecapStore
    {
        internal const int MaxDeaths = 10;
        internal const int MaxBosses = 10;

        private const int Magic = 0x4B575552; // "RUWK" on disk
        private const int Format = 1;
        private const int MaxItems = 1000;    // more than this in a count means a damaged file

        private static PlayerProfile _profile; // whose lists these are
        private static string _path;           // that character's file; null when this version must leave it alone
        private static bool _unread;           // the file is there but could not be opened: read it before the next save

        /// <summary>
        /// Makes the lists the current character's, each time a character enters a world. Cheap
        /// when nothing changed; called every HUD frame and before anything is added to a list.
        /// </summary>
        internal static void Sync()
        {
            try
            {
                var profile = Game.instance?.GetPlayerProfile();
                if (profile == null || ReferenceEquals(profile, _profile))
                    return;
                _profile = profile;
                _path = null;
                _unread = false;
                DeathRecap.History.Clear();
                BossFights.History.Clear();
                string name = profile.GetFilename();
                if (string.IsNullOrEmpty(name))
                    return;
                // The character's own id as well: a new character with a deleted one's name starts clean.
                string id = profile.GetPlayerID().ToString(System.Globalization.CultureInfo.InvariantCulture);
                Open(Path.Combine(BepInEx.Paths.ConfigPath, "KroolWorldUI", $"recaps_{SafeName(name)}_{id}.dat"));
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Recap history not loaded: {e.Message}");
            }
        }

        /// <summary>Empties the lists and fills them from this file, which later saves go to.</summary>
        internal static void Open(string path)
        {
            DeathRecap.History.Clear();
            BossFights.History.Clear();
            _path = path;
            _unread = false;
            try
            {
                // A save that stopped between removing the old file and renaming the new one.
                if (!File.Exists(path) && File.Exists(path + ".new"))
                    File.Move(path + ".new", path);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Recap history: {e.Message}");
            }
            Load();
        }

        /// <summary>
        /// Adds the file's recaps after those already in the lists (a saved one is older than
        /// anything this session added). A file that cannot be used is never written over blind:
        /// one that will not open is tried again before the next save, one from another version
        /// of the mod is left alone, and a damaged one is copied to .bad first.
        /// </summary>
        private static void Load()
        {
            string path = _path;
            if (path == null || !File.Exists(path))
            {
                _unread = false;
                return;
            }
            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(path);
            }
            catch (Exception e)
            {
                if (!_unread)
                    Plugin.Log.LogWarning($"Recap history could not be opened ({e.Message}); trying again at the next save.");
                _unread = true;
                return;
            }
            _unread = false;
            var deaths = new List<DeathRecapData>();
            var bosses = new List<BossRecapData>();
            try
            {
                Read(bytes, deaths, bosses);
            }
            catch (OtherVersion e)
            {
                _path = null;
                Plugin.Log.LogWarning($"Recap history was {e.Message}. That file is left as it is, and this version keeps recaps for this session only.");
                return;
            }
            catch (Exception e)
            {
                try { File.Copy(path, path + ".bad", true); }
                catch { /* the copy is a courtesy */ }
                Plugin.Log.LogWarning($"Recap history is damaged ({e.Message}); kept what could be read, and a copy of the file as .bad.");
            }
            foreach (var d in deaths)
                if (DeathRecap.History.Count < MaxDeaths)
                    DeathRecap.History.Add(d);
            foreach (var b in bosses)
                if (BossFights.History.Count < MaxBosses)
                    BossFights.History.Add(b);
            Plugin.Log.LogInfo($"Recap history: {deaths.Count} deaths, {bosses.Count} boss kills loaded.");
        }

        /// <summary>Writes both lists to the current character's file.</summary>
        internal static void Save()
        {
            try
            {
                if (_unread)
                    Load();
                if (_path == null || _unread)
                    return;
                byte[] bytes = Write(DeathRecap.History, BossFights.History);
                Directory.CreateDirectory(Path.GetDirectoryName(_path));
                // Whole file to a spare name first, so a crash mid-write cannot leave half a file.
                string spare = _path + ".new";
                File.WriteAllBytes(spare, bytes);
                if (File.Exists(_path))
                    File.Delete(_path);
                File.Move(spare, _path);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Recap history not saved: {e.Message}");
            }
        }

        /// <summary>A file this version does not know how to read, as opposed to a damaged one.</summary>
        private sealed class OtherVersion : IOException
        {
            internal OtherVersion(string message) : base(message) { }
        }

        [HarmonyPatch(typeof(Hud), "Update")]
        private static class Tick
        {
            private static void Postfix()
            {
                if (DeathRecap.Enabled.Value || BossRecap.Enabled.Value)
                    Sync();
            }
        }

        private static string SafeName(string file)
        {
            var sb = new StringBuilder(file.Length);
            foreach (char c in file)
                sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');
            return sb.ToString();
        }

        // ------------------------------------------------------------------ format

        internal static byte[] Write(List<DeathRecapData> deaths, List<BossRecapData> bosses)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms, Encoding.UTF8))
            {
                w.Write(Magic);
                w.Write(Format);
                w.Write(deaths.Count);
                foreach (var d in deaths)
                    Block(w, b => WriteDeath(b, d));
                w.Write(bosses.Count);
                foreach (var boss in bosses)
                    Block(w, b => WriteBoss(b, boss));
                w.Flush();
                return ms.ToArray();
            }
        }

        /// <summary>Adds what the file holds to the two lists. A recap that cannot be read is left out; the rest still load.</summary>
        internal static void Read(byte[] bytes, List<DeathRecapData> deaths, List<BossRecapData> bosses)
        {
            using (var r = new BinaryReader(new MemoryStream(bytes), Encoding.UTF8))
            {
                if (r.ReadInt32() != Magic)
                    throw new InvalidDataException("not a recap file");
                int format = r.ReadInt32();
                if (format != Format)
                    throw new OtherVersion($"written by another version of the mod (format {format})");
                int n = Count(r);
                for (int i = 0; i < n; i++)
                {
                    var d = Block(r, ReadDeath);
                    if (d != null && deaths.Count < MaxDeaths)
                        deaths.Add(d);
                }
                n = Count(r);
                for (int i = 0; i < n; i++)
                {
                    var b = Block(r, ReadBoss);
                    if (b != null && bosses.Count < MaxBosses)
                        bosses.Add(b);
                }
            }
        }

        private static void Block(BinaryWriter w, Action<BinaryWriter> write)
        {
            using (var ms = new MemoryStream())
            using (var b = new BinaryWriter(ms, Encoding.UTF8))
            {
                write(b);
                b.Flush();
                w.Write((int)ms.Length);
                w.Write(ms.GetBuffer(), 0, (int)ms.Length);
            }
        }

        private static T Block<T>(BinaryReader r, Func<BinaryReader, T> read) where T : class
        {
            int length = r.ReadInt32();
            if (length < 0 || length > r.BaseStream.Length - r.BaseStream.Position)
                throw new InvalidDataException("cut short");
            byte[] bytes = r.ReadBytes(length);
            try
            {
                using (var b = new BinaryReader(new MemoryStream(bytes), Encoding.UTF8))
                    return read(b);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Recap history: one entry skipped ({e.Message}).");
                return null;
            }
        }

        private static int Count(BinaryReader r)
        {
            int n = r.ReadInt32();
            if (n < 0 || n > MaxItems)
                throw new InvalidDataException("bad count");
            return n;
        }

        private static void WriteDeath(BinaryWriter w, DeathRecapData d)
        {
            WriteTime(w, d.When);
            WriteWho(w, d.Killer);
            w.Write(d.KillingBlow);
            w.Write(d.Overkill);
            WriteFloats(w, d.KillingTypes);
            w.Write(d.Taken);
            w.Write(d.Stopped);
            w.Write(d.Seconds);
            w.Write(d.MaxHealth);
            w.Write(d.Rows.Count);
            foreach (var row in d.Rows)
            {
                WriteWho(w, row.Who);
                w.Write(row.Taken);
                w.Write(row.Raw);
                w.Write(row.Hits);
                w.Write(row.Ticks);
                w.Write(row.Blocked);
                WriteFloats(w, row.Types);
            }
        }

        private static DeathRecapData ReadDeath(BinaryReader r)
        {
            var d = new DeathRecapData
            {
                When = ReadTime(r), Killer = ReadWho(r), KillingBlow = Num(r), Overkill = Num(r),
                KillingTypes = ReadFloats(r), Taken = Num(r), Stopped = Num(r), Seconds = Num(r),
                MaxHealth = Num(r),
            };
            int n = Count(r);
            for (int i = 0; i < n; i++)
            {
                d.Rows.Add(new DeathRecapData.Row
                {
                    Who = ReadWho(r), Taken = Num(r), Raw = Num(r), Hits = r.ReadInt32(),
                    Ticks = r.ReadInt32(), Blocked = r.ReadInt32(), Types = ReadFloats(r) ?? new float[Dmg.Count],
                });
            }
            return d;
        }

        private static void WriteBoss(BinaryWriter w, BossRecapData b)
        {
            WriteTime(w, b.When);
            WriteText(w, b.Name);
            WriteText(w, b.Prefab);
            w.Write(b.MaxHealth);
            w.Write(b.Seconds);
            w.Write(b.Total);
            w.Write(b.Fighters.Count);
            foreach (var f in b.Fighters)
            {
                WriteWho(w, f.Who);
                w.Write(f.IsLocal);
                w.Write(f.Estimated);
                w.Write(f.Total);
                w.Write(f.Hits);
                WriteFloats(w, f.Types);
                w.Write(f.Sources.Count);
                foreach (var s in f.Sources)
                {
                    WriteText(w, s.Weapon);
                    WriteText(w, s.Ammo);
                    WriteText(w, s.Effect);
                    w.Write(s.Skill);
                    w.Write(s.Type);
                    w.Write(s.Total);
                    w.Write(s.Hits);
                    WriteFloats(w, s.Types);
                }
            }
        }

        private static BossRecapData ReadBoss(BinaryReader r)
        {
            var b = new BossRecapData
            {
                When = ReadTime(r), Name = ReadText(r) ?? "", Prefab = ReadText(r) ?? "", MaxHealth = Num(r),
                Seconds = Num(r), Total = Num(r),
            };
            int fighters = Count(r);
            for (int i = 0; i < fighters; i++)
            {
                var f = new RecapFighter
                {
                    Who = ReadWho(r), IsLocal = r.ReadBoolean(), Estimated = r.ReadBoolean(), Total = Num(r),
                    Hits = r.ReadInt32(), Types = ReadFloats(r) ?? new float[Dmg.Count],
                };
                int sources = Count(r);
                for (int j = 0; j < sources; j++)
                {
                    var s = new RecapSource
                    {
                        Weapon = ReadText(r), Ammo = ReadText(r), Effect = ReadText(r), Skill = r.ReadInt32(),
                        Type = r.ReadInt32(), Total = Num(r), Hits = r.ReadInt32(),
                        Types = ReadFloats(r) ?? new float[Dmg.Count],
                    };
                    // The view indexes damage type names with this.
                    if (s.Type >= Dmg.Count)
                        s.Type = -1;
                    f.Sources.Add(s);
                }
                b.Fighters.Add(f);
            }
            return b;
        }

        private static void WriteWho(BinaryWriter w, Who who)
        {
            WriteText(w, who?.Key ?? "");
            WriteText(w, who?.Name);
            WriteText(w, who?.Prefab);
            w.Write(who != null && who.IsPlayer);
            w.Write(who?.Level ?? 0);
            w.Write(who != null ? (int)who.Cause : 0);
        }

        private static Who ReadWho(BinaryReader r) => new Who
        {
            Key = ReadText(r) ?? "", Name = ReadText(r), Prefab = ReadText(r), IsPlayer = r.ReadBoolean(),
            Level = r.ReadInt32(), Cause = (HitData.HitType)r.ReadInt32(),
        };

        private static void WriteTime(BinaryWriter w, DateTime when) => w.Write(when.ToUniversalTime().Ticks);

        private static DateTime ReadTime(BinaryReader r) => new DateTime(r.ReadInt64(), DateTimeKind.Utc).ToLocalTime();

        private static void WriteText(BinaryWriter w, string s)
        {
            w.Write(s != null);
            if (s != null)
                w.Write(s);
        }

        private static string ReadText(BinaryReader r) => r.ReadBoolean() ? r.ReadString() : null;

        private static void WriteFloats(BinaryWriter w, float[] a)
        {
            w.Write(a != null ? a.Length : -1);
            if (a != null)
                foreach (float v in a)
                    w.Write(v);
        }

        /// <summary>Damage by type. Always <see cref="Dmg.Count"/> long, whatever the file's count: a version with more types still reads.</summary>
        private static float[] ReadFloats(BinaryReader r)
        {
            int n = r.ReadInt32();
            if (n == -1)
                return null;
            if (n < 0 || n > MaxItems)
                throw new InvalidDataException("bad count");
            var a = new float[Dmg.Count];
            for (int i = 0; i < n; i++)
            {
                float v = Num(r);
                if (i < a.Length)
                    a[i] = v;
            }
            return a;
        }

        /// <summary>A number from the file; 0 for one no recap can hold (a damaged file), so the view never lays out with NaN.</summary>
        private static float Num(BinaryReader r)
        {
            float v = r.ReadSingle();
            return float.IsNaN(v) || float.IsInfinity(v) ? 0f : v;
        }
    }
}
