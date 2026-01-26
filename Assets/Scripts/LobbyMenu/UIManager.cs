using UnityEngine;
using UnityEngine.UI;

public class UIManager : MonoBehaviour
{
  private OpenCloseUIElement[] uiElements;
  void Start()
  {
    uiElements = FindObjectsByType<OpenCloseUIElement>(FindObjectsInactive.Include, FindObjectsSortMode.None);
    SetUpUIElements();
  }

  // Sets up all objects of type OpenCloseUIElement by iterating through all their
  // Open and close buttons, and assigns them to their appropriate open or close method.
  private void SetUpUIElements()
  {
    foreach (OpenCloseUIElement el in uiElements)
    {
      el.gameObject.SetActive(false);
      if (el.getIsActiveByDefault())
      {
        el.gameObject.SetActive(true);
      }
      foreach (Button b in el.getOpenButtons())
      {
        b.onClick.AddListener(() => openElement(el));
      }
      foreach (Button b in el.getCloseButtons())
      {
        b.onClick.AddListener(() => closeElement(el));
      }
    }
  }

  private void closeElement(OpenCloseUIElement el)
  {
    el.gameObject.SetActive(false);
  }

  private void openElement(OpenCloseUIElement el)
  {
    el.gameObject.SetActive(true);
  }

  private void refreshLobbyList()
  {
    
  }
}
