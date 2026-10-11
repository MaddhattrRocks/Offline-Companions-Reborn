using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Companions;

// 1.5.1: the Companion Shrine's window - your companions in this world with their status. Pick one that's Lost (or
// Unknown) and press Recover; a confirmation pops up, then it comes back next to the shrine.
public class ShrinePanel : MonoBehaviour
{
	private const float PanelW = 520f;

	private const float RowH = 30f;

	private const float MaxUseDistance = 6f;

	private static readonly Color BgColor = new Color(0.08f, 0.07f, 0.06f, 0.92f);

	private static readonly Color LabelColor = new Color(1f, 0.9f, 0.5f, 1f);

	private static readonly Color RowColor = new Color(0.16f, 0.14f, 0.11f, 0.85f);

	private static readonly Color RowSelected = new Color(0.45f, 0.36f, 0.12f, 0.95f);

	private static readonly Color ButtonOn = new Color(0.35f, 0.28f, 0.1f, 0.95f);

	private static readonly Color ButtonOff = new Color(0.2f, 0.2f, 0.2f, 0.6f);

	private bool _visible;

	private GameObject _root;

	private RectTransform _rootRT;

	private TMP_FontAsset _font;

	private TextMeshProUGUI _hintLabel;

	private TextMeshProUGUI _infoLabel;

	private Image _recoverImage;

	private CompanionShrine _shrine;

	private readonly List<KeyValuePair<MHR_Roster.Entry, MHR_Roster.Status>> _rows = new List<KeyValuePair<MHR_Roster.Entry, MHR_Roster.Status>>();

	private readonly List<GameObject> _rowObjects = new List<GameObject>();

	private int _selected = -1;

	private float _openedAt;

	public static ShrinePanel Instance { get; private set; }

	public bool IsVisible => _visible;

	public static void EnsureInstance()
	{
		if (!(Instance != null))
		{
			GameObject gameObject = new GameObject("HC_ShrinePanel");
			UnityEngine.Object.DontDestroyOnLoad(gameObject);
			gameObject.AddComponent<ShrinePanel>();
		}
	}

	private void Awake()
	{
		if (Instance != null && Instance != this)
		{
			UnityEngine.Object.Destroy(base.gameObject);
		}
		else
		{
			Instance = this;
		}
	}

	private void OnDestroy()
	{
		if (Instance == this)
		{
			Instance = null;
		}
	}

	public void Show(CompanionShrine shrine)
	{
		if (shrine == null)
		{
			return;
		}
		if (_root == null)
		{
			Build();
		}
		_shrine = shrine;
		_rows.Clear();
		_rows.AddRange(MHR_Roster.GetRoster());
		_selected = -1;
		for (int i = 0; i < _rows.Count; i++)
		{
			if (CanRecover(_rows[i].Value))
			{
				_selected = i;
				break;
			}
		}
		if (_selected < 0 && _rows.Count > 0)
		{
			_selected = 0;
		}
		RebuildRows();
		_root.SetActive(value: true);
		_visible = true;
		_openedAt = Time.unscaledTime;
		CompanionsPlugin.Log.LogInfo($"[Shrine] Opened - {_rows.Count} companion(s) on the roster");
	}

	public void Hide()
	{
		_visible = false;
		_shrine = null;
		if (_root != null)
		{
			_root.SetActive(value: false);
		}
	}

	private static bool CanRecover(MHR_Roster.Status s)
	{
		if (s != MHR_Roster.Status.Lost)
		{
			return s == MHR_Roster.Status.Unknown;
		}
		return true;
	}

	private void Update()
	{
		if (!_visible)
		{
			return;
		}
		Player localPlayer = Player.m_localPlayer;
		if (_shrine == null || localPlayer == null || localPlayer.IsDead() || Vector3.Distance(localPlayer.transform.position, _shrine.transform.position) > MaxUseDistance)
		{
			Hide();
			return;
		}
		if (Input.GetKeyDown(KeyCode.Escape) || ZInput.GetButtonDown("JoyButtonB"))
		{
			Hide();
			return;
		}
		Cursor.lockState = CursorLockMode.None;
		Cursor.visible = true;
		// the key press that opened the window doesn't also count in it
		if (_rows.Count > 0 && Time.unscaledTime - _openedAt > 0.3f)
		{
			int num = 0;
			if (ZInput.GetButtonDown("JoyDPadUp") || Input.GetKeyDown(KeyCode.UpArrow))
			{
				num = -1;
			}
			else if (ZInput.GetButtonDown("JoyDPadDown") || Input.GetKeyDown(KeyCode.DownArrow))
			{
				num = 1;
			}
			if (num != 0)
			{
				Select(Mathf.Clamp(_selected + num, 0, _rows.Count - 1));
			}
			if (ZInput.GetButtonDown("JoyButtonA") || Input.GetKeyDown(KeyCode.Return))
			{
				OnRecover();
			}
		}
		if (_hintLabel != null)
		{
			_hintLabel.text = (ZInput.IsGamepadActive() ? "[D-Pad] Choose   [A] Recover   [B] Close" : "Click a companion, then Recover  •  Esc to close");
		}
	}

