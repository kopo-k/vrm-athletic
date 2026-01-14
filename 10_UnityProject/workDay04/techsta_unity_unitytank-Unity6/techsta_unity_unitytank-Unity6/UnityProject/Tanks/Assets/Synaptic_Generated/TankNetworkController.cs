using Fusion;
using UnityEngine;
using UnityEngine.UI;

namespace Complete
{
    /// <summary>
    /// Photon Fusion対応の戦車ネットワーク制御スクリプト
    /// 【ポイント】全ての処理はホスト側(StateAuthority)で実行し、NetworkTransformで全員に同期
    /// </summary>
    public class TankNetworkController : NetworkBehaviour
    {
        [Header("Movement Settings")]
        public float m_Speed = 12f;
        public float m_TurnSpeed = 180f;

        [Header("Shooting Settings")]
        public NetworkPrefabRef m_ShellPrefab;     // ネットワーク対応のシェルプレハブ
        public Transform m_FireTransform;          // 発射位置
        public float m_MinLaunchForce = 15f;
        public float m_MaxLaunchForce = 30f;
        public float m_MaxChargeTime = 0.75f;

        [Header("Health Settings")]
        public float m_StartingHealth = 100f;

        [Header("Audio")]
        public AudioSource m_MovementAudio;
        public AudioSource m_ShootingAudio;
        public AudioClip m_EngineIdling;
        public AudioClip m_EngineDriving;
        public AudioClip m_ChargingClip;
        public AudioClip m_FireClip;
        public float m_PitchRange = 0.2f;

        [Header("UI References")]
        public Slider m_AimSlider;
        public Slider m_HealthSlider;
        public Image m_FillImage;
        public Color m_FullHealthColor = Color.green;
        public Color m_ZeroHealthColor = Color.red;

        [Header("Effects")]
        public GameObject m_ExplosionPrefab;

        // ネットワーク同期されるプロパティ（[Networked]属性で全クライアントに自動同期）
        [Networked] public float CurrentHealth { get; set; }
        [Networked] public NetworkBool IsDead { get; set; }
        [Networked] public float CurrentLaunchForce { get; set; }
        [Networked] public NetworkBool IsCharging { get; set; }
        [Networked] public int PlayerNumber { get; set; }
        [Networked] public Color NetworkedTankColor { get; set; }

        // ローカル変数
        private Rigidbody m_Rigidbody;
        private float m_OriginalPitch;
        private float m_ChargeSpeed;
        private ParticleSystem m_ExplosionParticles;
        private AudioSource m_ExplosionAudio;
        private ParticleSystem[] m_ParticleSystems;
        private Vector2 m_MoveInput;
        private bool m_FirePressed;
        private bool m_FireReleased;
        private Color m_LastAppliedColor;

        private void Awake()
        {
            m_Rigidbody = GetComponent<Rigidbody>();
            m_ChargeSpeed = (m_MaxLaunchForce - m_MinLaunchForce) / m_MaxChargeTime;

            // 爆発エフェクトの初期化
            if (m_ExplosionPrefab != null)
            {
                m_ExplosionParticles = Instantiate(m_ExplosionPrefab).GetComponent<ParticleSystem>();
                m_ExplosionAudio = m_ExplosionParticles.GetComponent<AudioSource>();
                m_ExplosionParticles.gameObject.SetActive(false);
            }

            // 体力バーのFillImageを事前に検索
            TryFindHealthBarFillImage();
        }

        private void Start()
        {
            // 少し遅延して体力バーを初期化（UIが完全に準備されるのを待つ）
            Invoke(nameof(DelayedHealthBarInit), 0.1f);
        }

        /// <summary>
        /// 体力バーのFillImageを検索して設定
        /// </summary>
        private void TryFindHealthBarFillImage()
        {
            if (m_FillImage != null) return;
            if (m_HealthSlider == null) return;

            // fillRectから取得を試みる
            if (m_HealthSlider.fillRect != null)
            {
                m_FillImage = m_HealthSlider.fillRect.GetComponent<Image>();
            }

            // それでもなければ、"Fill"という名前の子オブジェクトを探す
            if (m_FillImage == null)
            {
                Transform fillTransform = m_HealthSlider.transform.Find("Fill Area/Fill");
                if (fillTransform != null)
                {
                    m_FillImage = fillTransform.GetComponent<Image>();
                }
            }

            if (m_FillImage != null)
            {
                Debug.Log($"[TankNetworkController] TryFindHealthBarFillImage: Found {m_FillImage.gameObject.name}");
            }
        }

