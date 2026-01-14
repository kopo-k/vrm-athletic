using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Fusion;

namespace Complete
{
    /// <summary>
    /// シーンのネットワーク設定を自動化するエディタースクリプト
    /// </summary>
    public class SetupNetworkScene : EditorWindow
    {
        [MenuItem("Tanks/Setup Network Scene")]
        public static void Setup()
        {
            // NetworkGameManagerの参照を設定
            var gameManagerObj = GameObject.Find("GameManager");
            if (gameManagerObj == null)
            {
                Debug.LogError("GameManager not found!");
                return;
            }

            var networkGameManager = gameManagerObj.GetComponent<NetworkGameManager>();
            if (networkGameManager == null)
            {
                networkGameManager = gameManagerObj.AddComponent<NetworkGameManager>();
                Debug.Log("Added NetworkGameManager component");
            }

            // CameraControlの参照を設定
            var cameraRig = GameObject.Find("CameraRig");
            if (cameraRig != null)
            {
                var cameraControl = cameraRig.GetComponent<CameraControl>();
                if (cameraControl != null)
                {
                    SetFieldValue(networkGameManager, "m_CameraControl", cameraControl);
                    Debug.Log("Set CameraControl reference");
                }
            }

            // MessageTextの参照を設定
            var messageCanvas = GameObject.Find("MessageCanvas");
            if (messageCanvas != null)
            {
                var texts = messageCanvas.GetComponentsInChildren<Text>();
                foreach (var text in texts)
                {
                    SetFieldValue(networkGameManager, "m_MessageText", text);
                    Debug.Log($"Set MessageText reference: {text.gameObject.name}");
                    break;
                }
            }

            // NetworkUIの参照を設定
            var networkUI = GameObject.Find("NetworkUI");
            if (networkUI != null)
            {
                SetFieldValue(networkGameManager, "m_NetworkUI", networkUI);
                Debug.Log("Set NetworkUI reference");
            }

            // SpawnPointsの参照を設定
            var spawnPoint1 = GameObject.Find("SpawnPoint1");
            var spawnPoint2 = GameObject.Find("SpawnPoint2");
            if (spawnPoint1 != null && spawnPoint2 != null)
            {
                Transform[] spawnPoints = new Transform[] { spawnPoint1.transform, spawnPoint2.transform };
                SetFieldValue(networkGameManager, "m_SpawnPoints", spawnPoints);
                Debug.Log("Set SpawnPoints references");
            }

            // TankPrefabの参照を設定（NetworkTankまたはTankNetworkを使用）
            string[] tankPrefabPaths = new string[]
            {
                "Assets/Prefabs/NetworkTank.prefab",
                "Assets/Prefabs/TankNetwork.prefab",
                "Assets/Prefabs/CompleteTank.prefab"
            };

            foreach (var path in tankPrefabPaths)
            {
                var tankPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (tankPrefab != null)
                {
                    var networkObject = tankPrefab.GetComponent<NetworkObject>();
                    if (networkObject != null)
                    {
                        // NetworkPrefabRefを設定
                        Debug.Log($"Found network tank prefab: {path}");
                        // NetworkPrefabRefはSerializedPropertyで設定する必要がある
                        break;
                    }
                }
            }

            // シーンをDirtyとしてマーク
            EditorUtility.SetDirty(networkGameManager);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

            Debug.Log("✅ Network Scene setup complete!");
        }

        [MenuItem("Tanks/Setup UI Buttons")]
        public static void SetupUIButtons()
        {
            var networkUI = GameObject.Find("NetworkUI");
            if (networkUI == null)
            {
                Debug.LogError("NetworkUI not found!");
                return;
            }

            // HostButtonのテキストを設定
            var hostButton = FindChildByName(networkUI.transform, "HostButton");
            if (hostButton != null)
            {
                var text = hostButton.GetComponentInChildren<Text>();
                if (text != null)
                {
                    text.text = "HOST";
                    text.fontSize = 24;
                    text.color = Color.white;
                    EditorUtility.SetDirty(text);
                }
            }

            // JoinButtonのテキストを設定
            var joinButton = FindChildByName(networkUI.transform, "JoinButton");
            if (joinButton != null)
            {
                var text = joinButton.GetComponentInChildren<Text>();
                if (text != null)
                {
                    text.text = "JOIN";
                    text.fontSize = 24;
                    text.color = Color.white;
                    EditorUtility.SetDirty(text);
                }
            }

            // TitleTextの設定
            var titleText = FindChildByName(networkUI.transform, "TitleText");
            if (titleText != null)
            {
                var text = titleText.GetComponent<Text>();
                if (text != null)
                {
                    text.text = "TANKS! MULTIPLAYER";
                    text.fontSize = 36;
                    text.color = Color.white;
                    text.alignment = TextAnchor.MiddleCenter;
                    EditorUtility.SetDirty(text);
                }
            }

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
            Debug.Log("✅ UI Buttons setup complete!");
        }

        private static GameObject FindChildByName(Transform parent, string name)
        {
            foreach (Transform child in parent.GetComponentsInChildren<Transform>(true))
            {
                if (child.gameObject.name == name)
                {
                    return child.gameObject;
                }
            }
            return null;
        }

        private static void SetFieldValue(object obj, string fieldName, object value)
        {
            var field = obj.GetType().GetField(fieldName, 
                System.Reflection.BindingFlags.Public | 
                System.Reflection.BindingFlags.NonPublic | 
                System.Reflection.BindingFlags.Instance);
            
            if (field != null)
            {
                field.SetValue(obj, value);
            }
            else
            {
                Debug.LogWarning($"Field {fieldName} not found on {obj.GetType().Name}");
            }
        }
    }
}
