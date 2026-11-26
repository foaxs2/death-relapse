using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Màn hình hướng dẫn điều khiển lần đầu (Onboarding Overlay) mang phong cách Dark Fantasy.
/// Tự sinh UI TextMeshPro gọn đẹp nếu chưa có sẵn trong Scene, bật ở lần mở đầu tiên và cho phép bật/tắt bằng phím H.
/// Khóa tạm input tấn công của Player và AI của Boss khi đang mở để người chơi đọc thoải mái.
/// </summary>
public class FirstTimeControlsOverlay : MonoBehaviour
{
    private const string OnboardingSeenPrefKey = "DeathRelapse_OnboardingSeen_v09";

    [Header("Behavior")]
    [Tooltip("Tự động hiển thị bảng hướng dẫn ở lần mở game đầu tiên")]
    [SerializeField] private bool showOnFirstLaunch = true;

    [Tooltip("Độ trễ ngắn (giây thực) trước khi cho phép nhấn phím bất kỳ để đóng, tránh đóng nhầm do giữ phím")]
    [Min(0f)]
    [SerializeField] private float dismissGraceSeconds = 0.25f;

    [Header("Optional Pre-built UI References (Để trống sẽ tự sinh runtime)")]
    [SerializeField] private GameObject overlayRoot;
    [SerializeField] private Button startButton;

    [Header("Gameplay References (Tự tìm trong Scene nếu để trống)")]
    [SerializeField] private PlayerMovement playerMovement;
    [SerializeField] private PlayerAttackController playerAttackController;
    [SerializeField] private PlayerDodge playerDodge;
    [SerializeField] private PlayerHealth playerHealth;
    [SerializeField] private BossAttackController bossAttackController;
    [SerializeField] private BattleLoopManager battleLoopManager;

    private InputAction toggleHelpAction;
    private InputAction dismissKeyAction;
    private bool isOverlayOpen;
    private float openedRealtime;
    private int lastClosedFrame = -1;

    // Lưu trạng thái bật/tắt trước khi khóa để khôi phục chính xác khi đóng overlay
    private bool hadPlayerAttackEnabled;
    private bool hadPlayerDodgeEnabled;
    private bool hadBossAttackEnabled;
    private bool hadMovementLocked;

    public bool IsOverlayOpen => isOverlayOpen;
    public bool JustClosedThisFrame => lastClosedFrame == Time.frameCount;

    private void Awake()
    {
        CacheGameplayReferences();
        EnsureOverlayUI();

        if (startButton != null)
        {
            startButton.onClick.RemoveListener(CloseOverlay);
            startButton.onClick.AddListener(CloseOverlay);
        }

        // Chỉ dùng Input System mới (bật trong OnEnable, tắt trong OnDisable)
        toggleHelpAction = new InputAction("ToggleControlsOverlay", InputActionType.Button, "<Keyboard>/h");
        dismissKeyAction = new InputAction("DismissControlsOverlay", InputActionType.Button);
        dismissKeyAction.AddBinding("<Keyboard>/space");
        dismissKeyAction.AddBinding("<Keyboard>/enter");
        dismissKeyAction.AddBinding("<Keyboard>/escape");
        dismissKeyAction.AddBinding("<Keyboard>/anyKey");
    }

    private void OnEnable()
    {
        if (toggleHelpAction != null)
        {
            toggleHelpAction.performed += OnToggleHelpPerformed;
            toggleHelpAction.Enable();
        }

        if (dismissKeyAction != null)
        {
            dismissKeyAction.performed += OnDismissKeyPerformed;
            dismissKeyAction.Enable();
        }
    }

    private void Start()
    {
        bool alreadySeen = PlayerPrefs.GetInt(OnboardingSeenPrefKey, 0) == 1;
        if (showOnFirstLaunch && !alreadySeen)
        {
            OpenOverlay();
        }
        else if (overlayRoot != null)
        {
            overlayRoot.SetActive(false);
            isOverlayOpen = false;
        }
    }

