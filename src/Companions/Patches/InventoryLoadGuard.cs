using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace Companions;

// 1.5.97: Inventory.Load clears the bag and adds the items back one by one, and every AddItem fires the bag's change
// callback. CompanionSetup's "take off anything that isn't in the bag" check ran on those, before the equipped tool's
// reloaded copy was back, and took the Ore job's pickaxe off at load (second 1.5.97 vein test). While a bag is loading
// that check waits; the game's own Changed() at the end of Load runs it once with the whole bag there.
[HarmonyPatch]
internal static class InventoryLoadGuard
{
	internal static Inventory Loading;

	private static IEnumerable<MethodBase> TargetMethods()
	{
		foreach (MethodInfo m in typeof(Inventory).GetMethods(BindingFlags.Instance | BindingFlags.Public))
		{
			if (m.Name == "Load")
			{
				ParameterInfo[] p = m.GetParameters();
				if (p.Length >= 1 && p[0].ParameterType == typeof(ZPackage))
				{
					yield return m;
				}
			}
		}
	}

	private static void Prefix(Inventory __instance, out Inventory __state)
	{
		__state = Loading;
		Loading = __instance;
	}

	// a finalizer so an exception inside Load can't leave the flag set
	private static void Finalizer(Inventory __state)
	{
		Loading = __state;
	}
}
