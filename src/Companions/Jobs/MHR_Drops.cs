using System;
using System.Collections.Generic;
using UnityEngine;

namespace Companions;

// 1.5.98 GG4 (John, 2026-11-19 18:51Z: "From now on everything should base the logic off what is dropped by the object"):
// what a thing is comes from what the game drops when it breaks - never from its name or its kind of object.
//   ore   = MHR_Ore's list (what the coal-fired smelting stations take, plus obsidian, chitin, black marble, soft tissue)
//   wood  = what the game's charcoal kiln takes (each Smelter whose output is Coal), plus Wood, FineWood, RoundLog,
//           ElderBark, YggdrasilWood and Blackwood
//   stone = Stone, Flint, Grausten
// The drops are gathered the way the game drops them: a tree's own table plus the log it falls into (and that log's
// pieces); a log's table plus its pieces; a Destructible's DropOnDestroyed table plus what it turns into
// (m_spawnWhenDestroyed); a rock's m_dropItems. A thing that drops more than one kind counts as ore first, then wood,
// then stone. Kept per game prefab id (F1: never per name).
internal static class MHR_Drops
{
	internal const int Unknown = 0, Wood = 1, Stone = 2, Ore = 3;

	private static readonly string[] WoodExtra = { "Wood", "FineWood", "RoundLog", "ElderBark", "YggdrasilWood", "Blackwood" };

	private static readonly string[] StoneItems = { "Stone", "Flint", "Grausten" };

	private static HashSet<string> s_wood;

	private static readonly HashSet<string> s_stone = new HashSet<string>(StoneItems, StringComparer.OrdinalIgnoreCase);

	// F1 (test 17): kept by the game's prefab id (the ZDO's prefab hash), never by the object's name - every cracked rock,
	// a vein or a plain stone one, is named "___MineRock5 m_meshFilter"
	private static readonly Dictionary<int, int> s_kind = new Dictionary<int, int>();

	private static readonly Dictionary<int, string> s_names = new Dictionary<int, string>();

	// F1: the game's prefab id of the thing (0 = it has no saved game data: worked out every time, not kept)
	private static int PrefabId(GameObject go)
	{
		ZNetView nv = go.GetComponent<ZNetView>() ?? go.GetComponentInParent<ZNetView>();
		ZDO z = nv != null && nv.IsValid() ? nv.GetZDO() : null;
		return z != null ? z.GetPrefab() : 0;
	}

	private static string s_lastNames;

	private static bool WoodReady()
	{
		if (s_wood != null)
		{
			return true;
		}
		ZNetScene zs = ZNetScene.instance;
		if (zs == null || zs.m_prefabs == null || zs.m_prefabs.Count == 0)
		{
			return false;
		}
		HashSet<string> set = new HashSet<string>(WoodExtra, StringComparer.OrdinalIgnoreCase);
		foreach (GameObject go in zs.m_prefabs)
		{
			Smelter sm = go != null ? go.GetComponent<Smelter>() : null;
			if (sm == null || sm.m_conversion == null)
			{
				continue;
			}
			foreach (Smelter.ItemConversion cv in sm.m_conversion)
			{
				if (cv != null && cv.m_from != null && cv.m_to != null && cv.m_to.gameObject.name == "Coal")
				{
					set.Add(cv.m_from.gameObject.name);
				}
			}
		}
		s_wood = set;
		if (CompanionsPlugin.LogNormal) CompanionsPlugin.Log.LogInfo("[Drops] Wood items (what the game's charcoal kiln takes, plus " + string.Join(", ", WoodExtra) + "): " + string.Join(", ", s_wood) + "; stone items: " + string.Join(", ", StoneItems));
		return true;
	}

	// the object that breaks: the first tree, log, rock or Destructible from this collider up
	internal static GameObject Root(Collider c)
	{
		if (c == null)
		{
			return null;
		}
		Component k = (Component)c.GetComponentInParent<TreeBase>() ?? (Component)c.GetComponentInParent<TreeLog>() ?? (Component)c.GetComponentInParent<MineRock5>() ?? (Component)c.GetComponentInParent<MineRock>() ?? c.GetComponentInParent<Destructible>();
		return k != null ? k.gameObject : null;
	}

	// what it is by its drops (Unknown = drops none of them, or the game's prefabs aren't loaded yet)
	internal static int KindOf(GameObject go)
	{
		if (go == null || !WoodReady())
		{
			return Unknown;
		}
		int key = PrefabId(go);
		if (key != 0 && s_kind.TryGetValue(key, out int k))
		{
			return k;
		}
		List<GameObject> items = new List<GameObject>();
		Gather(go, items, 0);
		bool ore = false, wood = false, stone = false;
		List<string> names = new List<string>();
		foreach (GameObject it in items)
		{
			if (it == null)
			{
				continue;
			}
			string n = it.name;
			if (!names.Contains(n))
			{
				names.Add(n);
			}
			ore |= MHR_Ore.IsOre(n);
			wood |= s_wood.Contains(n);
			stone |= s_stone.Contains(n);
		}
		k = ore ? Ore : wood ? Wood : stone ? Stone : Unknown;
		s_lastNames = names.Count > 0 ? string.Join(", ", names) : "nothing";
		if (key != 0)
		{
			s_kind[key] = k;
			s_names[key] = s_lastNames;
		}
		return k;
	}

