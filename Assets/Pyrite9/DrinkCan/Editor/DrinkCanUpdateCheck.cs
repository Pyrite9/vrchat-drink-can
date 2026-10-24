// Pyrite9 Drink Can — new version check (inspector header), after the wheelchair's WcUpdateCheck
//   Compares the tag (vX.Y.Z) of the public repository's GitHub Releases 'latest' with DrinkCanConst.Version. No auth, sends
//   nothing (one GET). Automatic once a day (when the inspector opens), result kept in EditorPrefs. A failure is a quiet "couldn't check".
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace Pyrite9.DrinkCan
{
    public static class DcUpdateCheck
    {
        public const string Api = "https://api.github.com/repos/Pyrite9/vrchat-drink-can/releases/latest";
        public const string ReleasesPage = "https://github.com/Pyrite9/vrchat-drink-can/releases";
        const string KeyTag = "Pyrite9DC.LatestTag", KeyUrl = "Pyrite9DC.LatestUrl", KeyAt = "Pyrite9DC.CheckedAt", KeyFail = "Pyrite9DC.CheckFailed";
        static readonly System.TimeSpan Interval = System.TimeSpan.FromHours(24);

        public enum State { Unknown, Checking, Latest, Newer, Failed }

        [System.Serializable] class Release { public string tag_name; public string html_url; }

        static UnityWebRequest running;
        public static event System.Action Changed;

        public static string LatestTag => EditorPrefs.GetString(KeyTag, "");
        public static string LatestUrl { get { var u = EditorPrefs.GetString(KeyUrl, ""); return string.IsNullOrEmpty(u) ? ReleasesPage : u; } }

        public static State Current
        {
            get
            {
                if (running != null) return State.Checking;
                if (EditorPrefs.GetBool(KeyFail, false)) return State.Failed;
                var tag = LatestTag;
                if (string.IsNullOrEmpty(tag)) return State.Unknown;
                return Compare(tag, DrinkCanConst.Version) > 0 ? State.Newer : State.Latest;
            }
        }

        // check if a day has passed
        public static void CheckIfDue()
        {
            long at = System.Convert.ToInt64(EditorPrefs.GetString(KeyAt, "0"));
            if (System.DateTime.UtcNow - new System.DateTime(at, System.DateTimeKind.Utc) >= Interval) Check();
        }

        public static void Check()
        {
            if (running != null) return;
            var req = UnityWebRequest.Get(Api);
            req.timeout = 10;
            req.SetRequestHeader("Accept", "application/vnd.github+json");
            running = req;
            Changed?.Invoke();
            req.SendWebRequest().completed += _ =>
            {
                bool ok = req.result == UnityWebRequest.Result.Success;
                Release rel = null;
                if (ok) { try { rel = JsonUtility.FromJson<Release>(req.downloadHandler.text); } catch { } }
                ok = ok && rel != null && !string.IsNullOrEmpty(rel.tag_name);
                if (ok) { EditorPrefs.SetString(KeyTag, rel.tag_name.Trim()); EditorPrefs.SetString(KeyUrl, rel.html_url ?? ""); }
                EditorPrefs.SetBool(KeyFail, !ok);
                EditorPrefs.SetString(KeyAt, System.DateTime.UtcNow.Ticks.ToString());
                req.Dispose();
                running = null;
                Changed?.Invoke();
            };
        }

        // "v0.1.1" vs "0.1.0" -> positive when a is newer. Unreadable -> 0 (treated as the same)
        public static int Compare(string a, string b)
        {
            return System.Version.TryParse(a.TrimStart('v', 'V'), out var va) && System.Version.TryParse(b.TrimStart('v', 'V'), out var vb) ? va.CompareTo(vb) : 0;
        }
    }
}
#endif
