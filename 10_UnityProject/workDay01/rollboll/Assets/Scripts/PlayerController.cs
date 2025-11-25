using UnityEngine;
using UnityEngine.UI;

public class PlayerController : MonoBehaviour
{
    
    private float speed = 5;
    private Rigidbody rd;
    private int score;
    public Text scoreText;
    public Text winText;
    public Text timeLimitText;
    private float timeLimit = 20f;
    private float elapsedTime = 0f;
    private bool isGameOver = false;


    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        rd = GetComponent<Rigidbody>();
        score = 0;
        SetCountText();
        winText.text = "";
        UpdateTimerText();
    }

    // Update is called once per frame
    void Update()
    {
        if (!isGameOver)
        {
            var moveHorizontal = Input.GetAxis("Horizontal");
            var moveVertical = Input.GetAxis("Vertical");

            var movement = new Vector3(moveHorizontal, 0, moveVertical);
            rd.AddForce(movement * speed);
            
            elapsedTime += Time.deltaTime;  // 経過時間を加算
            UpdateTimerText();

            if (elapsedTime >= timeLimit)
            {
                GameOver();
            }
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.gameObject.CompareTag("PickUp"))
        {
            other.gameObject.SetActive(false);
            score++;  // ← スコアを増やす処理を追加
            SetCountText();
        }
    }

    void SetCountText()
    {
        scoreText.text = "Count: " + score.ToString();

        if (score >= 6)
        {
            isGameOver = true;
            winText.text = "You Win!";
            winText.color = Color.green;
        }
    }

    void UpdateTimerText()
    {
        float remainingTime = timeLimit - elapsedTime;  // 残り時間
        
        if (remainingTime < 0)
        {
            remainingTime = 0;
        }
        
        // 残り時間を表示（小数点1桁まで）
        timeLimitText.text = "Time: " + remainingTime.ToString("F1");
    }

    void GameOver()
    {
        isGameOver = true;
        winText.text = "Time Over!";
        winText.color = Color.red;
        rd.linearVelocity = Vector3.zero;  // 玉の動きを止める
        rd.angularVelocity = Vector3.zero;
    }


}