        /// <summary>
        /// 遅延して体力バーを初期化
        /// </summary>
        private void DelayedHealthBarInit()
        {
            TryFindHealthBarFillImage();

            if (m_FillImage != null)
            {
                // 現在の体力に基づいて色を設定
                float health = CurrentHealth > 0 ? CurrentHealth : m_StartingHealth;
                float healthPercent = health / m_StartingHealth;
                m_FillImage.color = Color.Lerp(m_ZeroHealthColor, m_FullHealthColor, healthPercent);
                Debug.Log($"[TankNetworkController] DelayedHealthBarInit: color set to {m_FillImage.color}");
            }
        }

        /// <summary>
        /// NetworkObjectがスポーンされた時に呼ばれる
        /// </summary>
        public override void Spawned()
        {
            // 初期化（StateAuthorityを持つ側のみが値を設定）
            if (HasStateAuthority)
            {
                CurrentHealth = m_StartingHealth;
                IsDead = false;
                CurrentLaunchForce = m_MinLaunchForce;
                IsCharging = false;
            }

            if (m_MovementAudio != null)
            {
                m_OriginalPitch = m_MovementAudio.pitch;
            }

            // パーティクルシステム取得
            m_ParticleSystems = GetComponentsInChildren<ParticleSystem>();

            // 体力スライダーの設定
            if (m_HealthSlider != null)
            {
                m_HealthSlider.minValue = 0;
                m_HealthSlider.maxValue = m_StartingHealth;
                m_HealthSlider.value = m_StartingHealth;

                // FillImageが設定されていない場合、自動検出
                if (m_FillImage == null)
                {
                    // fillRectから取得を試みる
                    if (m_HealthSlider.fillRect != null)
                    {
                        m_FillImage = m_HealthSlider.fillRect.GetComponent<Image>();
                    }

                    // それでもなければ、"Fill"という名前の子オブジェクトを探す
                    if (m_FillImage == null)
                    {
                        Transform fillTransform = m_HealthSlider.transform.Find("Fill Area/Fill");
                        if (fillTransform != null)
                        {
                            m_FillImage = fillTransform.GetComponent<Image>();
                        }
                    }

                    if (m_FillImage != null)
                    {
                        Debug.Log($"[TankNetworkController] Auto-found FillImage: {m_FillImage.gameObject.name}");
                    }
                }

                // 初期色を緑（満タン）に設定
                if (m_FillImage != null)
                {
                    m_FillImage.color = m_FullHealthColor;
                    Debug.Log($"[TankNetworkController] Health bar color set to: {m_FullHealthColor}");
                }
            }

            // 射撃スライダーの設定
            if (m_AimSlider != null)
            {
                m_AimSlider.minValue = m_MinLaunchForce;
                m_AimSlider.maxValue = m_MaxLaunchForce;
                m_AimSlider.value = m_MinLaunchForce;
            }

            // UIの初期化
            UpdateHealthUI();

            // 強制的に体力バーの色を更新
            ForceUpdateHealthBarColor();

            // 入力権限の詳細ログ
            Debug.Log($"=== Tank Spawned ===");
            Debug.Log($"  PlayerNumber: {PlayerNumber}");
            Debug.Log($"  HasInputAuthority: {HasInputAuthority}");
            Debug.Log($"  HasStateAuthority: {HasStateAuthority}");
            Debug.Log($"  Object.InputAuthority: {Object.InputAuthority}");
            Debug.Log($"  Runner.LocalPlayer: {Runner.LocalPlayer}");
            Debug.Log($"  Is Local Player's Tank: {Object.InputAuthority == Runner.LocalPlayer}");
            Debug.Log($"==================");
        }

        // デバッグ用カウンター
        private int m_FixedUpdateCount = 0;

