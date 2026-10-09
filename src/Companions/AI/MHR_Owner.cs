using UnityEngine;

namespace Companions;

// 1.4.152: the companion's own player. In multiplayer a companion is often run by another player's game (the game hands
// a character to whichever nearby player's computer it likes), and everything that meant "the player" used
// Player.m_localPlayer - so on B's computer A's companion followed B, slept when B slept and sat when B sat down.
// Of() is the player in CompanionSetup.OwnerHash when their character is loaded here (null when it isn't).
internal static class MHR_Owner
{
	internal static string Id(Component c)
	{
		CompanionSetup cs = c != null ? c.GetComponent<CompanionSetup>() : null;
		ZNetView nv = cs != null ? cs.GetComponent<ZNetView>() : null;
		ZDO zdo = nv != null && nv.IsValid() ? nv.GetZDO() : null;
		return zdo != null ? zdo.GetString(CompanionSetup.OwnerHash) : null;
	}

	internal static Player Of(Component c)
	{
		Player local = Player.m_localPlayer;
		string id = Id(c);
		if (string.IsNullOrEmpty(id))
		{
			return local;   // not claimed yet (old saves) - the local player, as before
		}
		if (local != null && local.GetPlayerID().ToString() == id)
		{
			return local;
		}
		foreach (Player p in Player.GetAllPlayers())
		{
			if (p != null && p.GetPlayerID().ToString() == id)
			{
				return p;
			}
		}
		return null;
	}

	internal static GameObject Go(Component c)
	{
		Player p = Of(c);
		return p != null ? p.gameObject : null;
	}

	// the companion belongs to the player at this computer
	internal static bool IsMine(Component c)
	{
		Player local = Player.m_localPlayer;
		if (local == null)
		{
			return false;
		}
		string id = Id(c);
		return string.IsNullOrEmpty(id) || id == local.GetPlayerID().ToString();
	}

	internal static long CreatorId(Component c)
	{
		long v;
		string id = Id(c);
		if (!string.IsNullOrEmpty(id) && long.TryParse(id, out v))
		{
			return v;
		}
		return Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerID() : 0L;
	}

	// the player gives this companion an order: their game runs it from now on, so the order (kept in memory, not in
	// the world data) is carried out where it was given
	internal static void TakeControl(Component c)
	{
		ZNetView nv = c != null ? c.GetComponent<ZNetView>() : null;
		if (nv != null && nv.IsValid() && !nv.IsOwner())
		{
			nv.ClaimOwnership();
		}
	}
}
