using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// World-space gauge for a <see cref="RepairWorkstation"/>: a fill bar, an "x / y" scrap
/// readout and a status line. Purely presentational — it reads the workstation's replicated
/// progress, so it shows the same value on host and clients.
/// </summary>
[DisallowMultipleComponent]
public class RepairProgressUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private RepairWorkstation workstation;
    [SerializeField] private Image fillBar;
    [SerializeField] private TextMeshProUGUI amountText;
    [SerializeField] private TextMeshProUGUI statusText;

    [Header("Appearance")]
    [SerializeField] private Color brokenColor = new Color(0.9f, 0.25f, 0.15f);
    [SerializeField] private Color partialColor = new Color(0.95f, 0.7f, 0.15f);
    [SerializeField] private Color repairedColor = new Color(0.2f, 0.9f, 0.35f);
    [SerializeField] private string brokenLabel = "NEEDS SCRAP";
    [SerializeField] private string repairedLabel = "ONLINE";

    [Tooltip("Seconds for the bar to catch up to a new value. 0 snaps instantly.")]
    [SerializeField] private float fillLerpDuration = 0.25f;

    private float displayedFill;
    private float targetFill;

    private void Reset()
    {
        workstation = GetComponentInParent<RepairWorkstation>();
    }

    private void OnEnable()
    {
        if (workstation == null) workstation = GetComponentInParent<RepairWorkstation>();

        if (workstation == null)
        {
            Debug.LogWarning($"{name}: RepairProgressUI has no RepairWorkstation assigned.", this);
            return;
        }

        workstation.ProgressChanged += HandleProgressChanged;

        targetFill = workstation.Progress01;
        displayedFill = targetFill;
        Refresh();
    }

    private void OnDisable()
    {
        if (workstation == null) return;
        workstation.ProgressChanged -= HandleProgressChanged;
    }

    private void HandleProgressChanged(float progress01)
    {
        targetFill = progress01;
        Refresh();
    }

    private void Update()
    {
        if (Mathf.Approximately(displayedFill, targetFill)) return;

        displayedFill = fillLerpDuration <= 0f
            ? targetFill
            : Mathf.MoveTowards(displayedFill, targetFill, Time.deltaTime / fillLerpDuration);

        if (fillBar != null) fillBar.fillAmount = displayedFill;
    }

    private void Refresh()
    {
        if (workstation == null) return;

        bool repaired = workstation.IsRepaired;
        Color color = repaired
            ? repairedColor
            : Color.Lerp(brokenColor, partialColor, targetFill);

        if (fillBar != null)
        {
            fillBar.fillAmount = displayedFill;
            fillBar.color = color;
        }

        if (amountText != null)
            amountText.text = $"{workstation.CurrentAmount} / {workstation.RequiredAmount}";

        if (statusText != null)
        {
            statusText.text = repaired ? repairedLabel : brokenLabel;
            statusText.color = color;
        }
    }
}
