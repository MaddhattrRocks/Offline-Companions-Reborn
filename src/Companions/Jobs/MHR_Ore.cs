using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Companions;

// 1.5.98 DD6 (test 13, John 2026-11-19: "We should only be checking for Ore veins"): what is ore is what the game's own
// coal-fired smelting stations take in (Smelter.m_conversion of each station whose fuel is Coal: the smelter and the blast
// furnace; the kiln, windmill, spinning wheel, eitr refinery and hot tub are Smelters too, but burn no coal), plus the
// pickaxe things John keeps in the Ore job: obsidian, chitin, black marble and soft tissue. A vein is a rock whose game drop
// table has one of them. Read once from the game's prefabs; the load log lists the ore items and every pickaxe-mineable
// thing in the game with its drops.
internal static class MHR_Ore
{
	private static readonly string[] Extra = { "Obsidian", "Chitin", "BlackMarble", "Softtissue" };

	private static HashSet<string> s_items;

	private static bool Ready()
	{
		if (s_items != null)
		{
			return true;
		}
		ZNetScene zs = ZNetScene.instance;
		if (zs == null || zs.m_prefabs == null || zs.m_prefabs.Count == 0)
		{
			return false;
		}
		HashSet<string> set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (GameObject go in zs.m_prefabs)
		{
			Smelter sm = go != null ? go.GetComponent<Smelter>() : null;
			if (sm == null || sm.m_fuelItem == null || sm.m_fuelItem.gameObject.name != "Coal" || sm.m_conversion == null)
			{
				continue;
			}
			foreach (Smelter.ItemConversion cv in sm.m_conversion)
			{
				if (cv != null && cv.m_from != null)
				{
					set.Add(cv.m_from.gameObject.name);
				}
			}
		}
		foreach (string e in Extra)
		{
			set.Add(e);
		}
		s_items = set;
		LogTypes(zs);
		return true;
	}

	internal static bool Drops(DropTable t)
	{
		if (t == null || t.m_drops == null || !Ready())
		{
			return false;
		}
		foreach (DropTable.DropData d in t.m_drops)
		{
			if (d.m_item != null && s_items.Contains(d.m_item.name))
			{
				return true;
			}
		}
		return false;
	}

	internal static bool IsVein(MineRock5 m)
	{
		return m != null && Drops(m.m_dropItems);
	}

	internal static bool IsVein(MineRock m)
	{
		return m != null && Drops(m.m_dropItems);
	}

	// a Destructible: its own drops, or the rock it turns into when broken (an unsplit copper deposit or silver vein
	// becomes its MineRock5 on the first hit)
	internal static bool IsVein(Destructible d)
	{
		if (d == null)
		{
			return false;
		}
		DropOnDestroyed dd = d.GetComponent<DropOnDestroyed>();
		if (dd != null && Drops(dd.m_dropWhenDestroyed))
		{
			return true;
		}
		GameObject s = d.m_spawnWhenDestroyed;
		return s != null && (IsVein(s.GetComponent<MineRock5>()) || IsVein(s.GetComponent<MineRock>()));
	}

	private static string DropNames(DropTable t)
	{
		if (t == null || t.m_drops == null)
		{
			return "";
		}
		List<string> n = new List<string>();
		foreach (DropTable.DropData d in t.m_drops)
		{
			if (d.m_item != null && !n.Contains(d.m_item.name))
			{
				n.Add(d.m_item.name);
			}
		}
		return string.Join(", ", n);
	}

	private static void LogTypes(ZNetScene zs)
	{
		if (!CompanionsPlugin.LogNormal)
		{
			return;
		}
		StringBuilder sb = new StringBuilder();
		sb.Append("[Ore] Ore items (what the game's coal-fired smelting stations take, plus ").Append(string.Join(", ", Extra)).Append("): ").Append(string.Join(", ", s_items));
		sb.Append("\n[Ore] Everything a pickaxe mines in this game (* = an ore vein for the Ore job):");
		foreach (GameObject go in zs.m_prefabs)
		{
			if (go == null)
			{
				continue;
			}
			MineRock5 m5 = go.GetComponent<MineRock5>();
			MineRock m = go.GetComponent<MineRock>();
			Destructible d = go.GetComponent<Destructible>();
			string line = null;
			bool vein = false;
			if (m5 != null)
			{
				vein = IsVein(m5);
				line = $"{go.name} (MineRock5 \"{m5.m_name}\"): {DropNames(m5.m_dropItems)}";
			}
			else if (m != null)
			{
				vein = IsVein(m);
				line = $"{go.name} (MineRock \"{m.m_name}\"): {DropNames(m.m_dropItems)}";
			}
			else if (d != null && d.m_damages.m_pickaxe != HitData.DamageModifier.Immune && (d.m_damages.m_chop == HitData.DamageModifier.Immune || d.m_damages.m_chop == HitData.DamageModifier.Ignore))
			{
				vein = IsVein(d);
				DropOnDestroyed dd = d.GetComponent<DropOnDestroyed>();
				line = $"{go.name} (Destructible): {(dd != null ? DropNames(dd.m_dropWhenDestroyed) : "")}{(d.m_spawnWhenDestroyed != null ? " -> becomes " + d.m_spawnWhenDestroyed.name : "")}";
			}
			if (line != null)
			{
				sb.Append("\n  ").Append(vein ? "* " : "  ").Append(line);
			}
		}
		CompanionsPlugin.Log.LogInfo(sb.ToString());
	}
}
