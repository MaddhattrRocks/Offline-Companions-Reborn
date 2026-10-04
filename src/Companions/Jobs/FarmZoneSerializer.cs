using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace Companions;

internal static class FarmZoneSerializer
{
	internal static readonly int FarmZonesHash = "HC_FarmZones".GetStableHashCode();

	internal static List<FarmZone> Load(ZDO zdo)
	{
		List<FarmZone> list = new List<FarmZone>(8);
		if (zdo == null)
		{
			return list;
		}
		string text = zdo.GetString(FarmZonesHash);
		if (string.IsNullOrEmpty(text))
		{
			return list;
		}
		string[] array = text.Split(new char[1] { '|' });
		string[] array2 = array;
		foreach (string text2 in array2)
		{
			string[] array3 = text2.Split(new char[1] { ',' });
			if (array3.Length >= 4 && float.TryParse(array3[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var result) && float.TryParse(array3[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var result2) && float.TryParse(array3[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var result3) && float.TryParse(array3[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var result4))
			{
				string cropSeed = ((array3.Length > 4) ? array3[4] : "");
				float result5 = 0f;
				if (array3.Length > 5)
				{
					float.TryParse(array3[5], NumberStyles.Float, CultureInfo.InvariantCulture, out result5);
				}
				list.Add(new FarmZone
				{
					Center = new Vector3(result, result2, result3),
					HalfSize = result4,
					CropSeed = cropSeed,
					Rotation = result5
				});
			}
		}
		return list;
	}

	internal static void Save(ZDO zdo, List<FarmZone> zones)
	{
		if (zdo == null)
		{
			return;
		}
		StringBuilder stringBuilder = new StringBuilder();
		for (int i = 0; i < zones.Count; i++)
		{
			if (zones[i].IsValid)
			{
				if (stringBuilder.Length > 0)
				{
					stringBuilder.Append('|');
				}
				FarmZone farmZone = zones[i];
				stringBuilder.Append(farmZone.Center.x.ToString(CultureInfo.InvariantCulture));
				stringBuilder.Append(',');
				stringBuilder.Append(farmZone.Center.y.ToString(CultureInfo.InvariantCulture));
				stringBuilder.Append(',');
				stringBuilder.Append(farmZone.Center.z.ToString(CultureInfo.InvariantCulture));
				stringBuilder.Append(',');
				stringBuilder.Append(farmZone.HalfSize.ToString(CultureInfo.InvariantCulture));
				stringBuilder.Append(',');
				stringBuilder.Append(farmZone.CropSeed ?? "");
				stringBuilder.Append(',');
				stringBuilder.Append(farmZone.Rotation.ToString(CultureInfo.InvariantCulture));
			}
		}
		zdo.Set(FarmZonesHash, stringBuilder.ToString());
	}
}
