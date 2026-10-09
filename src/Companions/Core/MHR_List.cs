using System;
using System.Collections.Generic;

namespace Companions
{
    // ---- 1.4.106: one way to read every list setting (user: "* for everything, -name to leave it off, name:100 to
    // specify an amount for only that item"). Comma-separated:
    //   *         everything
    //   Name      this one
    //   -Name     never this one (wins over everything else)
    //   Name:100  this one, with its own amount (0 = no limit)
    // Names are matched without caring about upper/lower case, on the prefab name or the in-game name.
    public sealed class MHR_List
    {
        public bool All { get; private set; }
        public readonly HashSet<string> Named = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> Excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, int> Amounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public readonly List<string> Order = new List<string>();   // named entries in the order written

        static readonly Dictionary<string, MHR_List> s_cache = new Dictionary<string, MHR_List>();

        public static MHR_List Get(string raw)
        {
            raw = raw ?? "";
            MHR_List l;
            if (s_cache.TryGetValue(raw, out l)) return l;
            l = new MHR_List();
            foreach (string part in raw.Split(','))
            {
                string s = part.Trim();
                if (s.Length == 0) continue;
                if (s == "*") { l.All = true; continue; }
                if (s.StartsWith("-")) { s = s.Substring(1).Trim(); if (s.Length > 0) l.Excluded.Add(s); continue; }
                int colon = s.IndexOf(':');
                if (colon >= 0)
                {
                    string num = s.Substring(colon + 1).Trim();
                    s = s.Substring(0, colon).Trim();
                    int n;
                    if (s.Length > 0 && int.TryParse(num, out n)) l.Amounts[s] = Math.Max(0, n);
                }
                if (s.Length == 0) continue;
                if (l.Named.Add(s)) l.Order.Add(s);
            }
            if (s_cache.Count > 64) s_cache.Clear();
            s_cache[raw] = l;
            return l;
        }

        // on the list (any of the names given: prefab name, in-game name...) and not excluded
        public bool Allows(params string[] names)
        {
            bool named = false;
            for (int i = 0; i < names.Length; i++)
            {
                string n = names[i];
                if (string.IsNullOrEmpty(n)) continue;
                if (Excluded.Contains(n)) return false;
                if (Named.Contains(n)) named = true;
            }
            return All || named;
        }

        public bool IsExcluded(params string[] names)
        {
            for (int i = 0; i < names.Length; i++) if (!string.IsNullOrEmpty(names[i]) && Excluded.Contains(names[i])) return true;
            return false;
        }

        // the amount set for this item with Name:Number, else the default
        public int AmountFor(string name, int dflt)
        {
            int n;
            return name != null && Amounts.TryGetValue(name, out n) ? n : dflt;
        }

        public bool HasAmount(string name) { return name != null && Amounts.ContainsKey(name); }

        // the in-game name of an item/creature token, for matching names typed as they appear in game
        public static string Local(string token)
        {
            if (string.IsNullOrEmpty(token)) return null;
            try { return Localization.instance != null ? Localization.instance.Localize(token) : null; } catch { return null; }
        }
    }
}
