using System.Collections.Generic;
using UnityEngine;

//======================================================================
// InGameLogger
// PC非接続のスマホ実機上で Debug.Log / Error を画面上部に表示する
//======================================================================
public class InGameLogger : MonoBehaviour
{
	private Queue<string> _logs = new Queue<string>();
	private const int MaxLogs = 12;
	private bool _isVisible = true;

	private void OnEnable()
	{
		Application.logMessageReceived += OnLogReceived;
	}

	private void OnDisable()
	{
		Application.logMessageReceived -= OnLogReceived;
	}

	private void OnLogReceived(string condition, string stackTrace, LogType type)
	{
		string color = type switch
		{
			LogType.Error => "red",
			LogType.Exception => "red",
			LogType.Warning => "yellow",
			_ => "white"
		};

		string log = $"<color={color}>[{type}] {condition}</color>";
		if (type == LogType.Exception || type == LogType.Error)
		{
			// エラー時は発生箇所のスタックトレースも1行追加
			string shortTrace = stackTrace.Split('\n')[0];
			log += $"\n<color=red>  --> {shortTrace}</color>";
		}

		_logs.Enqueue(log);
		while (_logs.Count > MaxLogs)
		{
			_logs.Dequeue();
		}
	}

	private void OnGUI()
	{
		// 画面右上：表示切り替えボタン
		float btnW = Screen.width * 0.25f;
		float btnH = Screen.height * 0.05f;
		if (GUI.Button(new Rect(Screen.width - btnW - 20, 20, btnW, btnH), _isVisible ? "LOG: ON" : "LOG: OFF"))
		{
			_isVisible = !_isVisible;
		}

		if (!_isVisible) return;

		// 画面上部：ログ表示領域
		GUIStyle boxStyle = new GUIStyle(GUI.skin.box)
		{
			fontSize = Mathf.Clamp(Mathf.RoundToInt(Screen.height * 0.016f), 12, 36),
			alignment = TextAnchor.UpperLeft,
			wordWrap = true,
			richText = true
		};

		float width = Screen.width - 40f;
		float height = Screen.height * 0.4f;
		GUI.Box(new Rect(20, btnH + 30, width, height), string.Join("\n", _logs.ToArray()), boxStyle);
	}
}