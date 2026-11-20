using System.Collections.Generic;
using UnityEngine;

namespace Companions;

// 1.5.98 FF3 (John, test 14, 2026-11-19): too heavy with ore on a vein. Its stone and wood are tossed first (G8, the toss
// in MHR_GatherRules.Yard.cs). Then:
//   1. still over what it can carry (encumbered): it drops only as much ore as takes it to that weight, with the game's
//      player drop (the game's own auto-pickup flag on the item, nothing else);
//   (F5, John 2026-11-20 15:38Z: "just have them come back for any ore that puts them over the weight limit") the dropped
//      ore stays where it fell - no rest and no pick-back; the pit's shared list of loose items picks it up after the pop;
//   4. it walks to the vein's one ore dump on the trench floor (the Yard ore trip) and drops its ore there: all of it when
//      there is no player-built storage with room within 20 m; with storage, only enough to get under its weight limit,
//      then it takes the rest out to the storage and comes back.
// Q2: after the vein pops, with storage, each companion carries what is on the ore dump to the storage, up to its own
// limit a trip, until the dump is empty or the storage is full (MHR_GatherRules.Yard.cs).
public partial class HarvestController
{
	private readonly List<ItemDrop> _hvDrops = new List<ItemDrop>();

	// what "under its weight limit" is: under the job's too-heavy weight and not over what it can carry
	internal float HeavyLimit => Mathf.Min(CompanionTierData.CarryLimit(this), OverweightThreshold - 0.01f);

	// the gate's next step after the toss: true = the steps above started (or the ore trip)
	private bool OvHeavyStart()
	{
		if (_ovOut || _humanoid == null || _humanoid.GetInventory() == null || !MHR_GatherRules.HasOre(_humanoid))
		{
			return false;
		}
		Inventory inv = _humanoid.GetInventory();
		float carry = CompanionTierData.CarryLimit(this);
		float w = inv.GetTotalWeight();
		if (w > carry)
		{
			_hvDrops.Clear();
			int n = MHR_GatherRules.DropOreTo(_humanoid, carry, _hvDrops);
			if (_hvDrops.Count > 0 && CompanionsPlugin.LogNormal)
			{
				LogInfo($"Over what it can carry ({w:F0} of {carry:F0}) - dropped {n} ore to get to it; it comes back for it after the pop (F5)");
			}
			_hvDrops.Clear();
		}
		return OvHeavyDump();
	}

	private bool OvHeavyDump()
	{
		if (!OvDumpSpot(out Vector3 dump))
		{
			return false;
		}
		Bounds box = _vp != null ? _vp.Box : _vBox;
		MHR_GatherRules.StartPitDump(this, dump, box.center);
		return true;
	}

	// K9: storage for the ore dump - a chest (cart, ship) within 30 m of the vein's box
	private const float OvDumpChest = 30f;

	// Q1: the vein's one ore dump - the trench floor at the trench spot nearest the first companion that needs it.
	// K9: if player-built storage with room (MHR_GatherRules.UsableChest, the checks the drop-off uses) is within 30 m of
	// the vein's box, the dump is instead the trench spot nearest that storage (the nearest such to the vein), which puts
	// it within the ore drop's 20 m chest check (FF3). The log names the storage used and why each other one within 30 m
	// wasn't.
	private bool OvDumpSpot(out Vector3 dump)
	{
		dump = Vector3.zero;
		OvPlan p = _vp;
		if (p == null)
		{
			return false;
		}
		if (!p.DumpHas)
		{
			Container chest = null;
			float bd = float.MaxValue;
			List<string> notes = new List<string>();
			List<Container> near = MHR_Chests.Near(p.Box.center, OvDumpChest + Mathf.Max(p.Box.extents.x, p.Box.extents.z));
			foreach (Container c in near)
			{
				if (c == null)
				{
					continue;
				}
				Vector3 cp = c.transform.position;
				float d = Vector3.Distance(p.Box.ClosestPoint(cp), cp);
				if (d > OvDumpChest)
				{
					continue;
				}
				string no = MHR_GatherRules.ChestNo(this, c);
				if (no != null)
				{
					notes.Add($"\"{c.m_name}\" at {MHR_Mover.Fmt(cp)} ({d:F0} m): {no}");
					continue;
				}
				if (d < bd)
				{
					if (chest != null)
					{
						notes.Add($"\"{chest.m_name}\" at {MHR_Mover.Fmt(chest.transform.position)} ({bd:F0} m): farther from the vein");
					}
					bd = d;
					chest = c;
				}
				else
				{
					notes.Add($"\"{c.m_name}\" at {MHR_Mover.Fmt(cp)} ({d:F0} m): farther from the vein");
				}
			}
			OvSpot q = OvNearest(chest != null ? chest.transform.position : base.transform.position);
			if (q == null)
			{
				return false;
			}
			p.Dump = new Vector3(q.P.x, GroundAt(q.P), q.P.z);
			p.DumpHas = true;
			if (CompanionsPlugin.LogNormal) LogInfo($"The ore dump for \"{p.Name}\": trench spot {q.Idx + 1} at {MHR_Mover.Fmt(p.Dump)} - " + (chest != null ? $"the trench spot nearest \"{chest.m_name}\" at {MHR_Mover.Fmt(chest.transform.position)} ({bd:F0} m from the vein, {Vector3.Distance(p.Dump, chest.transform.position):F0} m from the dump)" : $"no usable player-built storage within {OvDumpChest:F0} m of the vein, so the spot nearest it") + (notes.Count > 0 ? "; not used: " + string.Join("; ", notes) : ""));
		}
		dump = p.Dump;
		dump.y = GroundAt(dump);
		return true;
	}

