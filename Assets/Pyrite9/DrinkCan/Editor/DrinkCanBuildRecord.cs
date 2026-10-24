// Pyrite9 Drink Can — last build per avatar (inspector section 'Last build')
//   The build plugin writes Library/Pyrite9DrinkCan/last_builds.json keyed by avatar name.
//   Library is per project and outside git, so nothing is left in the user's assets. A failed read counts as 'no record'.
#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Pyrite9.DrinkCan
{
    public static class DcBuildRecord
    {
        public const string Dir = "Library/Pyrite9DrinkCan";
        public const string FilePath = Dir + "/last_builds.json";
        const int MaxEntries = 32;

        [System.Serializable]
        public class Entry
        {
            public string avatar;
            public string time;          // yyyy-MM-dd HH:mm:ss
            public string version;
            public bool fitted;          // the can was built (humanoid avatar found)
            public float canScale;
            public float bodyScale;      // grip offsets are in metres x this (the preview moves the can by it)
            public float handLengthR, handLengthL;   // m
            public int hand;             // DrinkCanHand (spawn hand)
            public int sips;
            public List<string> warnings = new List<string>();   // English, as in the build log
            public List<string> warnKeys = new List<string>();   // the same as DcWarn keys (shown in the menu language); empty in older records
        }

        [System.Serializable]
        class Store { public List<Entry> entries = new List<Entry>(); }

        public static string CleanName(string n) => n == null ? "" : n.Replace("(Clone)", "").Trim();

        static Store Load()
        {
            try
            {
                if (File.Exists(FilePath)) return JsonUtility.FromJson<Store>(File.ReadAllText(FilePath)) ?? new Store();
            }
            catch { }
            return new Store();
        }

        public static Entry Get(string avatar)
        {
            var n = CleanName(avatar);
            return Load().entries.FirstOrDefault(e => e.avatar == n);
        }

        public static long Stamp() { try { return File.Exists(FilePath) ? File.GetLastWriteTimeUtc(FilePath).Ticks : 0; } catch { return 0; } }

        public static void Put(Entry e)
        {
            try
            {
                e.avatar = CleanName(e.avatar);
                var st = Load();
                st.entries.RemoveAll(x => x.avatar == e.avatar);
                st.entries.Insert(0, e);
                if (st.entries.Count > MaxEntries) st.entries.RemoveRange(MaxEntries, st.entries.Count - MaxEntries);
                Directory.CreateDirectory(Dir);
                File.WriteAllText(FilePath, JsonUtility.ToJson(st, true));
            }
            catch (System.Exception ex) { Debug.LogWarning("[Pyrite9 Drink Can] build record not saved: " + ex.Message); }
        }
    }
}
#endif
