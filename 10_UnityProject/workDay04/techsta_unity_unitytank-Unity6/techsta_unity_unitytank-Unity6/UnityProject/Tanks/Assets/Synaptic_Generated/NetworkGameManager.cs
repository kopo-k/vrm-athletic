using Fusion;
using Fusion.Sockets;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

namespace Complete
{
    /// <summary>
    /// Photon Fusion対応のゲームマネージャー
    /// 【ポイント】全てのゲームロジックはホスト側で実行し、RPCで全クライアントに通知
    /// </summary>
    public class NetworkGameManager : MonoBehaviour, INetworkRunnerCallbacks
    {
        [Header("Game Settings")]
        public int m_NumRoundsToWin = 5;
        public float m_StartDelay = 3f;
        public float m_EndDelay = 3f;

        [Header("References")]
        public CameraControl m_CameraControl;
        public Text m_MessageText;
        public NetworkPrefabRef m_TankPrefab;

        [Header("Spawn Points")]
        public Transform[] m_SpawnPoints;
        public Color[] m_PlayerColors = { Color.blue, Color.red };

        [Header("UI")]
        public GameObject m_NetworkUI;  // Host/Joinボタンを含むUI

        // ネットワーク関連
        private NetworkRunner m_Runner;
        private Dictionary<PlayerRef, NetworkObject> m_SpawnedTanks = new Dictionary<PlayerRef, NetworkObject>();
        private Dictionary<PlayerRef, int> m_PlayerWins = new Dictionary<PlayerRef, int>();
        private List<PlayerRef> m_ConnectedPlayers = new List<PlayerRef>();

        // ゲーム状態
        private int m_RoundNumber;
        private bool m_GameStarted = false;
        private bool m_IsHost = false;

        // 入力追跡用
        private bool m_PreviousFireButton = false;

        // 【重要】入力をUpdateで収集してOnInputで使用（Fusion推奨パターン）
        private Vector2 m_CachedMovementInput = Vector2.zero;
        private bool m_CachedFireButton = false;
        private bool m_CachedFireButtonDown = false;
        private bool m_CachedFireButtonUp = false;

        // カメラ更新用
        private float m_CameraUpdateTimer = 0f;
        private const float CAMERA_UPDATE_INTERVAL = 0.5f;

        // デバッグ用タイマー
        private float m_DebugTimer = 0f;

        private void Update()
        {
            // 【重要】入力はUpdateで収集する（OnInputはFixedUpdate相当のタイミングで呼ばれるため）
            CollectInput();

            // 定期的にカメラターゲットを更新（クライアント側でも動作）
            m_CameraUpdateTimer += Time.deltaTime;
            if (m_CameraUpdateTimer >= CAMERA_UPDATE_INTERVAL)
            {
                m_CameraUpdateTimer = 0f;
                UpdateCameraTargetsFromScene();
            }

            // 5秒ごとにネットワーク状態をログ出力
            if (m_Runner != null)
            {
                m_DebugTimer += Time.deltaTime;
                if (m_DebugTimer >= 5f)
                {
                    m_DebugTimer = 0f;
                    Debug.Log($"=== Network Status ===");
                    Debug.Log($"  IsRunning: {m_Runner.IsRunning}");
                    Debug.Log($"  IsServer: {m_Runner.IsServer}");
                    Debug.Log($"  IsClient: {m_Runner.IsClient}");
                    Debug.Log($"  LocalPlayer: {m_Runner.LocalPlayer}");
                    Debug.Log($"  PlayerCount: {m_Runner.ActivePlayers.Count()}");
                    Debug.Log($"  SessionInfo: {m_Runner.SessionInfo?.Name ?? "null"}");
                    Debug.Log($"  SpawnedTanks: {m_SpawnedTanks.Count}");
                    Debug.Log($"======================");
                }
            }
        }

