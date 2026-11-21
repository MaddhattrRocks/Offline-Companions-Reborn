using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Companions;

// 1.5.96: reads a split vein's parts (MineRock5.m_hitAreas, the private nested HitArea and its BoundData). These are
// private in the game, so they are read by reflection, the same way the mod reads other private game fields.
internal static class MHR_Vein
{
	internal struct Area
	{
		public Collider Collider;

		public float Health;

		public Vector3 BoundPos;

		public Vector3 BoundSize;
	}

	private static bool s_init;

	private static bool s_ok;

	private static FieldInfo s_hitAreas;

	private static FieldInfo s_haveSetupBounds;

	private static MethodInfo s_loadHealth;

	private static FieldInfo s_collider;

	private static FieldInfo s_health;

	private static FieldInfo s_bound;

	private static FieldInfo s_pos;

	private static FieldInfo s_size;

	private static void Init()
	{
		if (s_init)
		{
			return;
		}
		s_init = true;
		try
		{
			s_hitAreas = AccessTools.Field(typeof(MineRock5), "m_hitAreas");
			s_haveSetupBounds = AccessTools.Field(typeof(MineRock5), "m_haveSetupBounds");
			s_loadHealth = AccessTools.Method(typeof(MineRock5), "LoadHealth", Type.EmptyTypes);
			Type ha = AccessTools.Inner(typeof(MineRock5), "HitArea");
			Type bd = AccessTools.Inner(typeof(MineRock5), "BoundData");
			if (ha != null)
			{
				s_collider = AccessTools.Field(ha, "m_collider");
				s_health = AccessTools.Field(ha, "m_health");
				s_bound = AccessTools.Field(ha, "m_bound");
			}
			if (bd != null)
			{
				s_pos = AccessTools.Field(bd, "m_pos");
				s_size = AccessTools.Field(bd, "m_size");
			}
			s_ok = s_hitAreas != null && s_collider != null && s_health != null;
			if (!s_ok)
			{
				CompanionsPlugin.Log?.LogWarning("[Mining] Can't read the vein's parts (game changed?)");
			}
		}
		catch (Exception e)
		{
			s_ok = false;
			CompanionsPlugin.Log?.LogWarning("[Mining] Can't read the vein's parts: " + e.Message);
		}
	}

	// the vein's parts as the game holds them now (loadHealth: reload their health first, as the scan always did); false when they can't be read
	internal static bool Areas(MineRock5 rock, List<Area> into, out bool haveBounds, bool loadHealth = true)
	{
		into.Clear();
		haveBounds = false;
		Init();
		if (!s_ok || rock == null)
		{
			return false;
		}
		if (loadHealth)
		{
			try
			{
				s_loadHealth?.Invoke(rock, null);
			}
			catch
			{
			}
		}
		if (!(s_hitAreas.GetValue(rock) is IList list))
		{
			return false;
		}
		haveBounds = s_haveSetupBounds != null && s_bound != null && s_pos != null && s_size != null && (bool)s_haveSetupBounds.GetValue(rock);
		foreach (object o in list)
		{
			if (o == null)
			{
				continue;
			}
			Area a = default(Area);
			a.Collider = s_collider.GetValue(o) as Collider;
			a.Health = (float)s_health.GetValue(o);
			if (haveBounds)
			{
				object b = s_bound.GetValue(o);
				if (b != null)
				{
					a.BoundPos = (Vector3)s_pos.GetValue(b);
					a.BoundSize = (Vector3)s_size.GetValue(b);
				}
			}
			into.Add(a);
		}
		return true;
	}

	private static readonly List<Area> s_tmp = new List<Area>();

	// true while any part is still standing; null when the parts can't be read
	internal static bool? AnyLeft(MineRock5 rock)
	{
		if (!Areas(rock, s_tmp, out _, loadHealth: false))
		{
			return null;
		}
		foreach (Area a in s_tmp)
		{
			if (a.Health > 0f && a.Collider != null && a.Collider.gameObject.activeSelf)
			{
				return true;
			}
		}
		return false;
	}
}
