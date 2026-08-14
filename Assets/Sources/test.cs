using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

public class NetworkTest : MonoBehaviour
{
    private IEnumerator Start()
    {
        // 国土地理院のサンプルタイル（皇居周辺）を直接取得テスト
        string testUrl = "https://cyberjapandata.gsi.go.jp/xyz/std/16/58203/25807.png";
        using (UnityWebRequest www = UnityWebRequestTexture.GetTexture(testUrl))
        {
            yield return www.SendWebRequest();
            if (www.result == UnityWebRequest.Result.Success)
            {
                Debug.Log("<color=green>[通信成功]</color> タイル画像を正常に取得できました！");
            }
            else
            {
                Debug.LogError($"[通信失敗] HTTPエラーまたは遮断: {www.error} (Code: {www.responseCode})");
            }
        }
    }
}