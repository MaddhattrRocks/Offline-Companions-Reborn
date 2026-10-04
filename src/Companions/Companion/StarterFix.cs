using System;

namespace Companions;

internal static class StarterFix
{
	internal static void ResetIfRequested()
	{
		try
		{
			if (ModConfig.ResetStarterFlag == null || !ModConfig.ResetStarterFlag.Value)
			{
				return;
			}
			Player localPlayer = Player.m_localPlayer;
			string text = Key();
			if (!(localPlayer == null) && text != null)
			{
				localPlayer.m_customData.Remove(text);
				ModConfig.ResetStarterFlag.Value = false;
				if (Game.instance != null)
				{
					Game.instance.SavePlayerProfile(setLogoutPoint: false);
				}
				Log("[CompanionManager] ResetStarterFlag: cleared the starter-companion flag for this world/character; the starter panel can show again.");
			}
		}
		catch (Exception ex)
		{
			Log("[CompanionManager] ResetStarterFlag failed: " + ex.Message);
		}
	}

	internal static void MarkSpawned()
	{
		try
		{
			Player localPlayer = Player.m_localPlayer;
			string text = Key();
			if (!(localPlayer == null) && text != null)
			{
				localPlayer.m_customData[text] = "1";
				if (Game.instance != null)
				{
					Game.instance.SavePlayerProfile(setLogoutPoint: false);
				}
				Log("[CompanionManager] Starter companion spawned - marked this world as done for the character.");
			}
		}
		catch (Exception ex)
		{
			Log("[CompanionManager] MarkSpawned failed: " + ex.Message);
		}
	}

	private static string Key()
	{
		ZNet instance = ZNet.instance;
		if (instance == null)
		{
			return null;
		}
		return $"HC_StarterCompanion_{instance.GetWorldUID()}";
	}

	private static void Log(string s)
	{
		CompanionsPlugin.Log?.LogInfo(s);
	}
}
