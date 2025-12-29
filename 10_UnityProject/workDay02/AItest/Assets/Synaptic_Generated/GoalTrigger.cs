using UnityEngine;

public class GoalTrigger : MonoBehaviour
{
    public float rotationSpeed = 50f;
    public float bobSpeed = 2f;
    public float bobHeight = 0.3f;
    
    private Vector3 startPosition;
    private Collider goalCollider;
    
    void Start()
    {
        startPosition = transform.position;
        
        goalCollider = GetComponent<Collider>();
        if (goalCollider != null)
        {
            goalCollider.isTrigger = true;
        }
        else
        {
            SphereCollider sc = gameObject.AddComponent<SphereCollider>();
            sc.isTrigger = true;
            sc.radius = 1.5f;
        }
        
        gameObject.tag = "Goal";
    }
    
    void Update()
    {
        transform.Rotate(Vector3.up, rotationSpeed * Time.deltaTime);
        
        float newY = startPosition.y + Mathf.Sin(Time.time * bobSpeed) * bobHeight;
        transform.position = new Vector3(startPosition.x, newY, startPosition.z);
    }
    
    void OnTriggerEnter(Collider other)
    {
        if (other.GetComponent<MazePlayerController>() != null || other.CompareTag("Player"))
        {
            // ゴール判定はMazeGameManagerのUpdateで距離判定で行うため、
            // ここではログ出力のみ
            Debug.Log("Goal reached!");
        }
    }
}
