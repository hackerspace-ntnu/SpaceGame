using Unity.VisualScripting;
using UnityEngine;

public class Ship : MonoBehaviour
{
    private int scrapAmount = 0;
    private int scrapToWin = 3;

    public void AddScrap()
    {
        scrapAmount +=1;
        Debug.Log("Scrap added");
        CheckWin();
    }

    private void CheckWin()
    {
        if(scrapAmount < scrapToWin) return;
        if(!GameManager.Instance) return;
            
        GameManager.Instance.WinGame();
    }

}
