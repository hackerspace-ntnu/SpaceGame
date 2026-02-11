using UnityEngine;

public class Ship : MonoBehaviour
{
    public int ScrapAmount = 0;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
    public void AddScrap()
    {
        ScrapAmount +=1;
        Debug.Log("Scrap added");
    }

}