        /// <summary>
        /// Updateで入力を収集し、キャッシュに保存
        /// </summary>
        private void CollectInput()
        {
            // 移動入力
            float horizontal = 0f;
            float vertical = 0f;

            // WASDキー
            if (Input.GetKey(KeyCode.A)) horizontal = -1f;
            else if (Input.GetKey(KeyCode.D)) horizontal = 1f;

            if (Input.GetKey(KeyCode.W)) vertical = 1f;
            else if (Input.GetKey(KeyCode.S)) vertical = -1f;

            // 矢印キー（WASDが押されていない場合）
            if (horizontal == 0f)
            {
                if (Input.GetKey(KeyCode.LeftArrow)) horizontal = -1f;
                else if (Input.GetKey(KeyCode.RightArrow)) horizontal = 1f;
            }

            if (vertical == 0f)
            {
                if (Input.GetKey(KeyCode.UpArrow)) vertical = 1f;
                else if (Input.GetKey(KeyCode.DownArrow)) vertical = -1f;
            }

            m_CachedMovementInput = new Vector2(horizontal, vertical);

            // 射撃ボタン
            bool currentFireButton = Input.GetKey(KeyCode.Space) ||
                                    Input.GetKey(KeyCode.Return) ||
                                    Input.GetMouseButton(0);

            m_CachedFireButtonDown = currentFireButton && !m_CachedFireButton;
            m_CachedFireButtonUp = !currentFireButton && m_CachedFireButton;
            m_CachedFireButton = currentFireButton;

            // デバッグ：入力があったときにログ出力
            if (m_CachedMovementInput != Vector2.zero)
            {
                Debug.Log($"[CollectInput] Movement: {m_CachedMovementInput}");
            }
        }

        /// <summary>
        /// シーン内のタンクを検索してカメラターゲットを更新（ホスト・クライアント両方で動作）
        /// </summary>
        private void UpdateCameraTargetsFromScene()
        {
            if (m_CameraControl == null) return;

            try
            {
                // シーン内の全てのTankNetworkControllerを検索
                TankNetworkController[] tanks = FindObjectsByType<TankNetworkController>(FindObjectsSortMode.None);

                if (tanks == null || tanks.Length == 0) return;

                List<Transform> targets = new List<Transform>();
                foreach (var tank in tanks)
                {
                    // null チェックと破棄チェックを強化
                    if (tank != null && tank.gameObject != null && tank.gameObject.activeSelf)
                    {
                        targets.Add(tank.transform);
                    }
                }

                // カメラターゲットが変更された場合のみ更新
                if (targets.Count > 0 && (m_CameraControl.m_Targets == null || m_CameraControl.m_Targets.Length != targets.Count))
                {
                    m_CameraControl.m_Targets = targets.ToArray();
                    m_CameraControl.SetStartPositionAndSize();
                    Debug.Log($"[NetworkGameManager] Camera targets updated: {targets.Count} tanks");
                }
            }
            catch (System.Exception)
            {
                // 破棄されたオブジェクトへのアクセスは無視
            }
        }

        private void Start()
        {
            Debug.Log("[NetworkGameManager] Start() called - waiting for Host/Client button press");
        }

        #region Network Connection
        /// <summary>
        /// ホストとして開始
        /// </summary>
        public async void StartHost()
        {
            Debug.Log("[NetworkGameManager] ========== Starting as Host ==========");

            // 既存のNetworkRunnerを削除して新規作成
            NetworkRunner existingRunner = GetComponent<NetworkRunner>();
            if (existingRunner != null)
            {
                Debug.Log("[NetworkGameManager] Removing existing NetworkRunner");
                Destroy(existingRunner);
            }

            // 既存のNetworkSceneManagerも削除
            NetworkSceneManagerDefault existingSceneManager = GetComponent<NetworkSceneManagerDefault>();
            if (existingSceneManager != null)
            {
                Destroy(existingSceneManager);
            }

            // 少し待ってから新規作成
            await System.Threading.Tasks.Task.Delay(100);

            m_Runner = gameObject.AddComponent<NetworkRunner>();
            m_Runner.ProvideInput = true;
            m_Runner.AddCallbacks(this);
            Debug.Log($"[NetworkGameManager] NetworkRunner created. ProvideInput: {m_Runner.ProvideInput}");

            var startGameArgs = new StartGameArgs()
            {
                GameMode = GameMode.Host,
                SessionName = "TanksRoom",
                SceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>()
            };

            var result = await m_Runner.StartGame(startGameArgs);

            if (result.Ok)
            {
                Debug.Log("[NetworkGameManager] ✅ Host started successfully!");
                Debug.Log($"[NetworkGameManager] LocalPlayer: {m_Runner.LocalPlayer}");
                m_IsHost = true;
                HideNetworkUI();
            }
            else
            {
                Debug.LogError($"[NetworkGameManager] ❌ Failed to start host: {result.ShutdownReason}");
            }
        }

