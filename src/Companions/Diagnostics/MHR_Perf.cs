using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace Companions;

// 1.5.91: (user: "I was hitting 70 fps with 8 companions running around before. Now with 4 of them I am hitting about
// 40fps.") With LogLevel on Detailed, the time the mod takes is measured and written to the log every 10 s: in all, and
// for the parts that took the most ("[Perf]" lines). Measured: every Update / FixedUpdate / LateUpdate / UpdateAI of the
// mod, the follow / movement / route / cart / farm-spot steps, and every Harmony patch of the mod on the game's own code.
// Times are inclusive (UpdateAI includes Follow, which includes the movement), so the parts don't add up to the total;
// the total counts each outermost call once. Off (no patches at all) until Detailed is turned on.
internal static class MHR_Perf
{
	private class Acc
	{
		public string Name;

		public long Ticks;

		public long Max;

		public int Calls;
	}

	private static readonly Dictionary<MethodBase, Acc> s_acc = new Dictionary<MethodBase, Acc>();

	private static Harmony s_harmony;

	private static bool s_installed;

	private static int s_depth;

	private static long s_top;

	private static int s_frames;

	private static float s_since = -1f;

	private const float Every = 10f;

	// the step functions worth timing on their own (type, method)
	private static readonly string[][] Steps = new string[][]
	{
		new string[] { "Companions.MHR_Follow", "Update" },
		new string[] { "Companions.MHR_Mover", "GoInner" },
		new string[] { "Companions.MHR_Mover", "Plan" },
		new string[] { "Companions.MHR_Mover", "Unstick" },
		new string[] { "Companions.MHR_Cart", "Pulled" },
		new string[] { "Companions.MHR_Cart", "InCartWay" },
		new string[] { "Companions.FarmController", "FindPlantPositionInZone" },
		new string[] { "Companions.FarmController", "FindPlantPosition" },
		new string[] { "Companions.FarmController", "TooHighToPick" },
	};

	// from CompanionsPlugin.Update: install once Detailed is on, count frames, write the summary every 10 s
	internal static void Tick(Harmony main)
	{
		if (!CompanionsPlugin.LogDetailed)
		{
			s_since = -1f;
			return;
		}
		if (!s_installed)
		{
			Install(main);
		}
		float now = Time.unscaledTime;
		if (s_since < 0f)
		{
			Reset(now);
			return;
		}
		s_frames++;
		if (now - s_since >= Every)
		{
			Report(now - s_since);
			Reset(now);
		}
	}

	private static void Reset(float now)
	{
		s_since = now;
		s_frames = 0;
		s_top = 0L;
		s_depth = 0;
		foreach (Acc a in s_acc.Values)
		{
			a.Ticks = 0L;
			a.Max = 0L;
			a.Calls = 0;
		}
	}

