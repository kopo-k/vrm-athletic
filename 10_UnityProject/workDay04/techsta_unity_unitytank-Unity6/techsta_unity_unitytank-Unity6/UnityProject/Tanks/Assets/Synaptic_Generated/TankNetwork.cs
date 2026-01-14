using Fusion;
using UnityEngine;

/// <summary>
/// Photon Fusion対応の戦車ネットワーク制御スクリプト
/// NetworkBehaviourを継承し、ネットワーク越しに戦車を同期
/// </summary>
public class TankNetwork : NetworkBehaviour
{
    [Header("Movement Settings")]
    public float speed = 12f;
    public float turnSpeed = 180f;

    [Header("Audio")]
    public AudioSource movementAudio;
    public AudioClip engineIdling;
    public AudioClip engineDriving;
    public float pitchRange = 0.2f;

    [Header("References")]
    public Transform turret; // 砲塔のTransform
    public ParticleSystem[] dustTrails; // トラックの煙エフェクト

    private Rigidbody rb;
    private float originalPitch;
    
    // ネットワーク同期される入力データ
    private Vector2 moveInput;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    public override void Spawned()
    {
        // NetworkObjectがスポーンされた時の初期化
        if (movementAudio != null)
        {
            originalPitch = movementAudio.pitch;
        }

        // パーティクルシステムの開始
        if (dustTrails != null)
        {
            foreach (var ps in dustTrails)
            {
                ps.Play();
            }
        }

        // 自分が操作する戦車以外は物理演算を無効化
        if (!HasInputAuthority)
        {
            rb.isKinematic = true;
        }
    }

    public override void FixedUpdateNetwork()
    {
        // ホスト側（StateAuthority）のみ物理演算を実行
        if (HasStateAuthority)
        {
            // 入力データを取得
            if (GetInput(out Complete.NetworkInputData input))
            {
                moveInput = input.movementInput;
            }

            // 移動処理
            Move(moveInput.y);
            Turn(moveInput.x);
        }
    }

    private void Update()
    {
        // オーディオはローカルで再生（全クライアント）
        UpdateEngineAudio();
    }

    private void Move(float inputValue)
    {
        // 前後移動
        Vector3 movement = transform.forward * inputValue * speed * Runner.DeltaTime;
        rb.MovePosition(rb.position + movement);
    }

    private void Turn(float inputValue)
    {
        // 回転
        float turn = inputValue * turnSpeed * Runner.DeltaTime;
        Quaternion turnRotation = Quaternion.Euler(0f, turn, 0f);
        rb.MoveRotation(rb.rotation * turnRotation);
    }

    private void UpdateEngineAudio()
    {
        if (movementAudio == null) return;

        // 移動中かどうかで音を切り替え
        bool isMoving = Mathf.Abs(moveInput.x) > 0.1f || Mathf.Abs(moveInput.y) > 0.1f;

        if (!isMoving)
        {
            if (movementAudio.clip == engineDriving)
            {
                movementAudio.clip = engineIdling;
                movementAudio.pitch = Random.Range(originalPitch - pitchRange, originalPitch + pitchRange);
                movementAudio.Play();
            }
        }
        else
        {
            if (movementAudio.clip == engineIdling)
            {
                movementAudio.clip = engineDriving;
                movementAudio.pitch = Random.Range(originalPitch - pitchRange, originalPitch + pitchRange);
                movementAudio.Play();
            }
        }
    }
}
