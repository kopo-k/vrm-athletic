using UnityEngine;

public class Collectible : MonoBehaviour
{
    public float rotationSpeed = 100f;
    public float bobSpeed = 3f;
    public float bobHeight = 0.2f;
    
    private Vector3 startPosition;
    
    void Start()
    {
        startPosition = transform.position;
        
        Collider col = GetComponent<Collider>();
        if (col != null)
        {
            col.isTrigger = true;
        }
        else
        {
            SphereCollider sc = gameObject.AddComponent<SphereCollider>();
            sc.isTrigger = true;
        }
        
        gameObject.tag = "Collectible";
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
            if (MazeGameManager.Instance != null)
            {
                MazeGameManager.Instance.CollectItem();
            }
            
            Destroy(gameObject);
        }
    }
}
