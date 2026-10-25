using System;
using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Companions;

// 1.5.77: Odin's Shrine menu (user: "Using the shrine should give a popup that has 2 options. 1. Recover lost companions
// 2. Purchase a new companion. The recover lost companions should show each companion and their status, with an option to
// recover dead or lost companions like how it works now. The purchase should pop-up the same starting window to customize
// the companion. Then have a purchase button at the bottom"). The recover list keeps the old rules: Lost and Unknown can be
// brought back (each asks first, Unknown with its warning); a dead one is brought back by its tombstone.
public class ShrinePanel : MonoBehaviour
{
	[HarmonyPatch(typeof(Hud), "InRadial")]
	private static class HudInRadial_Patch
	{
		private static void Postfix(ref bool __result)
		{
			if (!__result && IsOpen)
			{
				__result = true;
			}
		}
	}

	private static ShrinePanel _instance;

	private CompanionShrine _shrine;

	private Canvas _canvas;

	private GameObject _root;

	private TMP_FontAsset _font;

	private static readonly Color BgColor = new Color(0.08f, 0.07f, 0.06f, 0.94f);

	private static readonly Color ButtonColor = new Color(0.15f, 0.13f, 0.1f, 0.9f);

	private static readonly Color ButtonHover = new Color(0.25f, 0.22f, 0.16f, 0.9f);

	private static readonly Color LabelColor = new Color(1f, 0.9f, 0.5f, 1f);

	private static readonly Color TextColor = new Color(0.85f, 0.8f, 0.65f, 1f);

	private const float Width = 560f;

	private const float RowHeight = 34f;

	internal static bool IsOpen => _instance != null && _instance._root != null && _instance._root.activeSelf;

	internal static void Show(CompanionShrine shrine)
	{
		if (_instance == null)
		{
			GameObject gameObject = new GameObject("HC_ShrinePanel");
			UnityEngine.Object.DontDestroyOnLoad(gameObject);
			_instance = gameObject.AddComponent<ShrinePanel>();
		}
		_instance._shrine = shrine;
		_instance.BuildMenu();
	}

	// the two options
	private void BuildMenu()
	{
		Begin(200f, ModLocalization.Loc("hc_shrine_title"));
		AddButton(_root.transform, ModLocalization.Loc("hc_shrine_opt_recover"), new Vector2(0f, -56f), new Vector2(Width - 60f, 44f), BuildRecover);
		AddButton(_root.transform, ModLocalization.LocFmt("hc_shrine_opt_buy", CompanionTierData.Price.ToString("N0")), new Vector2(0f, -112f), new Vector2(Width - 60f, 44f), Purchase);
		CompanionsPlugin.Log.LogInfo("[Shrine] Menu opened");
	}

	// each companion on the roster and its status; Recover for a lost one (or one that isn't loaded)
	private void BuildRecover()
	{
		List<KeyValuePair<MHR_Roster.Entry, MHR_Roster.Status>> roster = MHR_Roster.GetRoster();
		int rows = Mathf.Max(1, roster.Count);
		Begin(110f + rows * (RowHeight + 4f) + 50f, ModLocalization.Loc("hc_shrine_opt_recover"));
		CompanionsPlugin.Log.LogInfo($"[Shrine] Recover list - {roster.Count} companion(s) on the roster");
		float y = -50f;
		if (roster.Count == 0)
		{
			AddText(_root.transform, ModLocalization.Loc("hc_shrine_none"), new Vector2(0f, y), new Vector2(Width - 40f, RowHeight * 2f), TextAlignmentOptions.Center);
		}
		foreach (KeyValuePair<MHR_Roster.Entry, MHR_Roster.Status> item in roster)
		{
			MHR_Roster.Entry entry = item.Key;
			MHR_Roster.Status status = item.Value;
			bool canRecover = status == MHR_Roster.Status.Lost || status == MHR_Roster.Status.Unknown;
			AddText(_root.transform, entry.Name + "  -  " + StatusText(status), new Vector2(canRecover ? -60f : 0f, y), new Vector2(canRecover ? Width - 160f : Width - 40f, RowHeight), TextAlignmentOptions.Left);
			if (canRecover)
			{
				AddButton(_root.transform, ModLocalization.Loc("hc_shrine_btn_recover"), new Vector2(Width / 2f - 70f, y), new Vector2(110f, RowHeight - 4f), delegate
				{
					AskRecover(entry, status);
				});
			}
			y -= RowHeight + 4f;
		}
		AddButton(_root.transform, ModLocalization.Loc("hc_shrine_back"), new Vector2(0f, y - 10f), new Vector2(160f, 34f), BuildMenu);
	}