        /// <summary>
        /// クライアントとして参加
        /// </summary>
        public async void StartClient()
        {
            Debug.Log("[NetworkGameManager] ========== Starting as Client ==========");

            // 既存のNetworkRunnerを削除して新規作成
            NetworkRunner existingRunner = GetComponent<NetworkRunner>();
            if (existingRunner != null)
            {
                Debug.Log("[NetworkGameManager] Removing existing NetworkRunner");
                Destroy(existingRunner);
            }

            // 既存のNetworkSceneManagerも削除
            NetworkSceneManagerDefault existingSceneManager = GetComponent<NetworkSceneManagerDefault>();
            if (existingSceneManager != null)
            {
                Destroy(existingSceneManager);
            }

            // 少し待ってから新規作成
            await System.Threading.Tasks.Task.Delay(100);

            m_Runner = gameObject.AddComponent<NetworkRunner>();
            m_Runner.ProvideInput = true;
            m_Runner.AddCallbacks(this);
            Debug.Log($"[NetworkGameManager] NetworkRunner created. ProvideInput: {m_Runner.ProvideInput}");

            var startGameArgs = new StartGameArgs()
            {
                GameMode = GameMode.Client,
                SessionName = "TanksRoom",
                SceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>()
            };

            var result = await m_Runner.StartGame(startGameArgs);

            if (result.Ok)
            {
                Debug.Log("[NetworkGameManager] ✅ Client connected successfully!");
                Debug.Log($"[NetworkGameManager] LocalPlayer: {m_Runner.LocalPlayer}");
                HideNetworkUI();
            }
            else
            {
                Debug.LogError($"[NetworkGameManager] ❌ Failed to connect as client: {result.ShutdownReason}");
            }
        }

        private void HideNetworkUI()
        {
            if (m_NetworkUI != null)
            {
                m_NetworkUI.SetActive(false);
            }
        }
        #endregion

        #region INetworkRunnerCallbacks
        public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
        {
            Debug.Log($"[NetworkGameManager] Player {player.PlayerId} joined");

            if (runner.IsServer)
            {
                // プレイヤーをリストに追加
                if (!m_ConnectedPlayers.Contains(player))
                {
                    m_ConnectedPlayers.Add(player);
                    m_PlayerWins[player] = 0;
                }

                // スポーン位置を決定
                int playerIndex = m_ConnectedPlayers.IndexOf(player);
                Vector3 spawnPos = Vector3.zero;
                Quaternion spawnRot = Quaternion.identity;

                if (m_SpawnPoints != null && playerIndex < m_SpawnPoints.Length)
                {
                    spawnPos = m_SpawnPoints[playerIndex].position;
                    spawnRot = m_SpawnPoints[playerIndex].rotation;
                }
                else
                {
                    // デフォルトのスポーン位置
                    spawnPos = playerIndex == 0 ? new Vector3(-10, 0, 0) : new Vector3(10, 0, 0);
                    spawnRot = playerIndex == 0 ? Quaternion.Euler(0, 90, 0) : Quaternion.Euler(0, -90, 0);
                }

                // タンクをスポーン
                if (m_TankPrefab.IsValid)
                {
                    var tank = runner.Spawn(m_TankPrefab, spawnPos, spawnRot, player,
                        onBeforeSpawned: (runner, obj) =>
                        {
                            var tankController = obj.GetComponent<TankNetworkController>();
                            if (tankController != null)
                            {
                                tankController.PlayerNumber = playerIndex + 1;
                            }
                        });

                    m_SpawnedTanks[player] = tank;

                    // 色を設定
                    var controller = tank.GetComponent<TankNetworkController>();
                    if (controller != null && playerIndex < m_PlayerColors.Length)
                    {
                        controller.SetTankColor(m_PlayerColors[playerIndex]);
                    }

                    Debug.Log($"[NetworkGameManager] Tank spawned for player {player.PlayerId} at {spawnPos}");

                    // カメラターゲットを更新
                    UpdateCameraTargets();

                    // 2人揃ったらゲーム開始
                    if (m_ConnectedPlayers.Count >= 2 && !m_GameStarted)
                    {
                        StartCoroutine(GameLoop());
                    }
                }
            }
        }

