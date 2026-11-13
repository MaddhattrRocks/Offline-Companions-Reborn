using System.Collections.Generic;
using UnityEngine;

namespace Companions;

public static class MHR_RespawnTracker
{
	internal static readonly int RespawnAtHash = "HC_RespawnAt".GetStableHashCode();

	internal static readonly int CleanupHash = "HC_TombCleanup".GetStableHashCode();

	private static float s_nextTick;

	internal static void Tick()
	{
		if (!(Time.unscaledTime >= s_nextTick))
		{
			return;
		}
		s_nextTick = Time.unscaledTime + 1f;
		CompanionTombstoneResurrect.MHR_SweepGravePins();
		CompanionManager.MHR_StarterFallbackTick();
		MHR_Diag.Tick();
		if (!ModConfig.AutoRespawnEnabled.Value)
		{
			return;
		}
		Player localPlayer = Player.m_localPlayer;
		if (localPlayer == null || localPlayer.IsDead() || ZDOMan.instance == null || ZNetScene.instance == null)
		{
			return;
		}
		if (s_deferred.Count > 0)
		{
			// 1.5.86: only the ones that died in this world (quitting to the menu while dead and loading another world
			// carried them over)
			string had = GetPending(localPlayer);
			long world = WorldUid();
			int added = 0;
			foreach (KeyValuePair<long, string> entry in s_deferred)
			{
				if (entry.Key == world && !had.Contains(";" + entry.Value.Substring(0, entry.Value.LastIndexOf(',') + 1)))
				{
					had = had + ";" + entry.Value;
					added++;
				}
			}
			SetPending(localPlayer, had);
			if (added > 0 && CompanionsPlugin.LogNormal) CompanionsPlugin.Log.LogInfo($"[Respawn] Tracking {added} companion(s) that died while you were dead");
			s_deferred.Clear();
		}
		MHR_AdoptLegacy(localPlayer);
		string pending = GetPending(localPlayer);
		if (string.IsNullOrEmpty(pending))
		{
			return;
		}
		long num = NowSeconds();
		string[] array = pending.Split(new char[1] { ';' });
		foreach (string text in array)
		{
			if (string.IsNullOrEmpty(text))
			{
				continue;
			}
			string[] array2 = text.Split(new char[1] { ',' });
			if (array2.Length != 3 || !long.TryParse(array2[0], out var result) || !uint.TryParse(array2[1], out var result2) || !long.TryParse(array2[2], out var result3) || num < result3)
			{
				continue;
			}
			ZDOID zDOID = new ZDOID(result, result2);
			ZDO zDO = ZDOMan.instance.GetZDO(zDOID);
			if (zDO == null)
			{
				// 1.5.86: not known here doesn't mean gone. Only a game that holds the whole world (single player / host)
				// can tell the tombstone is gone; a client of a dedicated server only knows what's near it, so the entry
				// waits until the tombstone is found (a very old one is let go so the list can't grow forever)
				bool wholeWorld = ZNet.instance != null && ZNet.instance.IsServer();
				if ((wholeWorld && num > result3 + 10800L) || num > result3 + 2592000L)
				{
					RemovePending(zDOID);
				}
			}
			else if (string.IsNullOrEmpty(zDO.GetString(CompanionTombstoneResurrect.RespawnOwnerHash)))
			{
				RemovePending(zDOID);
			}
			else if (!(ZNetScene.instance.FindInstance(zDO) != null))
			{
				if (CompanionsPlugin.LogNormal) CompanionsPlugin.Log.LogInfo("[Respawn] Respawn time reached for a companion whose tombstone is far away; respawning it now");
				CompanionTombstoneResurrect.MHR_ResurrectCore(zDO, null, isAutoRespawn: true);
				RemovePending(zDOID);
			}
		}
	}

	// world UID -> entry
	private static readonly List<KeyValuePair<long, string>> s_deferred = new List<KeyValuePair<long, string>>();

	internal static void OnCompanionDied(ZDO tombZdo, string ownerId)
	{
		if (tombZdo == null)
		{
			return;
		}
		// 1.5.1: died while the player is dead too (no player character right now): it's still the local player's
		// companion if the profile matches - remember it and add it to the respawn list once the player is back
		// (it was treated as another player's companion and only came back when the player went near its tombstone)
		if (Player.m_localPlayer == null)
		{
			string owner = tombZdo.GetString(CompanionTombstoneResurrect.RespawnOwnerHash);
			long profileId = Game.instance?.GetPlayerProfile()?.GetPlayerID() ?? 0L;
			if (profileId != 0L && owner == profileId.ToString())
			{
				s_deferred.Add(new KeyValuePair<long, string>(WorldUid(), EntryPrefix(tombZdo.m_uid) + tombZdo.GetLong(RespawnAtHash)));
				if (CompanionsPlugin.LogNormal) CompanionsPlugin.Log.LogInfo("[Respawn] Companion died while you were dead - it's added to the respawn list when you're back");
			}
			return;
		}
		// 1.5.80: an unsummon's tombstone only holds the gear - no owner, nothing to bring back (it said "another player's
		// game was running it")
		if (string.IsNullOrEmpty(tombZdo.GetString(CompanionTombstoneResurrect.RespawnOwnerHash)))
		{
			if (CompanionsPlugin.LogNormal) CompanionsPlugin.Log.LogInfo("[Respawn] Tombstone with an unsummoned companion's gear - nothing to respawn");
			return;
		}
		if (!IsLocalOwner(tombZdo))
		{
			if (CompanionsPlugin.LogNormal) CompanionsPlugin.Log.LogInfo("[Respawn] Companion died while another player's game was running it; its owner's game will respawn it when the owner is near the tombstone");
			return;
		}
		Player localPlayer = Player.m_localPlayer;
		string text = EntryPrefix(tombZdo.m_uid);
		string pending = GetPending(localPlayer);
		if (!pending.Contains(";" + text))
		{
			long num = tombZdo.GetLong(RespawnAtHash);
			SetPending(localPlayer, pending + ";" + text + num);
			if (CompanionsPlugin.LogNormal) CompanionsPlugin.Log.LogInfo($"[Respawn] Tracking dead companion (tombstone {text}): respawns at world time {num}s, now {NowSeconds()}s");
		}
	}

