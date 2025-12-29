using UnityEngine;

public class GoalRotator : MonoBehaviour
{
    public float rotationSpeed = 50f;
    public float bobSpeed = 2f;
    public float bobHeight = 0.3f;
    
    private Vector3 startPosition;
    
    void Start()
    {
        startPosition = transform.position;
    }
    
    void Update()
    {
        // 回転
        transform.Rotate(Vector3.up, rotationSpeed * Time.deltaTime);
        
        // 上下に浮遊
        float newY = startPosition.y + Mathf.Sin(Time.time * bobSpeed) * bobHeight;
        transform.position = new Vector3(startPosition.x, newY, startPosition.z);
    }
}