	// the ore spot for MHR_GatherRules: the vein's ore dump (on the vein: made if needed; off it: a kept plan's dump within
	// 25 m, flat)
	internal bool PitDumpFor(out Vector3 dump)
	{
		if (_vp != null)
		{
			return OvDumpSpot(out dump);
		}
		return PitDumpNear(base.transform.position, out dump);
	}

	internal static bool PitDumpNear(Vector3 at, out Vector3 dump)
	{
		dump = Vector3.zero;
		float bd = 25f;
		bool any = false;
		foreach (OvPlan p in s_ovPlans)
		{
			if (!p.DumpHas)
			{
				continue;
			}
			float d = Flat(at, p.Dump);
			if (d <= bd)
			{
				bd = d;
				dump = p.Dump;
				any = true;
			}
		}
		if (any)
		{
			dump.y = GroundAt(dump);
		}
		return any;
	}

	// T3 (John 2026-11-20: "They should head out when they hit 90+ percent capacity"): on its vein with a player-built
	// chest (not a cart or ship) with room for its ore within 20 m of the pit (the trench's outer edge = the furthest trench
	// spot + OvThingRadius from the vein's centre), at 90% or more of what it can carry, after its current swing: out by
	// the nearest way out (OvLeaveOut / OvGetOut, extended with T4 or cut if needed), only the ore into that chest
	// (OreDue -> OreLoad), then it takes the vein again. No such chest = it keeps working (the ore dump at the weight
	// limit, as before). Checked every 5 s at most while at 90%; 60 s between trips so a failed deposit can't loop.
	private const float OvChestNear = 20f, OvChestAt = 0.9f, OvChestEvery = 5f, OvChestCool = 60f;
	private float _ovChestNext;

	private bool OvChestTrip()
	{
		if (_vp == null || _vFor == null || _ovOut || _humanoid == null || _humanoid.InAttack() || _vp.Spots.Count == 0)
		{
			return false;
		}
		float now = Time.time;
		if (now < _ovChestNext)
		{
			return false;
		}
		Inventory inv = _humanoid.GetInventory();
		if (inv == null || inv.GetTotalWeight() < CompanionTierData.CarryLimit(this) * OvChestAt)
		{
			return false;
		}
		ItemDrop.ItemData ore = null;
		foreach (ItemDrop.ItemData it in inv.GetAllItems())
		{
			if (MHR_GatherRules.IsOreItem(it))
			{
				ore = it;
				break;
			}
		}
		if (ore == null)
		{
			return false;
		}
		_ovChestNext = now + OvChestEvery;
		float r = 0f;
		foreach (OvSpot sp in _vp.Spots)
		{
			r = Mathf.Max(r, Flat(sp.P, _vp.Center));
		}
		r += OvThingRadius;
		Container best = null;
		float bd = float.MaxValue;
		foreach (Container c in MHR_Chests.Near(_vp.Center, r + OvChestNear))
		{
			if (c == null || c.GetComponentInParent<Vagon>() != null || c.GetComponentInParent<Ship>() != null)
			{
				continue;
			}
			if (Flat(c.transform.position, _vp.Center) - r > OvChestNear)
			{
				continue;
			}
			if (!MHR_GatherRules.UsableChest(this, c) || !c.GetInventory().CanAddItem(ore, 1))
			{
				continue;
			}
			float d = Flat(base.transform.position, c.transform.position);
			if (d < bd)
			{
				bd = d;
				best = c;
			}
		}
		if (best == null || !MHR_GatherRules.OreToChest(this, best))
		{
			return false;
		}
		_ovChestNext = now + OvChestCool;
		if (CompanionsPlugin.LogNormal) LogInfo($"At {inv.GetTotalWeight():F0} of {CompanionTierData.CarryLimit(this):F0} carry - taking its ore to \"{best.m_name}\" {bd:F0}m away, then back to the vein");
		return true;
	}

	// 4. with storage: out of the trench toward it; once off the vein the ore goes in (MHR_GatherRules.OreDue)
	internal void OvToStorage(Container c)
	{
		if (c == null)
		{
			return;
		}
		OvLeaveOut(c.transform.position, "taking its ore to \"" + c.m_name + "\"");
	}
}