        /// <summary>
        /// Fusionのネットワーク固定アップデート
        /// 【ポイント】物理演算はホスト側(StateAuthority)のみで実行
        /// </summary>
        public override void FixedUpdateNetwork()
        {
            m_FixedUpdateCount++;

            // 100フレームごとにステータスをログ出力
            if (m_FixedUpdateCount % 100 == 1)
            {
                Debug.Log($"[Tank{PlayerNumber}] FixedUpdateNetwork #{m_FixedUpdateCount} - HasInputAuthority:{HasInputAuthority} HasStateAuthority:{HasStateAuthority} IsDead:{IsDead}");
            }

            // 死んでいたら処理しない
            if (IsDead) return;

            // 入力データを取得
            bool gotInput = GetInput(out NetworkInputData input);

            if (gotInput)
            {
                m_MoveInput = input.movementInput;
                m_FirePressed = input.fireButtonDown;
                m_FireReleased = input.fireButtonUp;

                // デバッグ: 入力があったとき
                if (m_MoveInput != Vector2.zero)
                {
                    Debug.Log($"[Tank{PlayerNumber}] GetInput SUCCESS: {m_MoveInput} HasStateAuthority:{HasStateAuthority}");
                }
            }
            else
            {
                // GetInputが失敗した場合（これが問題の可能性）
                if (m_FixedUpdateCount % 100 == 1)
                {
                    Debug.LogWarning($"[Tank{PlayerNumber}] GetInput FAILED - no input data available");
                }
            }

            // 【重要】StateAuthorityを持つ側（ホスト）のみが物理演算を実行
            if (HasStateAuthority)
            {
                // 移動処理
                if (m_MoveInput != Vector2.zero)
                {
                    Move(m_MoveInput.y);
                    Turn(m_MoveInput.x);
                    Debug.Log($"[Tank{PlayerNumber}] MOVING: input={m_MoveInput} pos={transform.position}");
                }

                // 射撃処理
                HandleShooting();
            }
        }

        /// <summary>
        /// 通常のUpdate（オーディオやUIの更新用）
        /// </summary>
        private void Update()
        {
            if (!Object || !Object.IsValid) return;

            UpdateEngineAudio();
            UpdateHealthUI();
            UpdateAimUI();
        }

        #region Movement
        /// <summary>
        /// 前後移動
        /// 【重要】NetworkTransformと互換性を持たせるため、transform.positionを直接更新
        /// Rigidbody.MovePositionだとNetworkTransformで同期されない問題があった
        /// </summary>
        private void Move(float inputValue)
        {
            Vector3 movement = transform.forward * inputValue * m_Speed * Runner.DeltaTime;
            // transform.positionを直接更新（NetworkTransformで同期される）
            transform.position += movement;
        }

        /// <summary>
        /// 回転
        /// 【重要】NetworkTransformと互換性を持たせるため、transform.rotationを直接更新
        /// </summary>
        private void Turn(float inputValue)
        {
            float turn = inputValue * m_TurnSpeed * Runner.DeltaTime;
            // transform.rotationを直接更新（NetworkTransformで同期される）
            transform.rotation *= Quaternion.Euler(0f, turn, 0f);
        }
        #endregion

        #region Shooting
        private void HandleShooting()
        {
            // チャージ中で最大に達した場合
            if (CurrentLaunchForce >= m_MaxLaunchForce && IsCharging)
            {
                CurrentLaunchForce = m_MaxLaunchForce;
                Fire();
            }
            // 射撃ボタンが押された瞬間
            else if (m_FirePressed && !IsCharging)
            {
                IsCharging = true;
                CurrentLaunchForce = m_MinLaunchForce;
                RPC_PlayChargingSound();
            }
            // チャージ中
            else if (IsCharging && !m_FireReleased)
            {
                CurrentLaunchForce += m_ChargeSpeed * Runner.DeltaTime;
            }
            // 射撃ボタンが離された
            else if (m_FireReleased && IsCharging)
            {
                Fire();
            }
        }

        private void Fire()
        {
            IsCharging = false;

            // 【重要】シェルのスポーンはホスト側のみで実行
            if (HasStateAuthority && m_ShellPrefab.IsValid)
            {
                // NetworkObjectとしてシェルをスポーン
                Runner.Spawn(m_ShellPrefab, m_FireTransform.position, m_FireTransform.rotation,
                    inputAuthority: null, // シェルは誰の入力も受け付けない
                    onBeforeSpawned: (runner, obj) =>
                    {
                        // スポーン前に速度を設定
                        var shell = obj.GetComponent<ShellNetworkController>();
                        if (shell != null)
                        {
                            shell.Initialize(CurrentLaunchForce * m_FireTransform.forward);
                        }
                    });
            }

            // 全クライアントで発射音を再生（RPC使用）
            RPC_PlayFireSound();

            CurrentLaunchForce = m_MinLaunchForce;
        }

