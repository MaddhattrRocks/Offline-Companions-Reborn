using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Companions;

internal static class WaterSafety
{

	private static PropertyInfo _staminaProp;

	private static PropertyInfo _maxStaminaProp;

	private static Hashtable _shoreTarget = new Hashtable();

	private static Hashtable _shoreRefresh = new Hashtable();

	private static Hashtable _shoreStartTime = new Hashtable();

	private static int _logs;

	// 1.5.98 (swim items 2 + 3)
	private static readonly Dictionary<int, float> _offUntil = new Dictionary<int, float>();

	private static readonly Dictionary<int, int> _jumps = new Dictionary<int, int>();

	private static readonly Dictionary<int, float> _nextJump = new Dictionary<int, float>();

	private static readonly Dictionary<int, List<KeyValuePair<Vector3, float>>> _cool = new Dictionary<int, List<KeyValuePair<Vector3, float>>>();

	// a spot within 1.5 m of one that had its 2 tries in the last 60 s
	private static bool Cooling(int id, Vector3 p, float now)
	{
		if (!_cool.TryGetValue(id, out var list))
		{
			return false;
		}
		list.RemoveAll((KeyValuePair<Vector3, float> e) => e.Value <= now);
		foreach (KeyValuePair<Vector3, float> e in list)
		{
			if (Vector3.Distance(e.Key, p) < 1.5f)
			{
				return true;
			}
		}
		return false;
	}

	private static string Fmt(Vector3 v)
	{
		return "(" + v.x.ToString("F1") + ", " + v.y.ToString("F1") + ", " + v.z.ToString("F1") + ")";
	}

	private const float MaxSeekSeconds = 25f;

	private static FieldInfo _characterField;

	// 1.4.150: heading for the shore to get out of the water - jobs wait (they pulled it back out and it could drown)
	internal static bool Active(Component c)
	{
		Character ch = c != null ? c.GetComponent<Character>() : null;
		return ch != null && _shoreTarget.ContainsKey(ch.GetInstanceID());
	}

	// 1.5.85: forgets the shore it was heading for (aboard a ship - log ce2af5bd: "stamina low while swimming" right after it sat)
	internal static void Clear(Character ch)
	{
		if (ch == null)
		{
			return;
		}
		int id = ch.GetInstanceID();
		_shoreTarget.Remove(id);
		_shoreRefresh.Remove(id);
		_shoreStartTime.Remove(id);
		_jumps.Remove(id);
		_nextJump.Remove(id);
	}

	internal static bool Tick(BaseAI ai, float dt)
	{
		try
		{
			if (ai == null)
			{
				return false;
			}
			if (_characterField == null)
			{
				_characterField = typeof(BaseAI).GetField("m_character", BindingFlags.Instance | BindingFlags.NonPublic);
			}
			Character character = ((!(_characterField == null)) ? ((Character)_characterField.GetValue(ai)) : null);
			if (character == null)
			{
				return false;
			}
			int instanceID = character.GetInstanceID();
			// 1.5.85: aboard a ship (seated, or standing on its deck) - nothing to swim for
			CompanionAI onShip = ai as CompanionAI;
			if ((onShip != null && onShip.IsOnShip) || MHR_Board.ShipUnder(character) != null)
			{
				Clear(character);
				return false;
			}
			// 1.5.84: boarding with a ship's ladder close by - keep on for the ladder (treading water costs no stamina)
			if (MHR_Board.KeepSwimming(character))
			{
				if (_shoreTarget.ContainsKey(instanceID))
				{
					_shoreTarget.Remove(instanceID);
					_shoreRefresh.Remove(instanceID);
					_shoreStartTime.Remove(instanceID);
				}
				return false;
			}
			// 1.5.98 (swim items 2 + 3): heads for a way out (MHR_Water.FindWayOut: anywhere the swim ends, or within one jump,
			// nothing in between at the surface) instead of the terrain-only "shore" (it looked through walls: in a sunken tower it
			// aimed at the swamp outside, and re-armed the update after its 25 s, so drowning, recall and follow never ran).
			// After 25 s, or with no way out, it lets go for 30 s. At the spot: the game's water jump, 2 tries, then that spot
			// waits 60 s and the next is taken.
			float now = Time.time;
			bool flag = _shoreTarget.ContainsKey(instanceID);
			if (!character.IsSwimming())
			{
				if (flag)
				{
					Clear(character);
				}
				return false;
			}
			if (_offUntil.TryGetValue(instanceID, out var off) && now < off)
			{
				return false;
			}
			if (flag)
			{
				float num = ((!_shoreStartTime.ContainsKey(instanceID)) ? now : ((float)_shoreStartTime[instanceID]));
				if (now - num > 25f)
				{
					Clear(character);
					_offUntil[instanceID] = now + 30f;
					if (CompanionsPlugin.LogNormal) Log("[WaterSafety] \"" + character.m_name + "\" didn't get out of the water in 25 s - letting go for 30 s (follow, recall and jobs run)");
					return false;
				}
			}
			else
			{
				Component component = character.GetComponent("CompanionStamina");
				if (component == null)
				{
					return false;
				}
				if (StaminaPct(component) > ThresholdPct())
				{
					return false;
				}
			}
			CompanionAI cai = ai as CompanionAI;
			if (cai == null)
			{
				return false;
			}
			Vector3 position = character.transform.position;
			float num2 = ((!_shoreRefresh.ContainsKey(instanceID)) ? 999f : (now - (float)_shoreRefresh[instanceID]));
			if (!flag || num2 > 3f)
			{
				if (MHR_Water.FindWayOut(character, position, character.GetLiquidLevel(), (Vector3 p) => Cooling(instanceID, p, now), out var spot, out var why))
				{
					if (flag && Vector3.Distance((Vector3)_shoreTarget[instanceID], spot) > 1.5f)
					{
						_jumps[instanceID] = 0;
					}
					_shoreTarget[instanceID] = spot;
					_shoreRefresh[instanceID] = now;
					if (!flag)
					{
						_shoreStartTime[instanceID] = now;
						_jumps[instanceID] = 0;
						if (CompanionsPlugin.LogNormal) Log("[WaterSafety] \"" + character.m_name + "\" stamina low while swimming - heading for a way out at " + Fmt(spot) + " (" + Vector3.Distance(position, spot).ToString("F1") + " m)");
					}
				}
				else
				{
					Clear(character);
					_offUntil[instanceID] = now + 30f;
					if (CompanionsPlugin.LogNormal) Log("[WaterSafety] \"" + character.m_name + "\" found no way out of the water at " + Fmt(position) + " - letting go for 30 s (" + why + ")");
					return false;
				}
			}
			Vector3 vector = (Vector3)_shoreTarget[instanceID];
			Vector3 d = vector - position;
			d.y = 0f;
			float flat = d.magnitude;
			Vector3 dir = flat > 0.05f ? d / flat : ai.transform.forward;
			if (flat <= 1.5f && now >= (_nextJump.TryGetValue(instanceID, out var nj) ? nj : 0f))
			{
				int jumps = _jumps.TryGetValue(instanceID, out var jn) ? jn : 0;
				if (jumps >= 2)
				{
					List<KeyValuePair<Vector3, float>> list;
					if (!_cool.TryGetValue(instanceID, out list))
					{
						list = new List<KeyValuePair<Vector3, float>>();
						_cool[instanceID] = list;
					}
					list.Add(new KeyValuePair<Vector3, float>(vector, now + 60f));
					_jumps[instanceID] = 0;
					_shoreRefresh[instanceID] = -999f;   // a new spot on the next update
					if (CompanionsPlugin.LogDetailed) CompanionsPlugin.Log.LogDebug("[WaterSafety] \"" + character.m_name + "\" 2 tries at " + Fmt(vector) + " didn't end the swim - that spot waits 60 s");
					cai.PushDirection(dir, run: true);
					return true;
				}
				MHR_Mover.WaterJump(cai, character, dir);
				_jumps[instanceID] = jumps + 1;
				_nextJump[instanceID] = now + 1.5f;
				if (CompanionsPlugin.LogDetailed) CompanionsPlugin.Log.LogDebug("[WaterSafety] \"" + character.m_name + "\" at the way out " + Fmt(vector) + " - jumping out (try " + (jumps + 1) + ")");
			}
			cai.PushDirection(dir, run: true);
			return true;
		}
		catch (Exception ex)
		{
			if (CompanionsPlugin.LogNormal) Log("[WaterSafety] error: " + ex.Message);
			return false;
		}
	}

