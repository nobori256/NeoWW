using UnityEngine;
using UnityEditor;
using System;
using System.IO;

[CreateAssetMenu(fileName = "BuildVersionInfo", menuName = "Config/BuildVersionInfo")]
public class BuildVersionInfo : ScriptableObject
{
    [SerializeField] private string buildTime;
    public string BuildTime => buildTime;

    private static string AssetPath => "Assets/Resources/BuildVersionInfo.asset";

#if UNITY_EDITOR
    public static void UpdateVersion()
    {
        string timeStr = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss");
        
        // Resourcesフォルダの存在確認
        if (!Directory.Exists("Assets/Resources"))
        {
            Directory.CreateDirectory("Assets/Resources");
        }

        BuildVersionInfo info = AssetDatabase.LoadAssetAtPath<BuildVersionInfo>(AssetPath);
        if (info == null)
        {
            info = ScriptableObject.CreateInstance<BuildVersionInfo>();
            AssetDatabase.CreateAsset(info, AssetPath);
        }

        info.buildTime = timeStr;
        EditorUtility.SetDirty(info);
        AssetDatabase.SaveAssets();
        Debug.Log($"[BuildVersion] バージョンを更新しました: {timeStr}");
    }
#endif

    public static string GetVersionString()
    {
        // Resourcesから読み込むため、ビルド後も確実に取得可能
        BuildVersionInfo info = Resources.Load<BuildVersionInfo>("BuildVersionInfo");
        if (info != null)
        {
            return info.buildTime;
        }
        return "Unknown Build";
    }
}

#if UNITY_EDITOR
// ビルド開始時に自動でバージョンを更新するフック
public class BuildVersionPreprocess : UnityEditor.Build.IPreprocessBuildWithReport
{
    public int callbackOrder => 0;

    public void OnPreprocessBuild(UnityEditor.Build.Reporting.BuildReport report)
    {
        BuildVersionInfo.UpdateVersion();
    }
}
#endif