	private void Select(int index)
	{
		_selected = index;
		for (int i = 0; i < _rowObjects.Count; i++)
		{
			_rowObjects[i].GetComponent<Image>().color = ((i == _selected) ? RowSelected : RowColor);
		}
		UpdateInfo();
	}

	private void UpdateInfo()
	{
		bool flag = _selected >= 0 && _selected < _rows.Count;
		bool flag2 = flag && CanRecover(_rows[_selected].Value);
		if (_recoverImage != null)
		{
			_recoverImage.color = (flag2 ? ButtonOn : ButtonOff);
		}
		if (_infoLabel == null)
		{
			return;
		}
		if (_rows.Count == 0)
		{
			_infoLabel.text = "No companions on your roster in this world yet. A companion joins the roster once it has been near you for a few seconds.";
		}
		else if (!flag)
		{
			_infoLabel.text = "";
		}
		else
		{
			MHR_Roster.Entry key = _rows[_selected].Key;
			switch (_rows[_selected].Value)
			{
			case MHR_Roster.Status.Alive:
				_infoLabel.text = key.Name + " is alive. Nothing to recover.";
				break;
			case MHR_Roster.Status.Dead:
				_infoLabel.text = key.Name + " is dead and its tombstone will bring it back.";
				break;
			case MHR_Roster.Status.Lost:
				_infoLabel.text = "Nothing is left of " + key.Name + " in the world. It can be brought back here with its gear and settings from the last roster save.";
				break;
			default:
				_infoLabel.text = key.Name + "'s status can't be checked from here (on a dedicated server only the area around you is known). It may still be alive somewhere.";
				break;
			}
		}
	}

	private void OnRecover()
	{
		if (_selected < 0 || _selected >= _rows.Count || !CanRecover(_rows[_selected].Value))
		{
			return;
		}
		MHR_Roster.Entry entry = _rows[_selected].Key;
		MHR_Roster.Status value = _rows[_selected].Value;
		CompanionShrine shrine = _shrine;
		string text = "Bring " + entry.Name + " back here? It returns with its gear and settings from the last roster save. If the old " + entry.Name + " turns up later, it will be removed.";
		if (value == MHR_Roster.Status.Unknown)
		{
			text = entry.Name + "'s status is Unknown - it may still be alive somewhere that isn't loaded" + (entry.Dead ? " (last known: it died, and its tombstone keeps its items too)" : "") + ".\n\n" + text;
		}
		Hide();
		if (!UnifiedPopup.IsAvailable())
		{
			return;
		}
		UnifiedPopup.Push(new YesNoPopup("Recover " + entry.Name, text, delegate
		{
			UnifiedPopup.Pop();
			if (shrine != null)
			{
				Vector3 near = shrine.transform.position + shrine.transform.forward * 2f;
				MHR_Roster.RecoverAt(entry, near);
			}
		}, delegate
		{
			UnifiedPopup.Pop();
		}, localizeText: false));
	}