	private static string StatusText(MHR_Roster.Status s)
	{
		return s switch
		{
			MHR_Roster.Status.Alive => "<color=#80E080>" + ModLocalization.Loc("hc_shrine_alive") + "</color>",
			MHR_Roster.Status.Dead => "<color=#C0C0C0>" + ModLocalization.Loc("hc_shrine_dead") + "</color>",
			MHR_Roster.Status.Lost => "<color=#FF7060>" + ModLocalization.Loc("hc_shrine_lost") + "</color>",
			_ => "<color=#FFCC50>" + ModLocalization.Loc("hc_shrine_unknown") + "</color>",
		};
	}

	// the same question the shrine asked before (game popup, above this panel)
	private void AskRecover(MHR_Roster.Entry entry, MHR_Roster.Status status)
	{
		if (!UnifiedPopup.IsAvailable() || UnifiedPopup.IsVisible())
		{
			return;
		}
		string text = "Bring " + entry.Name + " back here? It returns with its gear and settings from the last roster save. If the old " + entry.Name + " turns up later, it will be removed.";
		if (status == MHR_Roster.Status.Unknown)
		{
			text = text + "\n\n<color=#FFCC50>Warning:</color> " + entry.Name + " may still be alive somewhere that isn't loaded" + (entry.Dead ? " (last known: it died, and its tombstone keeps its items too)." : ".");
		}
		UnifiedPopup.Push(new YesNoPopup(MHR_Shrine.DisplayName, text, delegate
		{
			UnifiedPopup.Pop();
			CompanionShrine shrine = _shrine;
			if (shrine != null)
			{
				CompanionsPlugin.Log.LogInfo("[Shrine] Recovering " + entry.Name);
				MHR_Roster.RecoverAt(entry, shrine.transform.position + shrine.transform.forward * 2f);
			}
			Close();
		}, delegate
		{
			UnifiedPopup.Pop();
		}, localizeText: false));
	}

	// the starting window, with a Purchase button; the coins are checked when it's pressed
	private void Purchase()
	{
		CompanionsPlugin.Log.LogInfo($"[Shrine] Purchase - price {CompanionTierData.Price}, you have {CompanionManager.CoinsInPack()} coins");
		Close();
		StarterCompanionPanel.ShowHirePanel();
	}

