using System.Collections.Generic;
using Splatform;
using UnityEngine;

namespace Companions;

// 1.5.98 Z4: one piece placed the way Player.PlacePiece does it (shared by the Repair and Restock job's wood / stone piles
// and the Ore job's workbench and ladder). The caller checks the spot and pays the materials, stamina and tool wear.
internal static class MHR_Build
{
	private static readonly List<IPlaced> s_placed = new List<IPlaced>();

	// Player.PlacePiece without the parts only a player has (known stations, its stats, the ward's owner name)
	internal static GameObject Place(Component builder, GameObject prefab, Vector3 pos, Quaternion rot, ItemDrop.ItemData tool)
	{
		TerrainModifier.SetTriggerOnPlaced(trigger: true);
		GameObject go = Object.Instantiate(prefab, pos, rot);
		TerrainModifier.SetTriggerOnPlaced(trigger: false);
		Piece piece = go.GetComponent<Piece>();
		if (piece != null)
		{
			PlatformUserID user = default(PlatformUserID);
			try
			{
				user = PlatformManager.DistributionPlatform.LocalUser.PlatformUserID;
			}
			catch
			{
			}
			piece.SetCreator(MHR_Owner.CreatorId(builder), user);   // 1.4.152: built for its own player
		}
		go.GetComponent<WearNTear>()?.OnPlaced();
		string anim = tool?.m_shared?.m_attack?.m_attackAnimation;
		builder.GetComponent<ZSyncAnimation>()?.SetTrigger(!string.IsNullOrEmpty(anim) ? anim : "swing_hammer");
		go.GetComponent<ItemDrop>()?.MakePiece(sendRPC: true);
		s_placed.Clear();
		go.GetComponents(s_placed);
		foreach (IPlaced p in s_placed)
		{
			p.OnPlaced();
		}
		if (piece != null)
		{
			Character c = builder.GetComponent<Character>();
			piece.m_placeEffect.Create(pos, rot, go.transform, 1f, -1, c != null ? c.GetZDOID() : ZDOID.None);
		}
		return go;
	}
}
