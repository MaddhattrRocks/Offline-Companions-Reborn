using System;
using TMPro;
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

	private static bool s_textLogged;

	private static bool s_buttonLogged;

	// 1.5.79: a title like the game's container window title ("Chest")
	internal static void ApplyTitle(TMP_Text t)
	{
		CopyText(t, InventoryGui.instance?.m_containerName, "title");
	}

	// 1.5.79: body text like the game's crafting description
	internal static void ApplyBody(TMP_Text t)
	{
		CopyText(t, InventoryGui.instance?.m_recipeDecription, "body");
	}

	private static void CopyText(TMP_Text t, TMP_Text src, string what)
	{
		if (t == null || src == null)
		{
			return;
		}
		if (src.font != null)
		{
			t.font = src.font;
			t.fontSharedMaterial = src.fontSharedMaterial;
		}
		t.enableAutoSizing = false;
		t.fontSize = src.enableAutoSizing ? src.fontSizeMax : src.fontSize;
		t.color = src.color;
		t.fontStyle = src.fontStyle;
		if (!s_textLogged)
		{
			s_textLogged = true;
			TMP_Text title = InventoryGui.instance?.m_containerName;
			TMP_Text body = InventoryGui.instance?.m_recipeDecription;
			CompanionsPlugin.Log.LogInfo($"[UI] Game look: title text '{title?.font?.name}' {title?.fontSize:F0} (auto {title?.enableAutoSizing}), body text '{body?.font?.name}' {body?.fontSize:F0} (auto {body?.enableAutoSizing})");
		}
	}

	// 1.5.79: a copy of the game's Craft button with its own pieces (frame, label, sound), less the gamepad hint and the
	// hold-to-craft bar; at its own height (user: "It just looks like something is off for the buttons")
	internal static GameObject CopyButton(Transform parent, string label, out float height)
	{
		height = 0f;
		GameObject template = ButtonTemplate();
		if (template == null)
		{
			return null;
		}
		GameObject go = UnityEngine.Object.Instantiate(template, parent);
		go.name = "Btn";
		go.SetActive(value: true);
		RectTransform trt = template.GetComponent<RectTransform>();
		height = trt != null ? trt.rect.height : 0f;
		foreach (UIGamePad pad in go.GetComponentsInChildren<UIGamePad>(includeInactive: true))
		{
			if (pad.m_hint != null)
			{
				UnityEngine.Object.Destroy(pad.m_hint);
			}
			UnityEngine.Object.Destroy(pad);
		}
		Transform progress = InventoryGui.instance?.m_craftProgressPanel;
		if (progress != null && progress.IsChildOf(template.transform))
		{
			Transform same = go.transform.Find(GetPath(template.transform, progress));
			if (same != null)
			{
				UnityEngine.Object.Destroy(same.gameObject);
			}
		}
		ContentSizeFitter fitter = go.GetComponent<ContentSizeFitter>();
		if (fitter != null)
		{
			UnityEngine.Object.Destroy(fitter);
		}
		LayoutElement layout = go.GetComponent<LayoutElement>();
		if (layout != null)
		{
			UnityEngine.Object.Destroy(layout);
		}
		Button b = go.GetComponent<Button>();
		if (b != null)
		{
			b.onClick.RemoveAllListeners();
			b.interactable = true;
			b.navigation = new Navigation
			{
				mode = Navigation.Mode.Automatic
			};
		}
		TMP_Text text = go.GetComponentInChildren<TMP_Text>(includeInactive: true);
		if (text != null)
		{
			text.gameObject.SetActive(value: true);
			text.text = label;
		}
		if (!s_buttonLogged)
		{
			s_buttonLogged = true;
			string kids = "";
			foreach (Transform c in template.transform)
			{
				kids += (kids.Length > 0 ? ", " : "") + c.name;
			}
			CompanionsPlugin.Log.LogInfo($"[UI] Game look: Craft button {trt?.rect.width:F0} x {height:F0}, label '{text?.font?.name}' {text?.fontSize:F0} (auto {text?.enableAutoSizing}), parts: {kids}");
		}
		return go;
	}

	private static string GetPath(Transform root, Transform t)
	{
		string path = t.name;
		while (t.parent != null && t.parent != root)
		{
			t = t.parent;
			path = t.name + "/" + path;
		}
		return path;
	}

	// the game's Craft button, to copy
	internal static GameObject ButtonTemplate()
	{
		return InventoryGui.instance != null && InventoryGui.instance.m_craftButton != null ? InventoryGui.instance.m_craftButton.gameObject : null;
	}
}