	// the drops as KindOf found them (for the log)
	internal static string DropList(GameObject go)
	{
		if (go == null)
		{
			return "?";
		}
		int key = PrefabId(go);
		if (key != 0 && s_names.TryGetValue(key, out string s))
		{
			return s;
		}
		s_lastNames = null;
		KindOf(go);
		return s_lastNames ?? "?";
	}

	internal static string KindName(int k)
	{
		return k switch
		{
			Ore => "ore",
			Wood => "wood",
			Stone => "stone",
			_ => "unknown",
		};
	}

	// the name the game shows when you point at it (its hover text, e.g. "Fir"); the prefab name when it has none
	internal static string Tooltip(GameObject go)
	{
		if (go == null)
		{
			return "?";
		}
		Hoverable h = go.GetComponentInParent<Hoverable>() ?? go.GetComponentInChildren<Hoverable>();
		string s = null;
		try
		{
			s = h?.GetHoverName();
		}
		catch (Exception)
		{
			s = null;
		}
		return string.IsNullOrEmpty(s) ? Utils.GetPrefabName(go) : s;
	}

	private static void Gather(GameObject go, List<GameObject> items, int depth)
	{
		if (go == null || depth > 3)
		{
			return;
		}
		TreeBase tb = go.GetComponent<TreeBase>();
		if (tb != null)
		{
			Add(tb.m_dropWhenDestroyed, items);
			Gather(tb.m_logPrefab, items, depth + 1);
		}
		TreeLog tl = go.GetComponent<TreeLog>();
		if (tl != null)
		{
			Add(tl.m_dropWhenDestroyed, items);
			Gather(tl.m_subLogPrefab, items, depth + 1);
		}
		MineRock5 m5 = go.GetComponent<MineRock5>();
		if (m5 != null)
		{
			Add(m5.m_dropItems, items);
		}
		MineRock m = go.GetComponent<MineRock>();
		if (m != null)
		{
			Add(m.m_dropItems, items);
		}
		Destructible d = go.GetComponent<Destructible>();
		if (d != null)
		{
			DropOnDestroyed dd = go.GetComponent<DropOnDestroyed>();
			if (dd != null)
			{
				Add(dd.m_dropWhenDestroyed, items);
			}
			Gather(d.m_spawnWhenDestroyed, items, depth + 1);
		}
	}

	private static void Add(DropTable t, List<GameObject> items)
	{
		if (t == null || t.m_drops == null)
		{
			return;
		}
		foreach (DropTable.DropData dd in t.m_drops)
		{
			if (dd.m_item != null)
			{
				items.Add(dd.m_item);
			}
		}
	}

	// the game's damage modifiers for the tool's damage type: Immune / Ignore = that tool does nothing to it
	internal static bool ToolHurts(GameObject go, bool axe)
	{
		if (go == null)
		{
			return false;
		}
		HitData.DamageModifiers mods;
		TreeBase tb = go.GetComponent<TreeBase>();
		TreeLog tl = go.GetComponent<TreeLog>();
		MineRock5 m5 = go.GetComponent<MineRock5>();
		MineRock m = go.GetComponent<MineRock>();
		Destructible d = go.GetComponent<Destructible>();
		if (tb != null)
		{
			mods = tb.m_damageModifiers;
		}
		else if (tl != null)
		{
			mods = tl.m_damages;
		}
		else if (m5 != null)
		{
			mods = m5.m_damageModifiers;
		}
		else if (m != null)
		{
			mods = m.m_damageModifiers;
		}
		else if (d != null)
		{
			mods = d.m_damages;
		}
		else
		{
			return false;
		}
		HitData.DamageModifier mod = axe ? mods.m_chop : mods.m_pickaxe;
		return mod != HitData.DamageModifier.Immune && mod != HitData.DamageModifier.Ignore;
	}

	// the thing's health where the game keeps it (ZDO s_health: trees, logs, Destructibles); -1 = not kept there
	internal static float Health(GameObject go)
	{
		ZDO z = go != null ? go.GetComponent<ZNetView>()?.GetZDO() : null;
		if (z == null)
		{
			return -1f;
		}
		TreeLog log = go.GetComponent<TreeLog>();
		if (log != null)
		{
			return z.GetFloat(ZDOVars.s_health, log.m_health);
		}
		TreeBase tree = go.GetComponent<TreeBase>();
		if (tree != null)
		{
			return z.GetFloat(ZDOVars.s_health, tree.m_health);
		}
		Destructible d = go.GetComponent<Destructible>();
		if (d != null)
		{
			return z.GetFloat(ZDOVars.s_health, d.m_health);
		}
		return -1f;
	}
}