        public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
        {
            Debug.Log($"[NetworkGameManager] Player {player.PlayerId} left");

            if (m_SpawnedTanks.TryGetValue(player, out NetworkObject tank))
            {
                if (tank != null && runner.IsServer)
                {
                    runner.Despawn(tank);
                }
                m_SpawnedTanks.Remove(player);
            }

            m_ConnectedPlayers.Remove(player);
            m_PlayerWins.Remove(player);
        }

        // OnInputデバッグ用カウンター
        private int m_OnInputCallCount = 0;

        public void OnInput(NetworkRunner runner, NetworkInput input)
        {
            m_OnInputCallCount++;
            // 100フレームに1回ログ出力（OnInputが呼ばれているか確認）
            if (m_OnInputCallCount % 100 == 1)
            {
                Debug.Log($"[OnInput] Called {m_OnInputCallCount} times. IsHost:{m_IsHost} LocalPlayer:{runner.LocalPlayer} ProvideInput:{runner.ProvideInput}");
            }

            var inputData = new NetworkInputData();

            // 【重要】Updateでキャッシュした入力値を使用
            inputData.movementInput = m_CachedMovementInput;
            inputData.fireButton = m_CachedFireButton;
            inputData.fireButtonDown = m_CachedFireButtonDown;
            inputData.fireButtonUp = m_CachedFireButtonUp;

            // デバッグ出力（入力があるときのみ）
            if (inputData.movementInput != Vector2.zero || inputData.fireButton)
            {
                Debug.Log($"[OnInput] IsHost:{m_IsHost} Movement:{inputData.movementInput} Fire:{inputData.fireButton}");
            }

            input.Set(inputData);

            // ButtonDown/Upはフレームごとにリセット
            m_CachedFireButtonDown = false;
            m_CachedFireButtonUp = false;
        }
        #endregion

        #region Game Loop
        private IEnumerator GameLoop()
        {
            m_GameStarted = true;
            Debug.Log("[NetworkGameManager] Game starting!");

            yield return StartCoroutine(RoundStarting());
            yield return StartCoroutine(RoundPlaying());
            yield return StartCoroutine(RoundEnding());

            // ゲーム勝者チェック
            PlayerRef? gameWinner = GetGameWinner();
            if (gameWinner.HasValue)
            {
                // ゲーム終了 - シーンリロード
                SetMessageText($"PLAYER {m_ConnectedPlayers.IndexOf(gameWinner.Value) + 1} WINS THE GAME!");
                yield return new WaitForSeconds(m_EndDelay);
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
            }
            else
            {
                // 次のラウンドへ
                StartCoroutine(GameLoop());
            }
        }

        private IEnumerator RoundStarting()
        {
            Debug.Log("[NetworkGameManager] Round Starting");

            // タンクをリセット
            ResetAllTanks();
            DisableTankControl();

            // カメラ設定
            if (m_CameraControl != null)
            {
                m_CameraControl.SetStartPositionAndSize();
            }

            m_RoundNumber++;
            SetMessageText($"ROUND {m_RoundNumber}");

            yield return new WaitForSeconds(m_StartDelay);
        }

        private IEnumerator RoundPlaying()
        {
            Debug.Log("[NetworkGameManager] Round Playing");

            EnableTankControl();
            SetMessageText(string.Empty);

            while (!OneTankLeft())
            {
                yield return null;
            }
        }

        private IEnumerator RoundEnding()
        {
            Debug.Log("[NetworkGameManager] Round Ending");

            DisableTankControl();

            PlayerRef? roundWinner = GetRoundWinner();

            if (roundWinner.HasValue)
            {
                if (m_PlayerWins.ContainsKey(roundWinner.Value))
                {
                    m_PlayerWins[roundWinner.Value]++;
                }
            }

            string message = EndMessage(roundWinner);
            SetMessageText(message);

            yield return new WaitForSeconds(m_EndDelay);
        }

        private bool OneTankLeft()
        {
            int tanksLeft = 0;
            foreach (var kvp in m_SpawnedTanks)
            {
                if (kvp.Value != null && kvp.Value.gameObject.activeSelf)
                {
                    var controller = kvp.Value.GetComponent<TankNetworkController>();
                    if (controller != null && !controller.IsDead)
                    {
                        tanksLeft++;
                    }
                }
            }
            return tanksLeft <= 1;
        }

