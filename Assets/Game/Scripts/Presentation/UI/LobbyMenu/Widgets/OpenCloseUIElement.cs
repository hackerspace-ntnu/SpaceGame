using UnityEngine;
using UnityEngine.UI;

namespace SpaceGame.Presentation
{
    public class OpenCloseUIElement : MonoBehaviour
    {
      [SerializeField] private Button[] openButtons;
      [SerializeField] private Button[] closeButtons; 
      [SerializeField] private bool isOpenByDefault;

      public bool getIsActiveByDefault()
      {
        return isOpenByDefault;
      }
      public Button[] getOpenButtons()
      {
        return openButtons;
      }
      public Button[] getCloseButtons()
      {
        return closeButtons;
      }

    }
}
