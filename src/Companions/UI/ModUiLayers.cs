using System.Collections.Generic;
using UnityEngine;

namespace Companions;

// 1.5.60: the sorting order of every canvas the mod makes, in one place. The game's layers (from a player's log):
// EnemyHud 200, HUD 400 (its crosshair HoverName text sits in the screen centre, faded out but still catching
// clicks), inventory 600, HudMessage 1000, text input 1100, pause menu 1700, UnifiedPopup 1900, LongPressRadial 2000,
// console 5000. Our panels used to be 28-31, under the HUD - the dead strip of clicks in F8 and the hover text over the
// wheel's centre. They now sit above the HUD and centre messages but below text input and the pause menu (user: our
// panels never go above the pause menu). Same order among themselves as before.
internal static class ModUiLayers
{
	internal const int Base = 1050;

	internal const int FarmZonePlacer = Base;

	internal const int HomeZonePanel = Base + 1;

	internal const int RadialMenu = Base + 2;

	internal const int StarterCompanionPanel = Base + 3;

	internal const int FarmZoneCropPicker = Base + 4;

	internal const int ConfigPanel = Base + 5;

	private static bool _logged;

	// Once per session, the first time one of our panels opens: every root canvas in the scene with its order, so a
	// log shows where the game's layers really are and whether anything sits above ours.
	internal static void LogSceneLayers(string panel)
	{
		if (_logged)
		{
			return;
		}
		_logged = true;
		try
		{
			List<string> list = new List<string>();
			foreach (Canvas canvas in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
			{
				if (canvas != null && (canvas.isRootCanvas || canvas.overrideSorting))
				{
					list.Add($"{canvas.name}={canvas.sortingOrder}" + (canvas.overrideSorting ? "(override)" : ""));
				}
			}
			list.Sort();
			CompanionsPlugin.Log.LogInfo($"[UiLayers] {panel} opened at order {Base}+ - canvases in the scene: " + string.Join(", ", list));
		}
		catch (System.Exception ex)
		{
			CompanionsPlugin.Log.LogWarning("[UiLayers] Couldn't list the canvases: " + ex.Message);
		}
	}
}