	private static void Install(Harmony main)
	{
		s_installed = true;
		try
		{
			s_harmony = new Harmony(CompanionsPlugin.PluginGUID + ".perf");
			HarmonyMethod pre = new HarmonyMethod(typeof(MHR_Perf), nameof(Pre));
			HarmonyMethod fin = new HarmonyMethod(typeof(MHR_Perf), nameof(Fin));
			HashSet<MethodBase> targets = new HashSet<MethodBase>();
			Type[] types;
			try
			{
				types = typeof(MHR_Perf).Assembly.GetTypes();
			}
			catch (ReflectionTypeLoadException e)
			{
				List<Type> ok = new List<Type>();
				foreach (Type t in e.Types)
				{
					if (t != null)
					{
						ok.Add(t);
					}
				}
				types = ok.ToArray();
			}
			foreach (Type t in types)
			{
				if (t == typeof(MHR_Perf) || t == typeof(CompanionsPlugin) || t.IsGenericTypeDefinition || t.IsInterface)
				{
					continue;
				}
				foreach (MethodInfo m in t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
				{
					if ((m.Name == "Update" || m.Name == "FixedUpdate" || m.Name == "LateUpdate" || m.Name == "UpdateAI") && Timeable(m))
					{
						targets.Add(m);
					}
				}
			}
			foreach (string[] step in Steps)
			{
				Type t = AccessTools.TypeByName(step[0]);
				MethodInfo m = t != null ? AccessTools.Method(t, step[1]) : null;
				if (m != null && Timeable(m))
				{
					targets.Add(m);
				}
			}
			// the mod's own patches on the game's code
			if (main != null)
			{
				foreach (MethodBase orig in main.GetPatchedMethods())
				{
					Patches info = Harmony.GetPatchInfo(orig);
					if (info == null)
					{
						continue;
					}
					foreach (Patch p in info.Prefixes)
					{
						AddPatch(p, targets);
					}
					foreach (Patch p in info.Postfixes)
					{
						AddPatch(p, targets);
					}
					foreach (Patch p in info.Finalizers)
					{
						AddPatch(p, targets);
					}
				}
			}
			int done = 0;
			foreach (MethodBase m in targets)
			{
				try
				{
					s_harmony.Patch(m, prefix: pre, finalizer: fin);
					s_acc[m] = new Acc { Name = m.DeclaringType.Name + "." + m.Name };
					done++;
				}
				catch (Exception e)
				{
					CompanionsPlugin.Log.LogDebug("[Perf] can't time " + m.DeclaringType?.Name + "." + m.Name + ": " + e.Message);
				}
			}
			CompanionsPlugin.Log.LogInfo("[Perf] timing " + done + " of the mod's methods - a summary every " + Every.ToString("F0") + " s while LogLevel is Detailed");
		}
		catch (Exception e)
		{
			CompanionsPlugin.Log.LogWarning("[Perf] couldn't start the timing: " + e.Message);
		}
	}

	private static void AddPatch(Patch p, HashSet<MethodBase> targets)
	{
		if (p != null && p.owner == CompanionsPlugin.PluginGUID && p.PatchMethod != null && Timeable(p.PatchMethod))
		{
			targets.Add(p.PatchMethod);
		}
	}

	private static bool Timeable(MethodInfo m)
	{
		return m != null && !m.IsAbstract && !m.IsGenericMethodDefinition && !m.ContainsGenericParameters && m.GetMethodBody() != null;
	}

	private static void Pre(out long __state)
	{
		s_depth++;
		__state = Stopwatch.GetTimestamp();
	}

	private static void Fin(long __state, MethodBase __originalMethod)
	{
		long dt = Stopwatch.GetTimestamp() - __state;
		if (s_depth > 0)
		{
			s_depth--;
		}
		if (s_depth == 0)
		{
			s_top += dt;
		}
		if (__originalMethod != null && s_acc.TryGetValue(__originalMethod, out var a))
		{
			a.Ticks += dt;
			a.Calls++;
			if (dt > a.Max)
			{
				a.Max = dt;
			}
		}
	}

	private static void Report(float secs)
	{
		if (s_frames <= 0)
		{
			return;
		}
		double msPerTick = 1000.0 / Stopwatch.Frequency;
		List<Acc> list = new List<Acc>();
		foreach (Acc a in s_acc.Values)
		{
			if (a.Calls > 0)
			{
				list.Add(a);
			}
		}
		list.Sort((x, y) => y.Ticks.CompareTo(x.Ticks));
		int companions = CompanionSetup.AllCompanions != null ? CompanionSetup.AllCompanions.Count : 0;
		StringBuilder sb = new StringBuilder();
		sb.Append("[Perf] last ").Append(secs.ToString("F0")).Append(" s: ").Append((s_frames / secs).ToString("F0")).Append(" FPS, ")
			.Append(companions).Append(" companion(s) loaded. The mod took ").Append((s_top * msPerTick / s_frames).ToString("F2")).Append(" ms a frame in all");
		int shown = 0;
		foreach (Acc a in list)
		{
			double perFrame = a.Ticks * msPerTick / s_frames;
			if (shown >= 12 || perFrame < 0.02)
			{
				break;
			}
			sb.Append("\n    ").Append(a.Name).Append(": ").Append(perFrame.ToString("F2")).Append(" ms/frame, ")
				.Append(((float)a.Calls / s_frames).ToString("F1")).Append(" calls/frame, slowest ").Append((a.Max * msPerTick).ToString("F1")).Append(" ms");
			shown++;
		}
		CompanionsPlugin.Log.LogInfo(sb.ToString());
	}
}