    private void OnDisable()
    {
        if (toggleHelpAction != null)
        {
            toggleHelpAction.performed -= OnToggleHelpPerformed;
            toggleHelpAction.Disable();
        }

        if (dismissKeyAction != null)
        {
            dismissKeyAction.performed -= OnDismissKeyPerformed;
            dismissKeyAction.Disable();
        }

        if (isOverlayOpen)
        {
            SetCombatLocked(false);
            isOverlayOpen = false;
        }
    }

    private void OnDestroy()
    {
        toggleHelpAction?.Dispose();
        dismissKeyAction?.Dispose();
    }

    private void OnToggleHelpPerformed(InputAction.CallbackContext context)
    {
        if (PauseMenuOverlay.IsGamePaused || PauseMenuOverlay.JustResumedThisFrame || (battleLoopManager != null && battleLoopManager.IsBattleEnded))
        {
            return;
        }

        if (isOverlayOpen)
        {
            CloseOverlay();
        }
        else
        {
            OpenOverlay();
        }
    }

    private void OnDismissKeyPerformed(InputAction.CallbackContext context)
    {
        if (!isOverlayOpen) return;

        // Phím H đã có action riêng để bật/tắt, bỏ qua ở nhánh anyKey để tránh đóng rồi mở lặp
        if (context.control != null && context.control.name == "h")
        {
            return;
        }

        if (Time.realtimeSinceStartup - openedRealtime < dismissGraceSeconds)
        {
            return;
        }

        CloseOverlay();
    }

    /// <summary>Mở bảng hướng dẫn và tạm khóa chiến đấu.</summary>
    public void OpenOverlay()
    {
        EnsureOverlayUI();
        if (overlayRoot == null) return;

        if (!isOverlayOpen)
        {
            SetCombatLocked(true);
        }

        isOverlayOpen = true;
        openedRealtime = Time.realtimeSinceStartup;
        overlayRoot.SetActive(true);
        overlayRoot.transform.SetAsLastSibling();
    }

    /// <summary>Đóng bảng hướng dẫn, lưu cờ đã xem lần đầu và trả lại quyền điều khiển.</summary>
    public void CloseOverlay()
    {
        if (overlayRoot != null)
        {
            overlayRoot.SetActive(false);
        }

        PlayerPrefs.SetInt(OnboardingSeenPrefKey, 1);
        PlayerPrefs.Save();

        if (isOverlayOpen)
        {
            isOverlayOpen = false;
            lastClosedFrame = Time.frameCount;
            // Nhả khóa ở cuối frame để phím Space vừa nhấn để đóng không kích hoạt lăn né ngay lập tức
            if (isActiveAndEnabled)
            {
                StartCoroutine(ReleaseCombatLockNextFrame());
            }
            else
            {
                SetCombatLocked(false);
            }
        }
    }

    [ContextMenu("Reset First-Time Onboarding Flag")]
    public void ResetOnboardingFlag()
    {
        PlayerPrefs.DeleteKey(OnboardingSeenPrefKey);
        PlayerPrefs.Save();
    }

    private IEnumerator ReleaseCombatLockNextFrame()
    {
        yield return null;
        if (!isOverlayOpen)
        {
            SetCombatLocked(false);
        }
    }

    private void SetCombatLocked(bool locked)
    {
        CacheGameplayReferences();

        if (locked)
        {
            hadMovementLocked = playerMovement != null && playerMovement.IsMovementLocked;
            hadPlayerAttackEnabled = playerAttackController != null && playerAttackController.enabled;
            hadPlayerDodgeEnabled = playerDodge != null && playerDodge.enabled;
            hadBossAttackEnabled = bossAttackController != null && bossAttackController.enabled;

            if (playerMovement != null) playerMovement.SetMovementLocked(true);
            if (playerAttackController != null) playerAttackController.enabled = false;
            if (playerDodge != null) playerDodge.enabled = false;
            if (bossAttackController != null) bossAttackController.enabled = false;
        }
        else
        {
            bool battleEnded = battleLoopManager != null && battleLoopManager.IsBattleEnded;
            bool playerDead = playerHealth != null && playerHealth.IsDead;

            if (!battleEnded && !playerDead)
            {
                if (playerMovement != null) playerMovement.SetMovementLocked(hadMovementLocked);
                if (playerAttackController != null) playerAttackController.enabled = hadPlayerAttackEnabled;
                if (playerDodge != null) playerDodge.enabled = hadPlayerDodgeEnabled;
            }

            if (!battleEnded && bossAttackController != null)
            {
                bossAttackController.enabled = hadBossAttackEnabled;
            }
        }
    }

