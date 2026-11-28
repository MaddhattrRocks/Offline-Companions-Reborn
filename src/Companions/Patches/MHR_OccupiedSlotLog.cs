using System.Diagnostics;
using System.Text;
using HarmonyLib;

namespace Companions;

// 1.5.96: "Trying to add item to occupied slot -1, -1" is the game's warning when something adds an item to a bag with no
// free slot (Inventory.AddItem returns false). Detailed log only: whose bag, what item, and which code tried it - to find
// the cause before changing anything (John's log, 10:25: 4 of these while managing companion bags, nothing went missing).
[HarmonyPatch]
internal static class MHR_OccupiedSlotLog
{
	[HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem), new System.Type[] { typeof(ItemDrop.ItemData) })]
	[HarmonyPostfix]
	private static void AddItem(Inventory __instance, ItemDrop.ItemData item, bool __result)
	{
		if (!__result && CompanionsPlugin.LogDetailed) Report(__instance, item);
	}

	[HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem), new System.Type[] { typeof(ItemDrop.ItemData), typeof(Vector2i) })]
	[HarmonyPostfix]
	private static void AddItemAt(Inventory __instance, ItemDrop.ItemData item, bool __result)
	{
		if (!__result && CompanionsPlugin.LogDetailed) Report(__instance, item);
	}

	private static void Report(Inventory inv, ItemDrop.ItemData item)
	{
		try
		{
			string whose = "a bag";
			if (Player.m_localPlayer != null && Player.m_localPlayer.GetInventory() == inv)
			{
				whose = "your bag";
			}
			else
			{
				foreach (CompanionSetup cs in CompanionSetup.AllCompanions)
				{
					if (cs == null) continue;
					Humanoid hum = cs.GetComponent<Humanoid>();
					Container box = cs.GetComponent<Container>();
					if ((hum != null && hum.GetInventory() == inv) || (box != null && box.GetInventory() == inv))
					{
						Character ch = cs.GetComponent<Character>();
						whose = "companion " + (ch != null ? ch.m_name : cs.name) + "'s bag";
						break;
					}
				}
			}
			StringBuilder sb = new StringBuilder();
			StackFrame[] frames = new StackTrace(2, false).GetFrames();
			int n = 0;
			if (frames != null)
			{
				for (int i = 0; i < frames.Length && n < 5; i++)
				{
					System.Reflection.MethodBase m = frames[i].GetMethod();
					if (m == null || m.DeclaringType == null) continue;
					string t = m.DeclaringType.FullName ?? m.DeclaringType.Name;
					if (t.StartsWith("HarmonyLib") || t.Contains("MHR_OccupiedSlotLog")) continue;
					if (n > 0) sb.Append(" <- ");
					sb.Append(m.DeclaringType.Name).Append('.').Append(m.Name);
					n++;
				}
			}
			string what = item?.m_shared != null ? item.m_shared.m_name + " x" + item.m_stack : "?";
			CompanionsPlugin.Log.LogDebug("[Inventory] No free slot in " + whose + " (" + (inv != null ? inv.NrOfItems() + "/" + (inv.GetWidth() * inv.GetHeight()) : "?") + " slots used) for " + what + " - called from " + sb);
		}
		catch
		{
		}
	}
}
