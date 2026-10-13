using System;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace Companions;

// 1.5.41: speech overhaul, phase 1. The ambient lines a companion says now and then, one pool per situation, loaded
// from speech.json (or Translations/speech/<language>.json). A line can start with tags in square brackets that say
// when it fits, e.g. "[home] A fine hall you've built." or "[night,!rain] The stars are out." - see CompanionTalk.Fits.
// Tags: home, away, night, day, rain, dry, cold, indoors, outdoors, and the biomes meadows, blackforest, swamp,
// mountain, plains, mistlands, ashlands, deepnorth, ocean. "!" in front means "not". A line without tags always fits.
[Serializable]
public class SpeechConfig
{
	// 2 = the 1.5.41 layout. An older file is kept as speech.v1.json and replaced by the new defaults.
	public int Version;

	public string[] Action;

	public string[] Wood;

	public string[] Stone;

	public string[] Ore;

	// only used when Wood/Stone/Ore are empty
	public string[] Gather;

	public string[] Forage;

	public string[] Combat;

	public string[] Follow;

	public string[] Hungry;

	public string[] Repair;

	public string[] Overweight;

	public string[] Smelt;

	public string[] Idle;

	public string[] Hunt;

	public string[] Farm;

	public string[] Fish;

	public string[] Cook;

	public string[] Upkeep;

	public string[] NoBed;

	// idle chat when it goes over to you or another companion
	public string[] Chat;

	public const int CurrentVersion = 2;

	private static SpeechConfig _instance;

	public static SpeechConfig Instance
	{
		get
		{
			if (_instance == null)
			{
				Load();
			}
			return _instance;
		}
	}

	public static void Load()
	{
		string directoryName = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
		string text = "English";
		try
		{
			if (Localization.instance != null)
			{
				text = Localization.instance.GetSelectedLanguage();
			}
		}
		catch
		{
		}
		string path = Path.Combine(directoryName, "Translations", "speech", text + ".json");
		// a translation keeps its own lines even from an older layout (missing pools fall back to English)
		if (TryLoadFrom(path, upgradeOld: false))
		{
			return;
		}
		string text2 = Path.Combine(directoryName, "speech.json");
		if (TryLoadFrom(text2, upgradeOld: true))
		{
			return;
		}
		_instance = Defaults();
		Write(text2, "Created default");
	}

	public static void Reload()
	{
		_instance = null;
		Load();
	}

	private static void Write(string path, string what)
	{
		try
		{
			File.WriteAllText(path, JsonUtility.ToJson(_instance, prettyPrint: true));
			CompanionsPlugin.Log.LogInfo("[Speech] " + what + " " + path);
		}
		catch (Exception ex)
		{
			CompanionsPlugin.Log.LogWarning("[Speech] Failed to write " + path + ": " + ex.Message);
		}
	}

