using System;
using UnityEngine;
using UnityEngine.UI;

namespace Companions;

// 1.5.78: the mod's windows take their look from the game's own UI at runtime instead of textures packed in the mod (user:
// "I want to move away from using custom textures as much as possible ... if we ever try to make this work for consoles
// connecting to a dedicated server they wont have an issue. Also it fits in the game better."). The background is the
// one the inventory window's panels use; buttons are copies of its Craft button (CompanionPanel.CreateButton). Anything
// not found falls back to a plain colour, and the log says which.
internal static class GameLook
{
	private static Sprite s_panel;

	private static Material s_panelMat;

	private static bool s_looked;

	// the inventory window's panel background (the player inventory panel's own image, or the largest "bkg" image in it)
	internal static Sprite PanelSprite()
	{
		if (s_looked && s_panel != null)
		{
			return s_panel;
		}
		InventoryGui gui = InventoryGui.instance;
		if (gui == null)
		{
			return null;
		}
		s_looked = true;
		Image best = null;
		float bestArea = 0f;
		string from = null;
		RectTransform[] roots = new RectTransform[3] { gui.m_player, gui.m_container, gui.m_crafting };
		foreach (RectTransform root in roots)
		{
			if (root == null)
			{
				continue;
			}
			Image own = root.GetComponent<Image>();
			if (own != null && own.sprite != null)
			{
				float area = root.rect.width * root.rect.height;
				if (area > bestArea)
				{
					best = own;
					bestArea = area;
					from = root.name;
				}
			}
			foreach (Image img in root.GetComponentsInChildren<Image>(includeInactive: true))
			{
				if (img == null || img.sprite == null || img.gameObject.name.IndexOf("bkg", StringComparison.OrdinalIgnoreCase) < 0)
				{
					continue;
				}
				RectTransform rt = img.rectTransform;
				float area = rt.rect.width * rt.rect.height;
				if (area > bestArea)
				{
					best = img;
					bestArea = area;
					from = root.name + "/" + img.gameObject.name;
				}
			}
		}
		if (best != null)
		{
			s_panel = best.sprite;
			s_panelMat = best.material;
			CompanionsPlugin.Log.LogInfo($"[UI] Game look: panel background '{s_panel.name}' from the inventory window ({from})");
		}
		else
		{
			CompanionsPlugin.Log.LogInfo("[UI] Game look: no panel background found in the inventory window - plain colour");
		}
		return s_panel;
	}

	// a window or box background: the game's panel sprite, else the fallback colour
	internal static void ApplyPanel(Image img, Color fallback)
	{
		if (img == null)
		{
			return;
		}
		Sprite s = PanelSprite();
		if (s != null)
		{
			img.sprite = s;
			img.type = s.border.sqrMagnitude > 0f ? Image.Type.Sliced : Image.Type.Simple;
			if (s_panelMat != null && s_panelMat != img.defaultMaterial)
			{
				img.material = s_panelMat;
			}
			img.color = Color.white;
		}
		else
		{
			img.sprite = null;
			img.color = fallback;
		}
	}

	// the game's Craft button, to copy
	internal static GameObject ButtonTemplate()
	{
		return InventoryGui.instance != null && InventoryGui.instance.m_craftButton != null ? InventoryGui.instance.m_craftButton.gameObject : null;
	}
}
