using UnityEngine;

namespace Companions;

// 1.5.94: (user: "very choppy" even at 70 FPS) the mod's nearby searches and multi-hit rays used to make a new list
// every time (Physics.OverlapSphere / RaycastAll ...), which piles up garbage and causes collection hitches. These fill
// a buffer the caller keeps instead. Same layers, same trigger handling, and still every hit: a full buffer is doubled
// and the query run again. The hits come back in no particular order, as before. Each call site keeps its own buffer,
// and the Hits it gets back is only good until that same site searches again.
internal struct Hits<T>
{
	private readonly T[] _a;

	public readonly int Length;

	public Hits(T[] a, int n)
	{
		_a = a;
		Length = n;
	}

	public T this[int i] => _a[i];

	public Enumerator GetEnumerator()
	{
		return new Enumerator(_a, Length);
	}

	public struct Enumerator
	{
		private readonly T[] _a;

		private readonly int _n;

		private int _i;

		public Enumerator(T[] a, int n)
		{
			_a = a;
			_n = n;
			_i = -1;
		}

		public T Current => _a[_i];

		public bool MoveNext()
		{
			return ++_i < _n;
		}
	}
}

internal static class MHR_Phys
{
	// one buffer per call site (file and line it was written at)
	internal static RaycastHit[] CompanionCombatTactics788 = new RaycastHit[16];
	internal static RaycastHit[] BowDraw311 = new RaycastHit[16];
	internal static Collider[] Hive163 = new Collider[64];
	internal static Collider[] Mover1247 = new Collider[64];
	internal static RaycastHit[] Mover1291 = new RaycastHit[16];
	internal static Collider[] Mover1374 = new Collider[64];
	internal static RaycastHit[] Mover1407 = new RaycastHit[16];
	internal static Collider[] Mover1680 = new Collider[64];
	internal static Collider[] Mover1772 = new Collider[64];
	internal static Collider[] Mover1802 = new Collider[64];
	internal static Collider[] Mover1990 = new Collider[64];
	internal static Collider[] RiseWatch107 = new Collider[64];
	internal static Collider[] HarvestController406 = new Collider[64];
	internal static RaycastHit[] HarvestController471 = new RaycastHit[16];
	internal static Collider[] HarvestController2230 = new Collider[64];
	internal static RaycastHit[] HarvestController2342 = new RaycastHit[16];
	internal static RaycastHit[] HarvestController2526 = new RaycastHit[16];
	internal static Collider[] GatherRules527 = new Collider[64];
	internal static Collider[] PileTaker302 = new Collider[64];
	internal static Collider[] ToolKeeper1082 = new Collider[64];
	internal static RaycastHit[] ToolKeeper1164 = new RaycastHit[16];
	internal static RaycastHit[] ToolKeeper1340 = new RaycastHit[16];
	internal static RaycastHit[] UpkeepController1600 = new RaycastHit[16];
	internal static Collider[] UpkeepHoney145 = new Collider[64];
	internal static Collider[] UpkeepPiles499 = new Collider[64];
	internal static Collider[] UpkeepPiles645 = new Collider[32];
	internal static Collider[] UpkeepPiles893 = new Collider[64];

	internal static Hits<Collider> Sphere(ref Collider[] buf, Vector3 p, float r, int mask = -1, QueryTriggerInteraction q = QueryTriggerInteraction.UseGlobal)
	{
		int n = Physics.OverlapSphereNonAlloc(p, r, buf, mask, q);
		while (n >= buf.Length)
		{
			buf = new Collider[buf.Length * 2];
			n = Physics.OverlapSphereNonAlloc(p, r, buf, mask, q);
		}
		return new Hits<Collider>(buf, n);
	}

	internal static Hits<Collider> Box(ref Collider[] buf, Vector3 center, Vector3 half, Quaternion rot, int mask = -1, QueryTriggerInteraction q = QueryTriggerInteraction.UseGlobal)
	{
		int n = Physics.OverlapBoxNonAlloc(center, half, buf, rot, mask, q);
		while (n >= buf.Length)
		{
			buf = new Collider[buf.Length * 2];
			n = Physics.OverlapBoxNonAlloc(center, half, buf, rot, mask, q);
		}
		return new Hits<Collider>(buf, n);
	}

	internal static Hits<RaycastHit> Ray(ref RaycastHit[] buf, Vector3 from, Vector3 dir, float dist, int mask = Physics.DefaultRaycastLayers, QueryTriggerInteraction q = QueryTriggerInteraction.UseGlobal)
	{
		int n = Physics.RaycastNonAlloc(from, dir, buf, dist, mask, q);
		while (n >= buf.Length)
		{
			buf = new RaycastHit[buf.Length * 2];
			n = Physics.RaycastNonAlloc(from, dir, buf, dist, mask, q);
		}
		return new Hits<RaycastHit>(buf, n);
	}

	internal static Hits<RaycastHit> SphereCast(ref RaycastHit[] buf, Vector3 from, float radius, Vector3 dir, float dist, int mask = Physics.DefaultRaycastLayers, QueryTriggerInteraction q = QueryTriggerInteraction.UseGlobal)
	{
		int n = Physics.SphereCastNonAlloc(from, radius, dir, buf, dist, mask, q);
		while (n >= buf.Length)
		{
			buf = new RaycastHit[buf.Length * 2];
			n = Physics.SphereCastNonAlloc(from, radius, dir, buf, dist, mask, q);
		}
		return new Hits<RaycastHit>(buf, n);
	}
}
