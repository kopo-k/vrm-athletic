using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class MazeGameManager : MonoBehaviour
{
    public static MazeGameManager Instance { get; private set; }
    
    [Header("UI References")]
    public Text timerText;
    public Text messageText;
    public Text coinText;
    public GameObject winPanel;
    public Text winText;
    public Button restartButton;
    
    [Header("Game Settings")]
    public Transform player;
    public Transform goal;
    public float goalRadius = 2f;
    
    private float gameTime = 0f;
    private bool gameEnded = false;
    private bool gameStarted = false;
    private int collectedCoins = 0;
    private int totalCoins = 0;
    
    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }
    
    void Start()
    {
        // プレイヤーとゴールを自動検索
        if (player == null)
        {
            GameObject playerObj = GameObject.Find("Player");
            if (playerObj != null) player = playerObj.transform;
        }
        
        if (goal == null)
        {
            GameObject goalObj = GameObject.Find("Goal");
            if (goalObj != null) goal = goalObj.transform;
        }
        
        // UI要素を自動検索
        if (timerText == null)
        {
            GameObject timerObj = GameObject.Find("TimerText");
            if (timerObj != null) timerText = timerObj.GetComponent<Text>();
        }
        
        if (messageText == null)
        {
            GameObject msgObj = GameObject.Find("MessageText");
            if (msgObj != null) messageText = msgObj.GetComponent<Text>();
        }
        
        if (coinText == null)
        {
            GameObject coinObj = GameObject.Find("CoinText");
            if (coinObj != null) coinText = coinObj.GetComponent<Text>();
        }
        
        if (winPanel == null)
        {
            GameObject panelObj = GameObject.Find("WinPanel");
            if (panelObj != null) winPanel = panelObj;
        }
        
        if (winText == null)
        {
            GameObject winTextObj = GameObject.Find("WinText");
            if (winTextObj != null) winText = winTextObj.GetComponent<Text>();
        }
        
        if (restartButton == null)
        {
            GameObject btnObj = GameObject.Find("RestartButton");
            if (btnObj != null)
            {
                restartButton = btnObj.GetComponent<Button>();
                restartButton.onClick.AddListener(RestartGame);
            }
        }
        
        // コイン数をカウント
        totalCoins = FindObjectsOfType<Collectible>().Length;
        
        // 初期UI設定 - クリア画面を非表示
        if (winPanel != null)
        {
            winPanel.SetActive(false);
        }
        
        // WinTextとRestartButtonも初期非表示
        if (winText != null)
        {
            winText.gameObject.SetActive(false);
        }
        
        if (restartButton != null)
        {
            restartButton.gameObject.SetActive(false);
        }
        
        if (timerText != null)
        {
            timerText.text = "00:00.00";
        }
        
        if (messageText != null)
        {
            messageText.text = "";
        }
        
        if (coinText != null)
        {
            coinText.text = string.Format("コイン: 0/{0}", totalCoins);
        }
        
        gameStarted = true;
    }
    
    void Update()
    {
        if (!gameStarted || gameEnded) return;
        
        // タイマー更新
        gameTime += Time.deltaTime;
        UpdateTimerDisplay();
        
        // ゴール判定
        if (player != null && goal != null)
        {
            float distance = Vector3.Distance(player.position, goal.position);
            if (distance < goalRadius)
            {
                GameClear();
            }
        }
        
        // リスタート
        if (Input.GetKeyDown(KeyCode.R))
        {
            RestartGame();
        }
    }
    
    void UpdateTimerDisplay()
    {
        if (timerText != null)
        {
            int minutes = Mathf.FloorToInt(gameTime / 60);
            int seconds = Mathf.FloorToInt(gameTime % 60);
            int milliseconds = Mathf.FloorToInt((gameTime * 100) % 100);
            timerText.text = string.Format("{0:00}:{1:00}.{2:00}", minutes, seconds, milliseconds);
        }
    }
    
    void UpdateCoinDisplay()
    {
        if (coinText != null)
        {
            coinText.text = string.Format("コイン: {0}/{1}", collectedCoins, totalCoins);
        }
    }
    
    public void CollectItem()
    {
        collectedCoins++;
        UpdateCoinDisplay();
        
        if (messageText != null)
        {
            messageText.text = "コインゲット！";
            Invoke("ClearMessage", 1f);
        }
    }
    
    void ClearMessage()
    {
        if (messageText != null && !gameEnded)
        {
            messageText.text = "";
        }
    }
    
    void GameClear()
    {
        gameEnded = true;
        
        if (winPanel != null)
        {
            winPanel.SetActive(true);
        }
        
        // WinTextとRestartButtonを表示
        if (winText != null)
        {
            winText.gameObject.SetActive(true);
            int minutes = Mathf.FloorToInt(gameTime / 60);
            int seconds = Mathf.FloorToInt(gameTime % 60);
            winText.text = string.Format("クリア！\nタイム: {0:00}:{1:00}\nコイン: {2}/{3}", 
                minutes, seconds, collectedCoins, totalCoins);
        }
        
        if (restartButton != null)
        {
            restartButton.gameObject.SetActive(true);
        }
        
        if (messageText != null)
        {
            messageText.text = "Rキーでリスタート";
        }
        
        // カーソルを解放
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
    
    public void RestartGame()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }
}
