using UnityEngine;

public class Staff_Script : MonoBehaviour
{
    public float max_distance = 10000;
    private LineRenderer lr;
    [SerializeField] private Material beamMaterial;
    RaycastHit hit;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        lr = gameObject.AddComponent<LineRenderer>();
        lr.startWidth = lr.endWidth = 0.05f; 
        lr.material = beamMaterial;  
        lr.startColor = lr.endColor = Color.red;
    }


    // Update is called once per frame
    void Update()
    {
        //Debug.DrawRay(transform.position, Vector3.right * max_distance, Color.red);
        Ray ray = new Ray(transform.position, Vector3.right);  
        if (UnityEngine.Physics.Raycast(ray, out hit, max_distance)) {
            //Debug.Log("hit");
            lr.SetPosition(0, ray.origin);
            lr.SetPosition(1, hit.point);
            GameObject target = hit.collider.gameObject;
        }
        else
        {
            lr.SetPosition(0, ray.origin);
            lr.SetPosition(1, ray.origin + ray.direction * max_distance);
        }
    
    }
}
