using Fusion;
using Fusion.Sockets;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class NetworkManager : MonoBehaviour, INetworkRunnerCallbacks
{
    [SerializeField] private NetworkPrefabRef tankPrefab; // 戦車Prefab（後で設定）
    private NetworkRunner runner;
    private Dictionary<PlayerRef, NetworkObject> spawnedPlayers = new Dictionary<PlayerRef, NetworkObject>();

    // ホストとして開始
    public async void StartHost()
    {
        Debug.Log("Starting as Host...");
        
        runner = gameObject.AddComponent<NetworkRunner>();
        runner.ProvideInput = true;
        
        // ★重要: コールバックを登録
        runner.AddCallbacks(this);

        var startGameArgs = new StartGameArgs()
        {
            GameMode = GameMode.Host, // ホストモード
            SessionName = "TanksRoom", // ルーム名
            SceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>()
        };

        var result = await runner.StartGame(startGameArgs);
        
        if (result.Ok)
        {
            Debug.Log("Host started successfully!");
        }
        else
        {
            Debug.LogError($"Failed to start host: {result.ShutdownReason}");
        }
    }

    // クライアントとして参加
    public async void StartClient()
    {
        Debug.Log("Starting as Client...");
        
        runner = gameObject.AddComponent<NetworkRunner>();
        runner.ProvideInput = true;
        
        // ★重要: コールバックを登録
        runner.AddCallbacks(this);

        var startGameArgs = new StartGameArgs()
        {
            GameMode = GameMode.Client, // クライアントモード
            SessionName = "TanksRoom", // 同じルーム名
            SceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>()
        };

        var result = await runner.StartGame(startGameArgs);
        
        if (result.Ok)
        {
            Debug.Log("Client connected successfully!");
        }
        else
        {
            Debug.LogError($"Failed to connect as client: {result.ShutdownReason}");
        }
    }

    // プレイヤーが参加した時（ホスト側のみ実行）
    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    {
        if (runner.IsServer) // ホスト側のみ実行
        {
            Debug.Log($"Player {player} joined. Spawning tank...");
            
            // スポーン位置を決定
            Vector3 spawnPosition = Vector3.zero;
            Quaternion spawnRotation = Quaternion.identity;
            
            // プレイヤー番号に応じてスポーン位置を変更
            int playerIndex = player.PlayerId;
            if (playerIndex == 0)
            {
                spawnPosition = new Vector3(0, 0, 0);
                spawnRotation = Quaternion.Euler(0, 90, 0);
            }
            else
            {
                spawnPosition = new Vector3(0, 0, 20);
                spawnRotation = Quaternion.Euler(0, -90, 0);
            }

            // 戦車をスポーン（tankPrefabが設定されている場合）
            if (tankPrefab != null)
            {
                NetworkObject tank = runner.Spawn(tankPrefab, spawnPosition, spawnRotation, player);
                spawnedPlayers[player] = tank;
                Debug.Log($"Tank spawned for player {player} at {spawnPosition}");
            }
            else
            {
                Debug.LogWarning("Tank prefab is not assigned in NetworkManager!");
            }
        }
    }

    // プレイヤーが退出した時
    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    {
        Debug.Log($"Player {player} left.");
        
        if (spawnedPlayers.TryGetValue(player, out NetworkObject networkObject))
        {
            if (networkObject != null)
            {
                runner.Despawn(networkObject);
            }
            spawnedPlayers.Remove(player);
        }
    }

    // 入力データを収集（各クライアントで実行）
    public void OnInput(NetworkRunner runner, NetworkInput input)
    {
        var inputData = new Complete.NetworkInputData();

        // キーボード入力を取得
        inputData.movementInput = new Vector2(Input.GetAxis("Horizontal"), Input.GetAxis("Vertical"));
        inputData.fireButton = Input.GetButton("Fire1");

        // デバッグ用
        if (inputData.movementInput != Vector2.zero)
        {
            Debug.Log($"Input detected: {inputData.movementInput}");
        }

        // 入力データをネットワークに送信
        input.Set(inputData);
    }

    // 以下は必須のインターフェース実装
    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason) 
    {
        Debug.Log($"Network shutdown: {shutdownReason}");
    }
    public void OnConnectedToServer(NetworkRunner runner) 
    {
        Debug.Log("Connected to server");
    }
    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) 
    {
        Debug.Log($"Disconnected from server: {reason}");
    }
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) 
    {
        Debug.LogError($"Connection failed: {reason}");
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
}