    private void CacheGameplayReferences()
    {
        if (playerMovement == null) playerMovement = FindFirstObjectByType<PlayerMovement>();
        if (playerAttackController == null) playerAttackController = FindFirstObjectByType<PlayerAttackController>();
        if (playerDodge == null) playerDodge = FindFirstObjectByType<PlayerDodge>();
        if (playerHealth == null) playerHealth = FindFirstObjectByType<PlayerHealth>();
        if (bossAttackController == null) bossAttackController = FindFirstObjectByType<BossAttackController>();
        if (battleLoopManager == null) battleLoopManager = FindFirstObjectByType<BattleLoopManager>();
    }

    private void EnsureOverlayUI()
    {
        if (overlayRoot != null) return;

        Canvas canvas = GetComponent<Canvas>();
        if (canvas == null)
        {
            canvas = GetComponentInParent<Canvas>();
        }
        if (canvas == null)
        {
            canvas = FindFirstObjectByType<Canvas>();
        }
        if (canvas == null) return;

        TMP_FontAsset sharedFont = ResolveVietnameseFont(canvas);

        // 1. Nền tối phủ toàn màn hình (chặn click xuyên xuống gameplay)
        overlayRoot = new GameObject("FirstTimeControlsOverlayRoot", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        overlayRoot.transform.SetParent(canvas.transform, false);

        RectTransform rootRect = overlayRoot.GetComponent<RectTransform>();
        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.one;
        rootRect.offsetMin = Vector2.zero;
        rootRect.offsetMax = Vector2.zero;

        Image backdrop = overlayRoot.GetComponent<Image>();
        backdrop.color = new Color(0.04f, 0.04f, 0.06f, 0.86f);
        backdrop.raycastTarget = true;

        // 2. Khung thẻ Dark Fantasy ở giữa màn hình
        GameObject cardObj = new GameObject("DarkFantasyCard", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Outline));
        cardObj.transform.SetParent(overlayRoot.transform, false);

        RectTransform cardRect = cardObj.GetComponent<RectTransform>();
        cardRect.anchorMin = new Vector2(0.5f, 0.5f);
        cardRect.anchorMax = new Vector2(0.5f, 0.5f);
        cardRect.pivot = new Vector2(0.5f, 0.5f);
        cardRect.sizeDelta = new Vector2(820f, 520f);

        Image cardBg = cardObj.GetComponent<Image>();
        cardBg.color = new Color(0.09f, 0.08f, 0.11f, 0.96f);

        Outline cardBorder = cardObj.GetComponent<Outline>();
        cardBorder.effectColor = new Color(0.76f, 0.58f, 0.24f, 0.85f);
        cardBorder.effectDistance = new Vector2(2f, -2f);

        // 3. Tiêu đề chính
        CreateTmpLabel(
            "TitleText", cardObj.transform, sharedFont,
            "HƯỚNG DẪN",
            40f, FontStyles.Bold, new Color(0.92f, 0.77f, 0.40f, 1f),
            TextAlignmentOptions.Center,
            new Vector2(0f, 195f), new Vector2(740f, 56f));

        // 4. Đường kẻ nhấn sắc đỏ thẫm
        GameObject divider = new GameObject("Divider", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        divider.transform.SetParent(cardObj.transform, false);
        RectTransform divRect = divider.GetComponent<RectTransform>();
        divRect.anchoredPosition = new Vector2(0f, 152f);
        divRect.sizeDelta = new Vector2(520f, 3f);
        divider.GetComponent<Image>().color = new Color(0.68f, 0.16f, 0.16f, 0.9f);

        // 5. Nội dung phím điều khiển và mục tiêu
        const string controlsBody =
            "<color=#EBC566><b>WASD</b></color> :  Di chuyển\n" +
            "<color=#EBC566><b>Chuột trái</b></color> :  Tấn công\n" +
            "<color=#EBC566><b>Space</b></color> :  Né\n" +
            "<color=#EBC566><b>Phím H</b></color> :  Bật / Tắt bảng hướng dẫn\n\n" +
            "<color=#E05A5A><b>Mục tiêu:</b></color>  Quan sát động tác ra đòn của Boss, né tránh đúng nhịp và hạ gục Boss.";

        CreateTmpLabel(
            "ControlsBodyText", cardObj.transform, sharedFont,
            controlsBody,
            26f, FontStyles.Normal, new Color(0.92f, 0.91f, 0.88f, 1f),
            TextAlignmentOptions.Left,
            new Vector2(0f, 12f), new Vector2(700f, 235f));

        // 6. Nút "BẮT ĐẦU CHIẾN ĐẤU"
        GameObject btnObj = new GameObject("StartBattleButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(Outline));
        btnObj.transform.SetParent(cardObj.transform, false);

        RectTransform btnRect = btnObj.GetComponent<RectTransform>();
        btnRect.anchoredPosition = new Vector2(0f, -155f);
        btnRect.sizeDelta = new Vector2(380f, 68f);

        Image btnBg = btnObj.GetComponent<Image>();
        btnBg.color = new Color(0.55f, 0.12f, 0.14f, 1f);

        Outline btnOutline = btnObj.GetComponent<Outline>();
        btnOutline.effectColor = new Color(0.92f, 0.77f, 0.40f, 0.85f);
        btnOutline.effectDistance = new Vector2(1.5f, -1.5f);

        startButton = btnObj.GetComponent<Button>();
        startButton.targetGraphic = btnBg;
        ColorBlock colors = startButton.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.15f, 1.05f, 1.05f, 1f);
        colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
        startButton.colors = colors;

        TMP_Text btnLabel = CreateTmpLabel(
            "ButtonLabel", btnObj.transform, sharedFont,
            "XÁC NHẬN",
            28f, FontStyles.Bold, Color.white,
            TextAlignmentOptions.Center,
            Vector2.zero, new Vector2(360f, 56f));
        btnLabel.raycastTarget = false;

        // 7. Ghi chú nhỏ phía dưới nút
        CreateTmpLabel(
            "FooterHintText", cardObj.transform, sharedFont,
            "Nhấn nút trên hoặc bấm Space / bất kỳ phím nào để xác nhận",
            19f, FontStyles.Italic, new Color(0.72f, 0.70f, 0.66f, 0.9f),
            TextAlignmentOptions.Center,
            new Vector2(0f, -214f), new Vector2(740f, 34f));
    }

    private static TMP_Text CreateTmpLabel(
        string name,
        Transform parent,
        TMP_FontAsset font,
        string content,
        float fontSize,
        FontStyles style,
        Color color,
        TextAlignmentOptions alignment,
        Vector2 anchoredPos,
        Vector2 sizeDelta)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        obj.transform.SetParent(parent, false);

        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = sizeDelta;

        TextMeshProUGUI tmp = obj.GetComponent<TextMeshProUGUI>();
        if (font != null)
        {
            tmp.font = font;
        }
        tmp.text = content;
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.color = color;
        tmp.alignment = alignment;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        return tmp;
    }

    private static TMP_FontAsset ResolveVietnameseFont(Canvas canvas)
    {
        if (canvas != null)
        {
            TMP_Text existing = canvas.GetComponentInChildren<TMP_Text>(true);
            if (existing != null && existing.font != null)
            {
                return existing.font;
            }
        }
        return TMP_Settings.defaultFontAsset;
    }
}
