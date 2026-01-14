using Fusion;
using UnityEngine;

namespace Complete
{
    /// <summary>
    /// ネットワーク対応の砲弾スクリプト
    /// 【ポイント】ダメージ計算はホスト側のみで実行
    /// </summary>
    public class ShellNetworkController : NetworkBehaviour
    {
        [Header("Explosion Settings")]
        public LayerMask m_TankMask;
        public ParticleSystem m_ExplosionParticles;
        public AudioSource m_ExplosionAudio;
        public float m_MaxDamage = 100f;
        public float m_ExplosionForce = 1000f;
        public float m_MaxLifeTime = 2f;
        public float m_ExplosionRadius = 5f;

        // ネットワーク同期される速度
        [Networked] public Vector3 Velocity { get; set; }

        private Rigidbody m_Rigidbody;
        private bool m_HasExploded = false;

        private void Awake()
        {
            m_Rigidbody = GetComponent<Rigidbody>();
        }

        /// <summary>
        /// シェルの初期化（スポーン時に呼ばれる）
        /// </summary>
        public void Initialize(Vector3 velocity)
        {
            Velocity = velocity;
        }

        public override void Spawned()
        {
            // 速度を設定
            if (m_Rigidbody != null)
            {
                m_Rigidbody.linearVelocity = Velocity;
            }

            // 寿命後に破棄（ホスト側のみ）
            if (HasStateAuthority)
            {
                // TickTimerを使用して寿命管理
                Invoke(nameof(DespawnShell), m_MaxLifeTime);
            }
        }

        private void DespawnShell()
        {
            if (Object != null && Object.IsValid && HasStateAuthority)
            {
                Runner.Despawn(Object);
            }
        }

        public override void FixedUpdateNetwork()
        {
            // 【重要】衝突判定とダメージ計算はホスト側のみで実行
            if (!HasStateAuthority) return;
            if (m_HasExploded) return;
        }

        private void OnTriggerEnter(Collider other)
        {
            // 【重要】衝突処理はホスト側のみで実行
            if (!HasStateAuthority) return;
            if (m_HasExploded) return;

            m_HasExploded = true;

            // 爆発範囲内のタンクにダメージを与える
            Collider[] colliders = Physics.OverlapSphere(transform.position, m_ExplosionRadius, m_TankMask);

            foreach (Collider col in colliders)
            {
                Rigidbody targetRigidbody = col.GetComponent<Rigidbody>();
                if (targetRigidbody == null) continue;

                // 爆発力を加える
                targetRigidbody.AddExplosionForce(m_ExplosionForce, transform.position, m_ExplosionRadius);

                // TankNetworkControllerを取得してダメージを与える
                TankNetworkController tankController = col.GetComponent<TankNetworkController>();
                if (tankController != null)
                {
                    float damage = CalculateDamage(targetRigidbody.position);
                    tankController.TakeDamage(damage);
                }
            }

            // 爆発エフェクトを全クライアントで再生
            RPC_PlayExplosion();

            // シェルを破棄
            Runner.Despawn(Object);
        }

        private float CalculateDamage(Vector3 targetPosition)
        {
            Vector3 explosionToTarget = targetPosition - transform.position;
            float explosionDistance = explosionToTarget.magnitude;
            float relativeDistance = (m_ExplosionRadius - explosionDistance) / m_ExplosionRadius;
            float damage = relativeDistance * m_MaxDamage;
            return Mathf.Max(0f, damage);
        }

        /// <summary>
        /// 【RPC関数】爆発エフェクトを全クライアントで再生
        /// </summary>
        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_PlayExplosion()
        {
            if (m_ExplosionParticles != null)
            {
                // パーティクルを親から外す
                m_ExplosionParticles.transform.parent = null;
                m_ExplosionParticles.Play();

                // パーティクル終了後に破棄
                ParticleSystem.MainModule mainModule = m_ExplosionParticles.main;
                Destroy(m_ExplosionParticles.gameObject, mainModule.duration);
            }

            if (m_ExplosionAudio != null)
            {
                m_ExplosionAudio.Play();
            }
        }
    }
}