	internal static void RemovePending(ZDOID uid)
	{
		Player localPlayer = Player.m_localPlayer;
		if (localPlayer == null)
		{
			return;
		}
		string pending = GetPending(localPlayer);
		if (string.IsNullOrEmpty(pending))
		{
			return;
		}
		string text = EntryPrefix(uid);
		string[] array = pending.Split(new char[1] { ';' });
		string text2 = "";
		bool flag = false;
		foreach (string text3 in array)
		{
			if (!string.IsNullOrEmpty(text3))
			{
				if (text3.StartsWith(text))
				{
					flag = true;
				}
				else
				{
					text2 = text2 + ";" + text3;
				}
			}
		}
		if (flag)
		{
			SetPending(localPlayer, text2);
			if (CompanionsPlugin.LogDetailed) CompanionsPlugin.Log.LogDebug($"[Respawn] Stopped tracking tombstone {text}");
		}
	}

	// 1.5.86: the list from before 1.5.86 was one for every world - a world's clock is its own, so in another world its
	// entries looked hours overdue and were dropped, and the companion never came back by itself. Each entry moves to the
	// list of the world its tombstone is found in; until then it stays where it is.
	private static void MHR_AdoptLegacy(Player p)
	{
		if (p.m_customData == null || !p.m_customData.TryGetValue(LegacyKey, out var legacy) || string.IsNullOrEmpty(legacy))
		{
			return;
		}
		string pending = GetPending(p);
		string rest = "";
		int moved = 0;
		foreach (string text in legacy.Split(new char[1] { ';' }))
		{
			if (string.IsNullOrEmpty(text))
			{
				continue;
			}
			string[] array = text.Split(new char[1] { ',' });
			if (array.Length != 3 || !long.TryParse(array[0], out var user) || !uint.TryParse(array[1], out var id))
			{
				continue;
			}
			ZDO zDO = ZDOMan.instance.GetZDO(new ZDOID(user, id));
			if (zDO != null)
			{
				// its tombstone is in this world - track it here (or let it go if it's done respawning)
				if (IsLocalOwner(zDO) && !pending.Contains(";" + EntryPrefix(zDO.m_uid)))
				{
					pending = pending + ";" + text;
				}
				moved++;
			}
			else
			{
				rest = rest + ";" + text;
			}
		}
		if (moved == 0)
		{
			return;
		}
		SetPending(p, pending);
		if (rest.Length == 0)
		{
			p.m_customData.Remove(LegacyKey);
		}
		else
		{
			p.m_customData[LegacyKey] = rest;
		}
		if (CompanionsPlugin.LogNormal) CompanionsPlugin.Log.LogInfo($"[Respawn] {moved} dead companion(s) from the old shared list belong to this world - tracking them here");
	}

	internal static bool IsDueForLocalOwner(ZDO zdo)
	{
		if (IsLocalOwner(zdo) && !Player.m_localPlayer.IsDead())
		{
			long num = zdo.GetLong(RespawnAtHash);
			if (num != 0L)
			{
				return NowSeconds() >= num;
			}
			zdo.Set(RespawnAtHash, ComputeRespawnAt());
		}
		return false;
	}

	internal static long ComputeRespawnAt()
	{
		return NowSeconds() + (long)ModConfig.AutoRespawnDelaySeconds.Value;
	}

	internal static long NowSeconds()
	{
		if (!(ZNet.instance == null))
		{
			return (long)ZNet.instance.GetTimeSeconds();
		}
		return 0L;
	}

	internal static bool IsLocalOwner(ZDO zdo)
	{
		if (zdo != null)
		{
			Player localPlayer = Player.m_localPlayer;
			if (!(localPlayer == null))
			{
				long playerID = localPlayer.GetPlayerID();
				return zdo.GetString(CompanionTombstoneResurrect.RespawnOwnerHash) == playerID.ToString();
			}
		}
		return false;
	}

	private const string LegacyKey = "HC_PendingRespawns";

	// 1.5.86: one list per world
	private static string PendingKey()
	{
		return LegacyKey + "_" + WorldUid();
	}

	private static long WorldUid()
	{
		if (!(ZNet.instance == null))
		{
			return ZNet.instance.GetWorldUID();
		}
		return 0L;
	}

	private static string GetPending(Player p)
	{
		if (p.m_customData != null && p.m_customData.TryGetValue(PendingKey(), out var value) && value != null)
		{
			return value;
		}
		return "";
	}

	private static void SetPending(Player p, string val)
	{
		if (p.m_customData != null)
		{
			if (string.IsNullOrEmpty(val))
			{
				p.m_customData.Remove(PendingKey());
			}
			else
			{
				p.m_customData[PendingKey()] = val;
			}
		}
	}

	private static string EntryPrefix(ZDOID uid)
	{
		long userID = uid.UserID;
		uint iD = uid.ID;
		return string.Concat(userID + "," + iD, ",");
	}
}