        /// <summary>
        /// 【RPC関数】チャージ音を全クライアントで再生
        /// </summary>
        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_PlayChargingSound()
        {
            if (m_ShootingAudio != null && m_ChargingClip != null)
            {
                m_ShootingAudio.clip = m_ChargingClip;
                m_ShootingAudio.Play();
            }
        }

        /// <summary>
        /// 【RPC関数】発射音を全クライアントで再生
        /// </summary>
        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_PlayFireSound()
        {
            if (m_ShootingAudio != null && m_FireClip != null)
            {
                m_ShootingAudio.clip = m_FireClip;
                m_ShootingAudio.Play();
            }
        }
        #endregion

        #region Health & Damage
        /// <summary>
        /// ダメージを受ける（ホスト側のみ呼び出し可能）
        /// </summary>
        public void TakeDamage(float amount)
        {
            // 【重要】ダメージ計算はホスト側のみで実行
            if (!HasStateAuthority) return;
            if (IsDead) return;

            CurrentHealth -= amount;

            if (CurrentHealth <= 0f)
            {
                CurrentHealth = 0f;
                OnDeath();
            }
        }

        private void OnDeath()
        {
            IsDead = true;

            // 爆発エフェクトをRPCで全クライアントに通知
            RPC_PlayExplosion(transform.position);

            // タンクを非アクティブ化
            gameObject.SetActive(false);
        }

        /// <summary>
        /// 【RPC関数】爆発エフェクトを全クライアントで再生
        /// </summary>
        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_PlayExplosion(Vector3 position)
        {
            if (m_ExplosionParticles != null)
            {
                m_ExplosionParticles.transform.position = position;
                m_ExplosionParticles.gameObject.SetActive(true);
                m_ExplosionParticles.Play();
            }

            if (m_ExplosionAudio != null)
            {
                m_ExplosionAudio.Play();
            }
        }