	private void Build()
	{
		Canvas canvas = base.gameObject.AddComponent<Canvas>();
		canvas.renderMode = RenderMode.ScreenSpaceOverlay;
		canvas.sortingOrder = 29;
		CanvasScaler canvasScaler = base.gameObject.AddComponent<CanvasScaler>();
		canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
		canvasScaler.referenceResolution = new Vector2(1920f, 1080f);
		base.gameObject.AddComponent<GraphicRaycaster>();
		_font = ResolveFont();
		_root = new GameObject("ShrineRoot", typeof(RectTransform), typeof(Image));
		_root.transform.SetParent(base.transform, worldPositionStays: false);
		_rootRT = _root.GetComponent<RectTransform>();
		_rootRT.anchorMin = new Vector2(0.5f, 0.5f);
		_rootRT.anchorMax = new Vector2(0.5f, 0.5f);
		_rootRT.pivot = new Vector2(0.5f, 0.5f);
		_root.GetComponent<Image>().color = BgColor;
		TextMeshProUGUI textMeshProUGUI = Label(_root.transform, MHR_Shrine.DisplayName, 18f, LabelColor, TextAlignmentOptions.Center);
		textMeshProUGUI.fontStyle = FontStyles.Bold;
		Place(textMeshProUGUI.rectTransform, top: true, 6f, 30f);
		_infoLabel = Label(_root.transform, "", 13f, new Color(0.85f, 0.8f, 0.65f, 1f), TextAlignmentOptions.Center);
		_hintLabel = Label(_root.transform, "", 11f, new Color(0.6f, 0.6f, 0.6f, 0.8f), TextAlignmentOptions.Center);
		Place(_hintLabel.rectTransform, top: false, 4f, 18f);
		Button button = MakeButton(_root.transform, "Recover", OnRecover);
		_recoverImage = button.GetComponent<Image>();
		RectTransform component = button.GetComponent<RectTransform>();
		component.anchorMin = new Vector2(0.5f, 0f);
		component.anchorMax = new Vector2(0.5f, 0f);
		component.pivot = new Vector2(1f, 0f);
		component.sizeDelta = new Vector2(140f, 30f);
		component.anchoredPosition = new Vector2(-8f, 28f);
		RectTransform component2 = MakeButton(_root.transform, "Close", Hide).GetComponent<RectTransform>();
		component2.anchorMin = new Vector2(0.5f, 0f);
		component2.anchorMax = new Vector2(0.5f, 0f);
		component2.pivot = new Vector2(0f, 0f);
		component2.sizeDelta = new Vector2(140f, 30f);
		component2.anchoredPosition = new Vector2(8f, 28f);
		_root.SetActive(value: false);
	}

	private void RebuildRows()
	{
		foreach (GameObject rowObject in _rowObjects)
		{
			UnityEngine.Object.Destroy(rowObject);
		}
		_rowObjects.Clear();
		float num = 44f;
		for (int i = 0; i < _rows.Count; i++)
		{
			int index = i;
			MHR_Roster.Entry key = _rows[i].Key;
			MHR_Roster.Status value = _rows[i].Value;
			GameObject gameObject = new GameObject("Row", typeof(RectTransform), typeof(Image), typeof(Button));
			gameObject.transform.SetParent(_root.transform, worldPositionStays: false);
			RectTransform component = gameObject.GetComponent<RectTransform>();
			component.anchorMin = new Vector2(0f, 1f);
			component.anchorMax = new Vector2(1f, 1f);
			component.pivot = new Vector2(0.5f, 1f);
			component.offsetMin = new Vector2(16f, 0f);
			component.offsetMax = new Vector2(-16f, 0f);
			component.sizeDelta = new Vector2(-32f, RowH - 4f);
			component.anchoredPosition = new Vector2(0f, 0f - num);
			gameObject.GetComponent<Button>().onClick.AddListener(delegate
			{
				Select(index);
			});
			TextMeshProUGUI textMeshProUGUI = Label(gameObject.transform, key.Name, 14f, Color.white, TextAlignmentOptions.Left);
			Fill(textMeshProUGUI.rectTransform, 10f, 0.55f);
			TextMeshProUGUI textMeshProUGUI2 = Label(gameObject.transform, value.ToString(), 14f, StatusColor(value), TextAlignmentOptions.Right);
			textMeshProUGUI2.fontStyle = FontStyles.Bold;
			RectTransform rectTransform = textMeshProUGUI2.rectTransform;
			rectTransform.anchorMin = new Vector2(0.55f, 0f);
			rectTransform.anchorMax = new Vector2(1f, 1f);
			rectTransform.offsetMin = Vector2.zero;
			rectTransform.offsetMax = new Vector2(-10f, 0f);
			_rowObjects.Add(gameObject);
			num += RowH;
		}
		float num2 = Mathf.Max(1, _rows.Count) * RowH;
		_rootRT.sizeDelta = new Vector2(PanelW, 44f + num2 + 70f + 64f);
		RectTransform rectTransform2 = _infoLabel.rectTransform;
		rectTransform2.anchorMin = new Vector2(0f, 0f);
		rectTransform2.anchorMax = new Vector2(1f, 0f);
		rectTransform2.pivot = new Vector2(0.5f, 0f);
		rectTransform2.offsetMin = new Vector2(16f, 0f);
		rectTransform2.offsetMax = new Vector2(-16f, 0f);
		rectTransform2.sizeDelta = new Vector2(-32f, 64f);
		rectTransform2.anchoredPosition = new Vector2(0f, 64f);
		Select(_selected);
	}