	private static bool TryLoadFrom(string path, bool upgradeOld)
	{
		if (!File.Exists(path))
		{
			return false;
		}
		try
		{
			string json = File.ReadAllText(path);
			SpeechConfig loaded = JsonUtility.FromJson<SpeechConfig>(json);
			if (loaded == null)
			{
				return false;
			}
			if (upgradeOld && loaded.Version < CurrentVersion)
			{
				// 1.5.41: the old file's pools mixed lines for different situations (a woodcutter talking about ore, home
				// lines out in the wild). Keep it next to the new one for anyone who edited it, and start from the new set.
				string backup = Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path) + ".v1.json");
				try
				{
					File.Copy(path, backup, overwrite: true);
				}
				catch
				{
				}
				_instance = Defaults();
				Write(path, "Updated to the new speech lines (your old file is kept as " + Path.GetFileName(backup) + ") -");
				return true;
			}
			_instance = loaded;
			// pools missing from the file (added in a later version, or deleted) come from the defaults
			if (_instance.FillMissing(Defaults()))
			{
				Write(path, "Added missing speech lines to");
			}
			CompanionsPlugin.Log.LogInfo("[Speech] Loaded " + path);
			return true;
		}
		catch (Exception ex)
		{
			CompanionsPlugin.Log.LogWarning("[Speech] Failed to parse " + path + ": " + ex.Message);
			return false;
		}
	}

	private bool FillMissing(SpeechConfig d)
	{
		bool any = false;
		foreach (FieldInfo f in typeof(SpeechConfig).GetFields(BindingFlags.Public | BindingFlags.Instance))
		{
			if (f.FieldType != typeof(string[]) || f.Name == "Gather")
			{
				continue;
			}
			string[] v = f.GetValue(this) as string[];
			if (v == null || v.Length == 0)
			{
				f.SetValue(this, f.GetValue(d));
				any = true;
			}
		}
		return any;
	}

	private static SpeechConfig Defaults()
	{
		SpeechConfig s = new SpeechConfig();
		s.Version = CurrentVersion;
		// said when you give it an order from the command wheel
		s.Action = new string[]
		{
			"By your word!", "So it shall be.", "Aye, consider it done!", "I heed your call.", "As you command.", "On it!", "Right away.", "It shall be done.",
			"Say no more.", "Leave it to me."
		};
		// while actually chopping (Wood job)
		s.Wood = new string[]
		{
			"Good timber here.", "Another tree falls!", "My arms grow stronger with each swing.", "Timber!", "This wood will burn well.",
			"[blackforest] The pines grow thick here.", "[meadows] Beech and birch, good and straight.", "[swamp] Ancient bark... tough going."
		};
		// while actually mining stone (Stone job)
		s.Stone = new string[]
		{
			"There's good stone over here.", "Stone for the walls.", "Chip by chip.", "This rock won't move itself.", "Solid stone, this."
		};
		// while actually mining ore (Ore job)
		s.Ore = new string[]
		{
			"This ore looks promising.", "The veins run deep in this rock.", "The ground gives up its treasures.", "Ore for the smelter.",
			"[blackforest] Copper and tin, our first real metal.", "[swamp] Iron, buried with the dead.", "[mountain] Silver, cold as the peaks."
		};
		s.Gather = new string[] { "The land provides, if you know where to look.", "A fine haul so far." };
		// while actually foraging
		s.Forage = new string[]
		{
			"The wild provides for those who seek.", "Ripe for the picking.", "Nature hides her bounty well.", "These will make a fine stew.",
			"[meadows] The meadows offer their gifts.", "[blackforest] Good pickings under these trees.", "[night] Hard to spot anything in this dark."
		};
		// only in a real fight
		s.Combat = new string[]
		{
			"For Odin!", "Taste my steel!", "Stand and fight!", "They shall not pass!", "By Thor's hammer!", "You picked the wrong fight!", "I'll send you to Hel!",
			"Come then! I am ready!", "The Valkyries watch!"
		};
		// while following you
		s.Follow = new string[]
		{
			"Lead on, I am your shield.", "Where the path takes us, I follow.", "Right behind you.", "I've got your back.", "What lies ahead, I wonder?", "Which way now?",
			"[day,!rain] A fine day to wander the wilds.", "[night] Keep close, the night hides much.", "[rain] Wet boots again.", "[cold] My beard is freezing.",
			"[blackforest] Watch for greydwarfs in these woods.", "[swamp] This bog stinks of death.", "[mountain] Mind the drop.", "[plains] Watch for the fuling camps.",
			"[mistlands] I can barely see my own hand.", "[ashlands] Even the air burns here.", "[deepnorth] Cold enough to freeze a troll.", "[ocean] Nothing but water and sky."
		};
		// no food in it
		s.Hungry = new string[]
		{
			"My belly roars like a troll...", "I'd trade my axe for a leg of boar right now.", "Got anything to eat?", "A warrior fights on mead and meat, not empty guts.",
			"I could eat an entire neck right now."
		};
		// its gear is badly worn
		s.Repair = new string[]
		{
			"This blade has seen better days.", "My gear is wearing thin.", "I should find a workbench soon.", "A few more hits and this breaks."
		};
		s.Overweight = new string[] { "My back is breaking under all this!", "I can barely move with all this weight!", "My pack is fit to burst!", "I need to put some of this down." };
		// while actually working the smelters
		s.Smelt = new string[] { "Feeding the furnace.", "Good ore makes good metal.", "Keeping the fires stoked.", "Another bar for the hoard.", "The bellows sing their song." };
		// nothing to do; [home] lines only at home
		s.Idle = new string[]
		{
			"Skål!", "I could use a horn of mead.", "A moment of peace... rare in these lands.", "The world is quiet for now.", "Waiting for your command.",
			"[home] Nice place we've got here.", "[home] A fine hall you've built.", "[home] I could get used to this.", "[home,night] Good to be home for the night.",
			"[away] Strange place to stop.", "[away,night] I'd rather not linger out here after dark.", "[rain] At least the rain keeps the dust down.",
			"[day,!rain,!indoors] Just enjoying the view.", "[indoors] Cosy in here."
		};
		// while actually hunting
		s.Hunt = new string[] { "Stay quiet...", "The hunt is on!", "Fresh meat for the pot.", "Tracking prey through the brush.", "Easy now... nice and steady..." };
		// while actually farming
		s.Farm = new string[] { "Working the fields.", "Seeds in the ground, hope in the heart.", "Farm work is honest work.", "Another row done.", "[rain] Good weather for the crops." };
		// while actually fishing
		s.Fish = new string[] { "Casting my line...", "Patience is a fisherman's greatest virtue.", "Come on, take the bait...", "Fishing is peaceful work.", "Nothing yet... but I'll wait." };
		// while actually cooking or brewing
		s.Cook = new string[] { "A good meal lifts any warrior's spirits.", "Stirring the pot...", "A pinch of this, a dash of that.", "Food for the weary!", "Mmm, smells good." };
		// while actually doing Repair and Restock
		s.Upkeep = new string[] { "Keeping the place in shape.", "A sound roof and a warm fire.", "Everything in its place.", "A home needs looking after." };
		// it has no bed (only said at night)
		s.NoBed = new string[] { "I've got no bed to call my own.", "Wish I had a bed around here.", "I'll sleep on the ground again, I suppose.", "A place to rest my head would be nice." };
		// idle chat next to you or another companion
		s.Chat = new string[]
		{
			"Need anything?", "Anything need doing?", "Quiet around here.", "[day,!rain] Fine day for it.", "[rain] Smells like rain. Oh wait.", "[night] Late, isn't it?",
			"[home] Good to have a roof over us.", "[away] Should we head home soon?"
		};
		return s;
	}
}