	private void Begin(float height, string title)
	{
		Clear();
		_font = ResolveFont();
		GameObject canvasGo = new GameObject("ShrineCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
		canvasGo.transform.SetParent(base.transform, worldPositionStays: false);
		_canvas = canvasGo.GetComponent<Canvas>();
		_canvas.renderMode = RenderMode.ScreenSpaceOverlay;
		_canvas.sortingOrder = ModUiLayers.ShrinePanel;
		CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
		scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
		scaler.referenceResolution = new Vector2(1920f, 1080f);
		_root = new GameObject("ShrineRoot", typeof(RectTransform), typeof(Image));
		_root.transform.SetParent(canvasGo.transform, worldPositionStays: false);
		RectTransform rt = _root.GetComponent<RectTransform>();
		rt.anchorMin = new Vector2(0.5f, 0.5f);
		rt.anchorMax = new Vector2(0.5f, 0.5f);
		rt.pivot = new Vector2(0.5f, 0.5f);
		rt.sizeDelta = new Vector2(Width, height);
		_root.GetComponent<Image>().color = BgColor;
		TextMeshProUGUI t = AddText(_root.transform, title, new Vector2(0f, -6f), new Vector2(Width - 60f, 34f), TextAlignmentOptions.Center);
		t.fontSize = 20f;
		t.color = LabelColor;
		t.fontStyle = FontStyles.Bold;
		AddCloseButton(_root.transform);
		Cursor.lockState = CursorLockMode.None;
		Cursor.visible = true;
	}

	private TextMeshProUGUI AddText(Transform parent, string text, Vector2 pos, Vector2 size, TextAlignmentOptions align)
	{
		GameObject go = new GameObject("Text", typeof(RectTransform));
		go.transform.SetParent(parent, worldPositionStays: false);
		TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
		if (_font != null)
		{
			tmp.font = _font;
		}
		tmp.text = text;
		tmp.fontSize = 15f;
		tmp.color = TextColor;
		tmp.alignment = align;
		tmp.raycastTarget = false;
		tmp.textWrappingMode = TextWrappingModes.Normal;
		RectTransform rt = go.GetComponent<RectTransform>();
		rt.anchorMin = new Vector2(0.5f, 1f);
		rt.anchorMax = new Vector2(0.5f, 1f);
		rt.pivot = new Vector2(0.5f, 1f);
		rt.sizeDelta = size;
		rt.anchoredPosition = pos;
		return tmp;
	}

	private void AddButton(Transform parent, string label, Vector2 pos, Vector2 size, Action onClick)
	{
		GameObject go = new GameObject("Btn", typeof(RectTransform), typeof(Image), typeof(Button));
		go.transform.SetParent(parent, worldPositionStays: false);
		RectTransform rt = go.GetComponent<RectTransform>();
		rt.anchorMin = new Vector2(0.5f, 1f);
		rt.anchorMax = new Vector2(0.5f, 1f);
		rt.pivot = new Vector2(0.5f, 1f);
		rt.sizeDelta = size;
		rt.anchoredPosition = pos;
		go.GetComponent<Image>().color = ButtonColor;
		Button b = go.GetComponent<Button>();
		ColorBlock colors = b.colors;
		colors.normalColor = ButtonColor;
		colors.highlightedColor = ButtonHover;
		colors.pressedColor = ButtonHover;
		colors.selectedColor = ButtonHover;
		b.colors = colors;
		b.onClick.AddListener(delegate
		{
			onClick?.Invoke();
		});
		TextMeshProUGUI tmp = AddText(go.transform, label, Vector2.zero, Vector2.zero, TextAlignmentOptions.Center);
		RectTransform trt = tmp.GetComponent<RectTransform>();
		trt.anchorMin = Vector2.zero;
		trt.anchorMax = Vector2.one;
		trt.offsetMin = Vector2.zero;
		trt.offsetMax = Vector2.zero;
		tmp.color = LabelColor;
	}

	private void AddCloseButton(Transform parent)
	{
		GameObject go = new GameObject("CloseBtn", typeof(RectTransform), typeof(Image), typeof(Button));
		go.transform.SetParent(parent, worldPositionStays: false);
		RectTransform rt = go.GetComponent<RectTransform>();
		rt.anchorMin = new Vector2(1f, 1f);
		rt.anchorMax = new Vector2(1f, 1f);
		rt.pivot = new Vector2(1f, 1f);
		rt.sizeDelta = new Vector2(28f, 28f);
		rt.anchoredPosition = new Vector2(-4f, -4f);
		go.GetComponent<Image>().color = new Color(0.3f, 0.15f, 0.1f, 0.7f);
		go.GetComponent<Button>().onClick.AddListener(Close);
		TextMeshProUGUI tmp = AddText(go.transform, "X", Vector2.zero, Vector2.zero, TextAlignmentOptions.Center);
		RectTransform trt = tmp.GetComponent<RectTransform>();
		trt.anchorMin = Vector2.zero;
		trt.anchorMax = Vector2.one;
		trt.offsetMin = Vector2.zero;
		trt.offsetMax = Vector2.zero;
		tmp.color = Color.white;
	}

	private static TMP_FontAsset ResolveFont()
	{
		if (InventoryGui.instance != null)
		{
			TextMeshProUGUI[] texts = InventoryGui.instance.GetComponentsInChildren<TextMeshProUGUI>(includeInactive: true);
			for (int i = 0; i < texts.Length; i++)
			{
				if (texts[i] != null && texts[i].font != null && texts[i].font.name.IndexOf("LiberationSans", StringComparison.OrdinalIgnoreCase) < 0)
				{
					return texts[i].font;
				}
			}
		}
		return TMP_Settings.defaultFontAsset;
	}

	private void Update()
	{
		if (_root == null || !_root.activeSelf)
		{
			return;
		}
		Cursor.lockState = CursorLockMode.None;
		Cursor.visible = true;
		if (!UnifiedPopup.IsVisible() && (Input.GetKeyDown(KeyCode.Escape) || ZInput.GetButtonDown("JoyButtonB")))
		{
			Close();
		}
	}

	private void Clear()
	{
		if (_root != null)
		{
			UnityEngine.Object.Destroy(_root);
			_root = null;
		}
		if (_canvas != null)
		{
			UnityEngine.Object.Destroy(_canvas.gameObject);
			_canvas = null;
		}
	}

	private void Close()
	{
		Clear();
	}

	private void OnDestroy()
	{
		if (_instance == this)
		{
			_instance = null;
		}
	}
}
