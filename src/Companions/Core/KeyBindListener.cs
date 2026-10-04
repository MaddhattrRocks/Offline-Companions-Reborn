using System;
using BepInEx.Configuration;
using TMPro;
using UnityEngine;

namespace Companions;

internal class KeyBindListener : MonoBehaviour
{
	private ConfigEntry<KeyCode> _entry;

	private TMP_Text _label;

	private Func<bool> _isListening;

	private Action<bool> _setListening;

	internal void Init(ConfigEntry<KeyCode> entry, TMP_Text label, Func<bool> isListening, Action<bool> setListening)
	{
		_entry = entry;
		_label = label;
		_isListening = isListening;
		_setListening = setListening;
	}

	private void Update()
	{
		if (_isListening == null || !_isListening() || !Input.anyKeyDown)
		{
			return;
		}
		if (Input.GetKeyDown(KeyCode.Escape))
		{
			_label.text = _entry.Value.ToString();
			_label.color = new Color(0.83f, 0.52f, 0.18f, 1f);
			_setListening(obj: false);
			return;
		}
		foreach (KeyCode value in Enum.GetValues(typeof(KeyCode)))
		{
			if (value != KeyCode.None && Input.GetKeyDown(value))
			{
				_entry.Value = value;
				_label.text = value.ToString();
				_label.color = new Color(0.83f, 0.52f, 0.18f, 1f);
				_setListening(obj: false);
				break;
			}
		}
	}
}
