using System.Collections.Generic;

namespace Companions;

// 1.4.117: item helpers that used to live in HomesteadController (Homestead is retired; Cook still uses these)
internal static class MHR_Items
{
	internal static int CountItemInInventory(Inventory inv, string prefabName)
	{
		int num = 0;
		List<ItemDrop.ItemData> allItems = inv.GetAllItems();
		for (int i = 0; i < allItems.Count; i++)
		{
			string text = allItems[i].m_dropPrefab?.name ?? allItems[i].m_shared?.m_name;
			if (text == prefabName)
			{
				num += allItems[i].m_stack;
			}
		}
		return num;
	}

	internal static ItemDrop.ItemData FindItemByPrefab(Inventory inv, string prefabName)
	{
		List<ItemDrop.ItemData> allItems = inv.GetAllItems();
		for (int i = 0; i < allItems.Count; i++)
		{
			if (allItems[i].m_dropPrefab != null && allItems[i].m_dropPrefab.name == prefabName)
			{
				return allItems[i];
			}
		}
		return null;
	}

	internal static bool TransferOne(Inventory source, Inventory dest, ItemDrop.ItemData item)
	{
		ItemDrop.ItemData itemData = item.Clone();
		itemData.m_stack = 1;
		if (!dest.CanAddItem(itemData, 1))
		{
			return false;
		}
		if (!dest.AddItem(itemData))
		{
			return false;
		}
		source.RemoveItem(item, 1);
		return true;
	}

	internal static bool ConsumeOneFromInventory(Inventory inv, string prefabName)
	{
		List<ItemDrop.ItemData> allItems = inv.GetAllItems();
		for (int i = 0; i < allItems.Count; i++)
		{
			string text = allItems[i].m_dropPrefab?.name ?? allItems[i].m_shared?.m_name;
			if (text == prefabName)
			{
				inv.RemoveItem(allItems[i], 1);
				return true;
			}
		}
		return false;
	}
}
