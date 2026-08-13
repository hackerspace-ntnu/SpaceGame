using System;
using TMPro;
using UnityEngine;

namespace SpaceGame.Presentation
{
    public class LobbyWarningSystem : MonoBehaviour
    {
        public GameObject warningPanel;
        public TextMeshProUGUI warningPanelErrorMessage;

        public void warn(String errorMessage)
        {
            //Change warning panel text and set it to active
            warningPanel.SetActive(true);
            warningPanelErrorMessage.text = errorMessage;
        }
    }
}