        private PlayerRef? GetRoundWinner()
        {
            foreach (var kvp in m_SpawnedTanks)
            {
                if (kvp.Value != null && kvp.Value.gameObject.activeSelf)
                {
                    var controller = kvp.Value.GetComponent<TankNetworkController>();
                    if (controller != null && !controller.IsDead)
                    {
                        return kvp.Key;
                    }
                }
            }
            return null;
        }

        private PlayerRef? GetGameWinner()
        {
            foreach (var kvp in m_PlayerWins)
            {
                if (kvp.Value >= m_NumRoundsToWin)
                {
                    return kvp.Key;
                }
            }
            return null;
        }

        private string EndMessage(PlayerRef? winner)
        {
            string message = "DRAW!";

            if (winner.HasValue)
            {
                int playerNum = m_ConnectedPlayers.IndexOf(winner.Value) + 1;
                message = $"PLAYER {playerNum} WINS THE ROUND!";
            }

            message += "\n\n\n\n";

            foreach (var player in m_ConnectedPlayers)
            {
                int playerNum = m_ConnectedPlayers.IndexOf(player) + 1;
                int wins = m_PlayerWins.ContainsKey(player) ? m_PlayerWins[player] : 0;
                message += $"PLAYER {playerNum}: {wins} WINS\n";
            }

            return message;
        }

        private void SetMessageText(string message)
        {
            if (m_MessageText != null)
            {
                m_MessageText.text = message;
            }
        }
        #endregion

        #region Tank Control
        private void ResetAllTanks()
        {
            int index = 0;
            foreach (var kvp in m_SpawnedTanks)
            {
                if (kvp.Value != null)
                {
                    var controller = kvp.Value.GetComponent<TankNetworkController>();
                    if (controller != null)
                    {
                        Vector3 spawnPos;
                        Quaternion spawnRot;

                        if (m_SpawnPoints != null && index < m_SpawnPoints.Length)
                        {
                            spawnPos = m_SpawnPoints[index].position;
                            spawnRot = m_SpawnPoints[index].rotation;
                        }
                        else
                        {
                            spawnPos = index == 0 ? new Vector3(-10, 0, 0) : new Vector3(10, 0, 0);
                            spawnRot = index == 0 ? Quaternion.Euler(0, 90, 0) : Quaternion.Euler(0, -90, 0);
                        }

                        controller.ResetTank(spawnPos, spawnRot);
                    }
                }
                index++;
            }
        }

        private void EnableTankControl()
        {
            foreach (var kvp in m_SpawnedTanks)
            {
                if (kvp.Value != null)
                {
                    kvp.Value.gameObject.SetActive(true);
                }
            }
        }

        private void DisableTankControl()
        {
            // タンクの操作を無効化（Networkedプロパティで制御）
        }

        private void UpdateCameraTargets()
        {
            if (m_CameraControl == null) return;

            List<Transform> targets = new List<Transform>();
            foreach (var kvp in m_SpawnedTanks)
            {
                if (kvp.Value != null)
                {
                    targets.Add(kvp.Value.transform);
                }
            }
            m_CameraControl.m_Targets = targets.ToArray();
        }
        #endregion

        #region Unused Callbacks
        public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
        public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason) 
        {
            Debug.Log($"[NetworkGameManager] Shutdown: {shutdownReason}");
        }
        public void OnConnectedToServer(NetworkRunner runner) 
        {
            Debug.Log("[NetworkGameManager] Connected to server");
        }
        public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) 
        {
            Debug.Log($"[NetworkGameManager] Disconnected: {reason}");
        }
        public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
        public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) 
        {
            Debug.LogError($"[NetworkGameManager] Connection failed: {reason}");
        }
        public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
        public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
        public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
        public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
        public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
        public void OnSceneLoadDone(NetworkRunner runner) { }
        public void OnSceneLoadStart(NetworkRunner runner) { }
        public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
        public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
        public void OnDisconnectedFromServer(NetworkRunner runner) { }
        public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ArraySegment<byte> data) { }
        public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
        #endregion
    }
}