	internal static bool ShouldAvoidChase(Character self, Character target)
	{
		//Discarded unreachable code: IL_0008, IL_004d
		return MHR_Threat.AvoidChase(self, target);
	}

	private static void Log(string s)
	{
		if (_logs < 60)
		{
			_logs++;
			if (CompanionsPlugin.LogNormal) CompanionsPlugin.Log?.LogInfo(s);
		}
	}

	private static float ThresholdPct()
	{
		try
		{
			if (ModConfig.WaterStaminaFleeThreshold != null)
			{
				return ModConfig.WaterStaminaFleeThreshold.Value;
			}
		}
		catch (Exception)
		{
		}
		return 0.25f;
	}

	private static float StaminaPct(Component stamComp)
	{
		Type type = stamComp.GetType();
		if (_staminaProp == null)
		{
			_staminaProp = type.GetProperty("Stamina", BindingFlags.Instance | BindingFlags.Public);
		}
		if (_maxStaminaProp == null)
		{
			_maxStaminaProp = type.GetProperty("MaxStamina", BindingFlags.Instance | BindingFlags.Public);
		}
		if (_staminaProp == null || _maxStaminaProp == null)
		{
			return 1f;
		}
		float num = (float)_staminaProp.GetValue(stamComp, null);
		float num2 = (float)_maxStaminaProp.GetValue(stamComp, null);
		if (num2 <= 0f)
		{
			return 1f;
		}
		return num / num2;
	}

	internal static bool TryFindShore(Vector3 from, float waterLevel, out Vector3 shore)
	{
		ZoneSystem instance = ZoneSystem.instance;
		shore = from;
		if (instance == null)
		{
			return false;
		}
		for (int i = 1; i <= 5; i++)
		{
			float num = (float)i * 10f;
			int num2 = 10;
			float num3 = float.MaxValue;
			Vector3 vector = from;
			bool flag = false;
			for (int j = 0; j < num2; j++)
			{
				float f = 360f / (float)num2 * (float)j * (Mathf.PI / 180f);
				Vector3 vector2 = from + new Vector3(Mathf.Sin(f) * num, 0f, Mathf.Cos(f) * num);
				if (instance.GetGroundHeight(vector2, out var height) && height >= waterLevel - 0.3f)
				{
					float num4 = Vector3.Distance(from, vector2);
					if (num4 < num3)
					{
						num3 = num4;
						vector = vector2;
						vector.y = height;
						flag = true;
					}
				}
			}
			if (flag)
			{
				shore = vector;
				return true;
			}
		}
		return false;
	}
}
