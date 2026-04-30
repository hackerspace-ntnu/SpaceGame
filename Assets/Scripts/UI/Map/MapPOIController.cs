using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Drop-and-name POI markers at runtime. Press the configured key to open a
/// small text-entry overlay; type a name and press Enter to register a marker
/// at the player's current world position via MapService. Press Escape to
/// cancel. Empty names auto-fall back to "POI N".
///
/// Add this component to any GameObject in the persistent scene (e.g. the same
/// one as MapService). All UI is generated at runtime — no prefab required.
/// </summary>
public class MapPOIController : MonoBehaviour
{
    [Header("Input")]
    [Tooltip("Optional Input Action name for opening the naming prompt. If not found, falls back to the key below.")]
    [SerializeField] private string dropActionName = "DropPOI";
    [SerializeField] private Key fallbackKey = Key.P;

    [Header("Marker")]
    [SerializeField] private MapMarkerType defaultType = MapMarkerType.Discovery;
    [Tooltip("If on, dropped POIs ignore chunk reveal state and always show on the map.")]
    [SerializeField] private bool alwaysVisible = true;

    [Header("UI")]
    [SerializeField] private string promptLabel = "NAME POI";
    [SerializeField] private Color uiTint = new Color(0.20f, 0.95f, 1.00f, 1f);
    [SerializeField] private int fontSize = 18;

    private InputAction dropAction;
    private Transform player;
    private Canvas canvas;
    private TMP_InputField inputField;
    private GameObject panel;
    private bool inputting;
    private Vector3 pendingPosition;
    private int nextAutoIndex = 1;
    private readonly List<MapService.Marker> droppedMarkers = new();

    private void Start()
    {
        dropAction = InputSystem.actions?.FindAction(dropActionName);
        BuildUI();
        SetPanelVisible(false);
    }

