using UnityEngine;

namespace Complete
{
    public class HealthPickup : MonoBehaviour
    {
        public float m_HealAmount = 30f;           // 回復量
        public float m_RotateSpeed = 50f;          // 回転速度
        public float m_BobSpeed = 2f;              // 上下移動の速度
        public float m_BobHeight = 0.3f;           // 上下移動の高さ
        public AudioClip m_PickupSound;            // 取得時の効果音
        
        private Vector3 m_StartPosition;           // 初期位置
        private float m_BobTimer;                  // 上下移動用タイマー

        private void Start()
        {
            m_StartPosition = transform.position;
            m_BobTimer = Random.Range(0f, Mathf.PI * 2f); // ランダムな開始位相
        }

        private void Update()
        {
            // アイテムを回転させる
            transform.Rotate(Vector3.up, m_RotateSpeed * Time.deltaTime);
            
            // アイテムを上下に動かす
            m_BobTimer += m_BobSpeed * Time.deltaTime;
            float newY = m_StartPosition.y + Mathf.Sin(m_BobTimer) * m_BobHeight;
            transform.position = new Vector3(m_StartPosition.x, newY, m_StartPosition.z);
        }

        private void OnTriggerEnter(Collider other)
        {
            // 戦車かどうかチェック
            TankHealth tankHealth = other.GetComponent<TankHealth>();
            
            if (tankHealth != null)
            {
                // 戦車を回復させる
                tankHealth.Heal(m_HealAmount);
                
                // 効果音を再生
                if (m_PickupSound != null)
                {
                    AudioSource.PlayClipAtPoint(m_PickupSound, transform.position);
                }
                
                // アイテムを削除
                Destroy(gameObject);
            }
        }
    }
}