	private static Color StatusColor(MHR_Roster.Status s)
	{
		return s switch
		{
			MHR_Roster.Status.Alive => new Color(0.5f, 0.9f, 0.5f, 1f),
			MHR_Roster.Status.Dead => new Color(0.75f, 0.75f, 0.75f, 1f),
			MHR_Roster.Status.Lost => new Color(1f, 0.45f, 0.35f, 1f),
			_ => new Color(1f, 0.8f, 0.3f, 1f),
		};
	}

	private TextMeshProUGUI Label(Transform parent, string text, float size, Color color, TextAlignmentOptions align)
	{
		GameObject gameObject = new GameObject("Label", typeof(RectTransform));
		gameObject.transform.SetParent(parent, worldPositionStays: false);
		TextMeshProUGUI textMeshProUGUI = gameObject.AddComponent<TextMeshProUGUI>();
		if (_font != null)
		{
			textMeshProUGUI.font = _font;
		}
		textMeshProUGUI.text = text;
		textMeshProUGUI.fontSize = size;
		textMeshProUGUI.color = color;
		textMeshProUGUI.alignment = align;
		textMeshProUGUI.textWrappingMode = TextWrappingModes.Normal;
		textMeshProUGUI.raycastTarget = false;
		return textMeshProUGUI;
	}

	private static void Place(RectTransform rt, bool top, float margin, float height)
	{
		float num = (top ? 1f : 0f);
		rt.anchorMin = new Vector2(0f, num);
		rt.anchorMax = new Vector2(1f, num);
		rt.pivot = new Vector2(0.5f, num);
		rt.offsetMin = new Vector2(10f, 0f);
		rt.offsetMax = new Vector2(-10f, 0f);
		rt.sizeDelta = new Vector2(-20f, height);
		rt.anchoredPosition = new Vector2(0f, top ? (0f - margin) : margin);
	}

	private static void Fill(RectTransform rt, float pad, float right)
	{
		rt.anchorMin = new Vector2(0f, 0f);
		rt.anchorMax = new Vector2(right, 1f);
		rt.offsetMin = new Vector2(pad, 0f);
		rt.offsetMax = Vector2.zero;
	}

	private Button MakeButton(Transform parent, string text, UnityEngine.Events.UnityAction onClick)
	{
		GameObject gameObject = new GameObject(text + "Btn", typeof(RectTransform), typeof(Image), typeof(Button));
		gameObject.transform.SetParent(parent, worldPositionStays: false);
		gameObject.GetComponent<Image>().color = ButtonOn;
		Button component = gameObject.GetComponent<Button>();
		component.onClick.AddListener(onClick);
		TextMeshProUGUI textMeshProUGUI = Label(gameObject.transform, text, 14f, Color.white, TextAlignmentOptions.Center);
		RectTransform rectTransform = textMeshProUGUI.rectTransform;
		rectTransform.anchorMin = Vector2.zero;
		rectTransform.anchorMax = Vector2.one;
		rectTransform.offsetMin = Vector2.zero;
		rectTransform.offsetMax = Vector2.zero;
		return component;
	}

	private static TMP_FontAsset ResolveFont()
	{
		Component[] array = new Component[2]
		{
			InventoryGui.instance,
			Hud.instance
		};
		foreach (Component component in array)
		{
			if (component == null)
			{
				continue;
			}
			TextMeshProUGUI[] componentsInChildren = component.GetComponentsInChildren<TextMeshProUGUI>(includeInactive: true);
			foreach (TextMeshProUGUI textMeshProUGUI in componentsInChildren)
			{
				if (textMeshProUGUI != null && textMeshProUGUI.font != null && textMeshProUGUI.font.name.IndexOf("LiberationSans", StringComparison.OrdinalIgnoreCase) < 0)
				{
					return textMeshProUGUI.font;
				}
			}
		}
		return TMP_Settings.defaultFontAsset;
	}
}