    private void Update()
    {
        if (!inputting)
        {
            if (DropTriggered()) BeginDrop();
            return;
        }

        // While the input field is focused, let TMP handle text. We watch for
        // submit/cancel via the keyboard directly (works regardless of focus).
        var kb = Keyboard.current;
        if (kb == null) return;

        if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)
            ConfirmDrop();
        else if (kb.escapeKey.wasPressedThisFrame)
            CancelDrop();
    }

    private bool DropTriggered()
    {
        if (dropAction != null && dropAction.WasPressedThisFrame()) return true;
        var kb = Keyboard.current;
        return kb != null && kb[fallbackKey].wasPressedThisFrame;
    }

    // ─────────────────────────────────────────────
    //  Drop flow
    // ─────────────────────────────────────────────

    private void BeginDrop()
    {
        if (player == null)
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p != null) player = p.transform;
        }
        if (player == null)
        {
            Debug.LogWarning("[MapPOIController] No GameObject tagged 'Player' found.", this);
            return;
        }

        pendingPosition = player.position;
        inputting = true;
        SetPanelVisible(true);
        inputField.text = string.Empty;
        inputField.Select();
        inputField.ActivateInputField();
    }

    private void ConfirmDrop()
    {
        string name = inputField != null ? inputField.text : null;
        if (string.IsNullOrWhiteSpace(name)) name = $"POI {nextAutoIndex}";
        else name = name.Trim();
        nextAutoIndex++;

        if (MapService.Instance != null)
        {
            var marker = MapService.Instance.AddStaticMarker(
                pendingPosition, defaultType, name, requiresRevealedChunk: !alwaysVisible);
            if (marker != null) droppedMarkers.Add(marker);
        }
        else
        {
            Debug.LogWarning("[MapPOIController] No MapService in scene; POI not registered.", this);
        }

        EndInput();
    }

    private void CancelDrop() => EndInput();

    private void EndInput()
    {
        inputting = false;
        SetPanelVisible(false);
    }

    public void RemoveAllDroppedPOIs()
    {
        if (MapService.Instance == null) return;
        foreach (var m in droppedMarkers) MapService.Instance.RemoveMarker(m);
        droppedMarkers.Clear();
        nextAutoIndex = 1;
    }

    // ─────────────────────────────────────────────
    //  UI build
    // ─────────────────────────────────────────────

    private void SetPanelVisible(bool v)
    {
        if (panel != null) panel.SetActive(v);
    }

    private void BuildUI()
    {
        var canvasGo = new GameObject("MapPOIController_Canvas",
            typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;

        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        var es = FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>();
        if (es == null)
        {
            var esGo = new GameObject("EventSystem (POI)",
                typeof(UnityEngine.EventSystems.EventSystem),
                typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
            esGo.transform.SetParent(transform, false);
        }

        // Panel — dim background
        panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(canvas.transform, false);
        var prt = (RectTransform)panel.transform;
        prt.anchorMin = new Vector2(0.5f, 0.5f);
        prt.anchorMax = new Vector2(0.5f, 0.5f);
        prt.pivot = new Vector2(0.5f, 0.5f);
        prt.sizeDelta = new Vector2(420, 90);
        prt.anchoredPosition = Vector2.zero;
        var bg = panel.GetComponent<Image>();
        bg.color = new Color(0.02f, 0.06f, 0.10f, 0.85f);
        bg.raycastTarget = true;

        // Border outline (4 thin strips)
        SpawnStrip(prt, "Top",    new Vector2(0,  prt.sizeDelta.y * 0.5f - 1), new Vector2(prt.sizeDelta.x, 2));
        SpawnStrip(prt, "Bottom", new Vector2(0, -prt.sizeDelta.y * 0.5f + 1), new Vector2(prt.sizeDelta.x, 2));
        SpawnStrip(prt, "Left",   new Vector2(-prt.sizeDelta.x * 0.5f + 1, 0), new Vector2(2, prt.sizeDelta.y));
        SpawnStrip(prt, "Right",  new Vector2( prt.sizeDelta.x * 0.5f - 1, 0), new Vector2(2, prt.sizeDelta.y));

        // Label
        var labelGo = new GameObject("Label", typeof(RectTransform));
        labelGo.transform.SetParent(panel.transform, false);
        var label = labelGo.AddComponent<TextMeshProUGUI>();
        label.text = promptLabel;
        label.color = uiTint;
        label.fontSize = fontSize - 2;
        label.alignment = TextAlignmentOptions.Left;
        label.fontStyle = FontStyles.Bold;
        label.raycastTarget = false;
        var lrt = (RectTransform)labelGo.transform;
        lrt.anchorMin = new Vector2(0, 1);
        lrt.anchorMax = new Vector2(0, 1);
        lrt.pivot = new Vector2(0, 1);
        lrt.anchoredPosition = new Vector2(14, -8);
        lrt.sizeDelta = new Vector2(prt.sizeDelta.x - 28, 22);

        // Input field
        var inputGo = new GameObject("Input", typeof(RectTransform), typeof(Image));
        inputGo.transform.SetParent(panel.transform, false);
        var irt = (RectTransform)inputGo.transform;
        irt.anchorMin = new Vector2(0, 0);
        irt.anchorMax = new Vector2(1, 0);
        irt.pivot = new Vector2(0.5f, 0);
        irt.anchoredPosition = new Vector2(0, 12);
        irt.sizeDelta = new Vector2(-28, 38);
        var inputBg = inputGo.GetComponent<Image>();
        inputBg.color = new Color(0.05f, 0.12f, 0.16f, 0.95f);

        // Text & placeholder children
        var textGo = new GameObject("Text", typeof(RectTransform));
        textGo.transform.SetParent(inputGo.transform, false);
        var text = textGo.AddComponent<TextMeshProUGUI>();
        text.color = uiTint;
        text.fontSize = fontSize;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.raycastTarget = false;
        StretchAndPad(textGo, 10, 4);

        var placeGo = new GameObject("Placeholder", typeof(RectTransform));
        placeGo.transform.SetParent(inputGo.transform, false);
        var placeholder = placeGo.AddComponent<TextMeshProUGUI>();
        placeholder.text = "Type a name and press Enter…";
        placeholder.color = new Color(uiTint.r, uiTint.g, uiTint.b, 0.45f);
        placeholder.fontSize = fontSize;
        placeholder.alignment = TextAlignmentOptions.MidlineLeft;
        placeholder.fontStyle = FontStyles.Italic;
        placeholder.raycastTarget = false;
        StretchAndPad(placeGo, 10, 4);

        inputField = inputGo.AddComponent<TMP_InputField>();
        inputField.textComponent = text;
        inputField.placeholder = placeholder;
        inputField.targetGraphic = inputBg;
        inputField.lineType = TMP_InputField.LineType.SingleLine;
        inputField.characterLimit = 32;
    }

    private void SpawnStrip(Transform parent, string name, Vector2 anchored, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchored;
        rt.sizeDelta = size;
        go.GetComponent<Image>().color = uiTint;
    }

    private static void StretchAndPad(GameObject go, float padX, float padY)
    {
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(padX, padY);
        rt.offsetMax = new Vector2(-padX, -padY);
    }
}