        /// <summary>
        /// タンクをリセット（ホスト側から呼び出し）
        /// </summary>
        public void ResetTank(Vector3 position, Quaternion rotation)
        {
            if (!HasStateAuthority) return;

            transform.position = position;
            transform.rotation = rotation;
            CurrentHealth = m_StartingHealth;
            IsDead = false;
            CurrentLaunchForce = m_MinLaunchForce;
            IsCharging = false;

            gameObject.SetActive(true);

            // パーティクルをリセット
            RPC_ResetParticles();
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_ResetParticles()
        {
            if (m_ParticleSystems != null)
            {
                foreach (var ps in m_ParticleSystems)
                {
                    ps.Clear();
                    ps.Play();
                }
            }
        }
        #endregion

        #region UI Updates
        /// <summary>
        /// 体力バーの色を強制的に更新（初期化時用）
        /// </summary>
        private void ForceUpdateHealthBarColor()
        {
            if (m_HealthSlider == null) return;

            // FillImageを再検索
            Image fillImage = m_FillImage;
            if (fillImage == null && m_HealthSlider.fillRect != null)
            {
                fillImage = m_HealthSlider.fillRect.GetComponent<Image>();
            }

            if (fillImage != null)
            {
                float healthPercent = (m_StartingHealth > 0) ? CurrentHealth / m_StartingHealth : 1f;
                Color targetColor = Color.Lerp(m_ZeroHealthColor, m_FullHealthColor, healthPercent);
                fillImage.color = targetColor;
                Debug.Log($"[TankNetworkController] ForceUpdateHealthBarColor: {targetColor} (health: {CurrentHealth}/{m_StartingHealth})");
            }
            else
            {
                Debug.LogWarning("[TankNetworkController] Could not find FillImage for health bar!");
            }
        }

        private void UpdateHealthUI()
        {
            float healthPercent = (m_StartingHealth > 0) ? CurrentHealth / m_StartingHealth : 1f;

            if (m_HealthSlider != null)
            {
                m_HealthSlider.value = CurrentHealth;
            }

            // FillImageの色を設定
            if (m_FillImage != null)
            {
                m_FillImage.color = Color.Lerp(m_ZeroHealthColor, m_FullHealthColor, healthPercent);
            }
            else
            {
                // FillImageがなくても、スライダーの塗りつぶし部分を探して色を設定
                if (m_HealthSlider != null)
                {
                    Transform fillArea = m_HealthSlider.fillRect;
                    if (fillArea != null)
                    {
                        Image fillImage = fillArea.GetComponent<Image>();
                        if (fillImage != null)
                        {
                            fillImage.color = Color.Lerp(m_ZeroHealthColor, m_FullHealthColor, healthPercent);
                        }
                    }
                }
            }
        }

        private void UpdateAimUI()
        {
            if (m_AimSlider != null)
            {
                m_AimSlider.value = IsCharging ? CurrentLaunchForce : m_MinLaunchForce;
            }
        }

        private void UpdateEngineAudio()
        {
            if (m_MovementAudio == null) return;

            bool isMoving = Mathf.Abs(m_MoveInput.x) > 0.1f || Mathf.Abs(m_MoveInput.y) > 0.1f;

            if (!isMoving)
            {
                if (m_MovementAudio.clip == m_EngineDriving)
                {
                    m_MovementAudio.clip = m_EngineIdling;
                    m_MovementAudio.pitch = Random.Range(m_OriginalPitch - m_PitchRange, m_OriginalPitch + m_PitchRange);
                    m_MovementAudio.Play();
                }
            }
            else
            {
                if (m_MovementAudio.clip == m_EngineIdling)
                {
                    m_MovementAudio.clip = m_EngineDriving;
                    m_MovementAudio.pitch = Random.Range(m_OriginalPitch - m_PitchRange, m_OriginalPitch + m_PitchRange);
                    m_MovementAudio.Play();
                }
            }
        }
        #endregion

        /// <summary>
        /// タンクの色を設定（ネットワーク同期）
        /// </summary>
        public void SetTankColor(Color color)
        {
            // ネットワークプロパティに保存（全クライアントに同期される）
            NetworkedTankColor = color;
            ApplyTankColor(color);
        }

        /// <summary>
        /// 色を実際にマテリアルに適用
        /// </summary>
        private void ApplyTankColor(Color color)
        {
            if (color == m_LastAppliedColor) return;
            m_LastAppliedColor = color;

            MeshRenderer[] renderers = GetComponentsInChildren<MeshRenderer>();
            foreach (var renderer in renderers)
            {
                renderer.material.color = color;
            }
            Debug.Log($"[TankNetworkController] Applied color {color} to tank {PlayerNumber}");
        }

        /// <summary>
        /// Fusionのレンダリングコールバック（毎フレーム呼ばれる）
        /// NetworkedプロパティをUIやビジュアルに反映
        /// </summary>
        public override void Render()
        {
            // 色の同期を適用
            if (NetworkedTankColor != default && NetworkedTankColor != m_LastAppliedColor)
            {
                ApplyTankColor(NetworkedTankColor);
            }

            // 体力バーの色を毎フレーム更新（参照問題を回避）
            UpdateHealthBarColorDirect();
        }

        // 体力バー更新用のキャッシュ
        private Image m_CachedHealthFillImage = null;
        private bool m_HealthFillSearched = false;

        /// <summary>
        /// 体力バーの色を直接更新（参照がなくても動作）
        /// </summary>
        private void UpdateHealthBarColorDirect()
        {
            // FillImageを探す（初回のみ）
            if (!m_HealthFillSearched)
            {
                m_HealthFillSearched = true;
                FindHealthFillImage();
            }

            // FillImageがあれば色を更新
            if (m_CachedHealthFillImage != null)
            {
                // CurrentHealthが0の場合（まだ同期されていない場合）は満タンと見なす
                float health = CurrentHealth > 0 ? CurrentHealth : m_StartingHealth;
                float healthPercent = (m_StartingHealth > 0) ? health / m_StartingHealth : 1f;
                Color targetColor = Color.Lerp(m_ZeroHealthColor, m_FullHealthColor, healthPercent);

                // 常に色を設定（比較しない）
                m_CachedHealthFillImage.color = targetColor;
            }
            else
            {
                // 毎回探す（遅延ロードされた場合に対応）
                m_HealthFillSearched = false;
            }
        }

        /// <summary>
        /// 体力バーのFillImageを検索
        /// </summary>
        private void FindHealthFillImage()
        {
            Debug.Log($"[Tank{PlayerNumber}] FindHealthFillImage called...");

            // まずm_FillImageを確認
            if (m_FillImage != null)
            {
                m_CachedHealthFillImage = m_FillImage;
                Debug.Log($"[Tank{PlayerNumber}] Using assigned m_FillImage: {m_FillImage.gameObject.name}, current color: {m_FillImage.color}");
                ConfigureHealthImage(m_CachedHealthFillImage);
                return;
            }

            // m_HealthSliderから取得
            if (m_HealthSlider != null && m_HealthSlider.fillRect != null)
            {
                m_CachedHealthFillImage = m_HealthSlider.fillRect.GetComponent<Image>();
                if (m_CachedHealthFillImage != null)
                {
                    Debug.Log($"[Tank{PlayerNumber}] Found FillImage from HealthSlider.fillRect: {m_CachedHealthFillImage.gameObject.name}");
                    ConfigureHealthImage(m_CachedHealthFillImage);
                    return;
                }
            }

            // 子オブジェクトからSliderを探す
            Slider[] sliders = GetComponentsInChildren<Slider>(true);
            Debug.Log($"[Tank{PlayerNumber}] Found {sliders.Length} sliders in children");

            foreach (Slider slider in sliders)
            {
                Debug.Log($"[Tank{PlayerNumber}]   Checking slider: {slider.gameObject.name}");

                // 名前に"Health"が含まれるか、最初のスライダーを使用
                bool isHealthSlider = slider.gameObject.name.ToLower().Contains("health") ||
                                     (slider.transform.parent != null && slider.transform.parent.name.ToLower().Contains("health"));

                if (isHealthSlider || m_HealthSlider == null)
                {
                    if (slider.fillRect != null)
                    {
                        Image fillImg = slider.fillRect.GetComponent<Image>();
                        if (fillImg != null)
                        {
                            m_CachedHealthFillImage = fillImg;
                            m_HealthSlider = slider; // 参照も設定
                            Debug.Log($"[Tank{PlayerNumber}] Found FillImage from child slider: {slider.gameObject.name}, fillRect: {fillImg.gameObject.name}");
                            ConfigureHealthImage(m_CachedHealthFillImage);
                            return;
                        }
                    }

                    // Fill Area/Fill パスを試す
                    Transform fillArea = slider.transform.Find("Fill Area");
                    if (fillArea != null)
                    {
                        Transform fill = fillArea.Find("Fill");
                        if (fill != null)
                        {
                            Image fillImg = fill.GetComponent<Image>();
                            if (fillImg != null)
                            {
                                m_CachedHealthFillImage = fillImg;
                                m_HealthSlider = slider;
                                Debug.Log($"[Tank{PlayerNumber}] Found FillImage from Fill Area/Fill path: {fillImg.gameObject.name}");
                                ConfigureHealthImage(m_CachedHealthFillImage);
                                return;
                            }
                        }
                    }
                }
            }

            // 最後の手段：名前に"Fill"を含むImageを全て探す
            Image[] allImages = GetComponentsInChildren<Image>(true);
            Debug.Log($"[Tank{PlayerNumber}] Found {allImages.Length} images in children, searching by name...");

            foreach (Image img in allImages)
            {
                if (img.gameObject.name.ToLower().Contains("fill") &&
                    !img.gameObject.name.ToLower().Contains("aim"))
                {
                    m_CachedHealthFillImage = img;
                    Debug.Log($"[Tank{PlayerNumber}] Found FillImage by name search: {img.gameObject.name}");
                    ConfigureHealthImage(m_CachedHealthFillImage);
                    return;
                }
            }

            Debug.LogWarning($"[Tank{PlayerNumber}] Could not find health bar FillImage! Total images found: {allImages.Length}");

            // 全Imageの名前をログ出力（デバッグ用）
            foreach (Image img in allImages)
            {
                Debug.Log($"[Tank{PlayerNumber}]   Image found: {img.gameObject.name}, color: {img.color}");
            }
        }

        /// <summary>
        /// 体力バー用のImageを設定（色が表示されるように）
        /// </summary>
        private void ConfigureHealthImage(Image img)
        {
            if (img == null) return;

            // Imageが色を表示できるように設定
            // Image Typeが"Filled"か"Simple"であることを確認
            Debug.Log($"[Tank{PlayerNumber}] Configuring health image: type={img.type}, sprite={(img.sprite != null ? img.sprite.name : "NULL")}");

            // スプライトがない場合、色のみで表示するためにデフォルトスプライトを設定
            if (img.sprite == null)
            {
                // Unity内蔵の白いスプライトを使用
                img.sprite = Resources.GetBuiltinResource<Sprite>("UI/Skin/Background.psd");
                Debug.Log($"[Tank{PlayerNumber}] Set default sprite for health image");
            }

            // 色を緑に設定
            img.color = m_FullHealthColor;
            Debug.Log($"[Tank{PlayerNumber}] Set initial color to: {m_FullHealthColor}");
        }
    }
}
