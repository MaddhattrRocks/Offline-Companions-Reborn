using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Companions;

// 1.5.94: (user: FPS "very choppy" even at 70; the [Perf] log showed one companion update taking 11-24 ms every 10 s)
// Shared lists of the tombstones, fires, bodies and beds in the loaded world, kept by watching each one's Awake, instead
// of searching the whole scene (Object.FindObjectsByType) - each companion did that for tombstones every 5 s, the fire
// checks every 2 s, the loot code every second. Destroyed objects drop out the next time a list is read. Ladders have no
// Awake of their own, so their scene search is shared and kept 5 s.
internal static class MHR_World
{
	private static readonly List<TombStone> s_tombs = new List<TombStone>();

	private static readonly List<Fireplace> s_fires = new List<Fireplace>();

	private static readonly List<Ragdoll> s_bodies = new List<Ragdoll>();

	private static readonly List<Bed> s_beds = new List<Bed>();

	private static readonly List<Ladder> s_ladders = new List<Ladder>();

	private static float s_laddersAt = -100f;

	internal static void Add<T>(List<T> list, T item) where T : Component
	{
		if (item != null)
		{
			list.Add(item);   // Awake runs once per object
		}
	}

	// the live ones (destroyed or inactive ones removed); callers must not add or remove while looping
	private static List<T> Live<T>(List<T> list) where T : Component
	{
		for (int i = list.Count - 1; i >= 0; i--)
		{
			T t = list[i];
			if (t == null)
			{
				list.RemoveAt(i);
			}
		}
		return list;
	}

	private static bool On(Component c)
	{
		return c != null && c.gameObject.activeInHierarchy;
	}

	internal static List<TombStone> Tombstones()
	{
		return Live(s_tombs);
	}

	internal static List<Fireplace> Fires()
	{
		return Live(s_fires);
	}

	internal static List<Ragdoll> Bodies()
	{
		return Live(s_bodies);
	}

	internal static List<Bed> Beds()
	{
		return Live(s_beds);
	}

	internal static List<Ladder> Ladders()
	{
		if (Time.time - s_laddersAt > 5f)
		{
			s_laddersAt = Time.time;
			s_ladders.Clear();
			s_ladders.AddRange(Object.FindObjectsByType<Ladder>(FindObjectsSortMode.None));
		}
		return Live(s_ladders);
	}

	internal static bool Active(Component c)
	{
		return On(c);
	}

	[HarmonyPatch(typeof(TombStone), "Awake")]
	private static class TombAwake
	{
		private static void Postfix(TombStone __instance)
		{
			Add(s_tombs, __instance);
		}
	}

	[HarmonyPatch(typeof(Fireplace), "Awake")]
	private static class FireAwake
	{
		private static void Postfix(Fireplace __instance)
		{
			Add(s_fires, __instance);
		}
	}

	[HarmonyPatch(typeof(Ragdoll), "Awake")]
	private static class BodyAwake
	{
		private static void Postfix(Ragdoll __instance)
		{
			Add(s_bodies, __instance);
		}
	}

	[HarmonyPatch(typeof(Bed), "Awake")]
	private static class BedAwake
	{
		private static void Postfix(Bed __instance)
		{
			Add(s_beds, __instance);
		}
	}
}
