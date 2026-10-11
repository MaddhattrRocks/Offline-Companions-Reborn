using System;
using System.Collections.Generic;
using UnityEngine;

namespace Companions;

// 1.5.1: the Companion Shrine - a buildable stone (the vanilla ward's model, without the ward) that shows your companion
// roster and brings back a lost companion. Built with the hammer at a workbench, [Death] ShrineRecipe.
internal static class MHR_Shrine
{
	internal const string PrefabName = "HC_CompanionShrine";

	internal const string DisplayName = "Companion Shrine";

	private const string ModelPrefab = "guard_stone";

	private static GameObject s_holder;

	private static GameObject s_prefab;

	// called on every ZNetScene.Awake (each world load). The piece is always registered - a world with shrines in it
	// would otherwise lose them while ShrineBuildable is off - and only left out of the hammer when it's off.
	internal static void Init(ZNetScene zs)
	{
		try
		{
			if (s_prefab == null && !Create(zs))
			{
				return;
			}
			if (zs.GetPrefab(PrefabName) == null)
			{
				CompanionPrefabs.RegisterPrefab(s_prefab, zs);
			}
			Piece component = s_prefab.GetComponent<Piece>();
			component.m_resources = ParseRecipe(zs, ModConfig.ShrineRecipe.Value);
			component.m_craftingStation = zs.GetPrefab("piece_workbench")?.GetComponent<CraftingStation>();
			PieceTable pieceTable = zs.GetPrefab("Hammer")?.GetComponent<ItemDrop>()?.m_itemData?.m_shared?.m_buildPieces;
			if (pieceTable == null)
			{
				CompanionsPlugin.Log.LogWarning("[Shrine] Hammer build table not found - the Companion Shrine can't be built");
				return;
			}
			pieceTable.m_pieces.RemoveAll((GameObject p) => p == null || p.name == PrefabName);
			if (ModConfig.ShrineBuildable.Value)
			{
				pieceTable.m_pieces.Add(s_prefab);
				CompanionsPlugin.Log.LogInfo("[Shrine] Companion Shrine added to the hammer (Misc), recipe " + ModConfig.ShrineRecipe.Value + " at a workbench");
			}
		}
		catch (Exception ex)
		{
			CompanionsPlugin.Log.LogWarning("[Shrine] Setup failed: " + ex);
		}
	}

	private static bool Create(ZNetScene zs)
	{
		GameObject prefab = zs.GetPrefab(ModelPrefab);
		if (prefab == null)
		{
			CompanionsPlugin.Log.LogWarning("[Shrine] Model " + ModelPrefab + " not found - no Companion Shrine");
			return false;
		}
		if (s_holder == null)
		{
			s_holder = new GameObject("HC_ShrinePrefab");
			UnityEngine.Object.DontDestroyOnLoad(s_holder);
			s_holder.SetActive(value: false);
		}
		GameObject gameObject = UnityEngine.Object.Instantiate(prefab, s_holder.transform, worldPositionStays: false);
		gameObject.name = PrefabName;
		float hoverOffset = 1f;
		PrivateArea component = gameObject.GetComponent<PrivateArea>();
		if (component != null)
		{
			hoverOffset = component.m_hoverOffset;
			if (component.m_areaMarker != null)
			{
				UnityEngine.Object.DestroyImmediate(component.m_areaMarker.gameObject);
			}
			if (component.m_inRangeEffect != null && component.m_inRangeEffect.transform.IsChildOf(gameObject.transform))
			{
				component.m_inRangeEffect.SetActive(value: false);
			}
			// the glow of a switched-on ward - the shrine is always "on"
			if (component.m_enabledEffect != null && component.m_enabledEffect.transform.IsChildOf(gameObject.transform))
			{
				component.m_enabledEffect.SetActive(value: true);
			}
			UnityEngine.Object.DestroyImmediate(component);
		}
		CircleProjector[] componentsInChildren = gameObject.GetComponentsInChildren<CircleProjector>(includeInactive: true);
		foreach (CircleProjector circleProjector in componentsInChildren)
		{
			UnityEngine.Object.DestroyImmediate(circleProjector);
		}
		Piece component2 = gameObject.GetComponent<Piece>();
		if (component2 == null)
		{
			CompanionsPlugin.Log.LogWarning("[Shrine] Model " + ModelPrefab + " isn't a building piece - no Companion Shrine");
			UnityEngine.Object.Destroy(gameObject);
			return false;
		}
		component2.m_name = DisplayName;
		component2.m_description = "Shows your companions and whether they're alive, and brings back one that's been lost.";
		component2.m_category = Piece.PieceCategory.Misc;
		component2.m_enabled = true;
		gameObject.AddComponent<CompanionShrine>().m_hoverOffset = hoverOffset;
		s_prefab = gameObject;
		CompanionsPlugin.Log.LogInfo("[Shrine] Companion Shrine piece created (model: " + ModelPrefab + ")");
		return true;
	}

	// "Stone:10,Wood:5,GreydwarfEye:3"
	private static Piece.Requirement[] ParseRecipe(ZNetScene zs, string recipe)
	{
		List<Piece.Requirement> list = new List<Piece.Requirement>();
		string[] array = (recipe ?? "").Split(new char[1] { ',' }, StringSplitOptions.RemoveEmptyEntries);
		foreach (string text in array)
		{
			string[] array2 = text.Split(new char[1] { ':' });
			string text2 = array2[0].Trim();
			int result = 1;
			if (array2.Length > 1 && !int.TryParse(array2[1].Trim(), out result))
			{
				result = 1;
			}
			ItemDrop itemDrop = zs.GetPrefab(text2)?.GetComponent<ItemDrop>();
			if (itemDrop == null)
			{
				CompanionsPlugin.Log.LogWarning("[Shrine] ShrineRecipe: unknown item \"" + text2 + "\" - skipped");
			}
			else if (result > 0)
			{
				list.Add(new Piece.Requirement
				{
					m_resItem = itemDrop,
					m_amount = result,
					m_recover = true
				});
			}
		}
		return list.ToArray();
	}
}

public class CompanionShrine : MonoBehaviour, Hoverable, Interactable
{
	internal float m_hoverOffset = 1f;

	public string GetHoverText()
	{
		return Localization.instance.Localize(MHR_Shrine.DisplayName + "\n[<color=yellow><b>$KEY_Use</b></color>] Your companions");
	}

	public string GetHoverName()
	{
		return MHR_Shrine.DisplayName;
	}

	public float GetHoverOffset()
	{
		return m_hoverOffset;
	}

	public bool Interact(Humanoid user, bool hold, bool alt)
	{
		if (hold || user == null || user != Player.m_localPlayer)
		{
			return false;
		}
		ShrinePanel.EnsureInstance();
		ShrinePanel.Instance.Show(this);
		return true;
	}

	public bool UseItem(Humanoid user, ItemDrop.ItemData item)
	{
		return false;
	}
}
