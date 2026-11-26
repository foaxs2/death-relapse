using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Màn hình Tạm Dừng (Pause):
/// - Bấm ESC trong lúc đang chơi để ngưng game (Time.timeScale = 0) và chỉ hiển thị đúng chữ "Tạm Dừng".
/// - Bấm phím bất kỳ trên bàn phím để tiếp tục game.
/// - Không chồng lấn lên FirstTimeControlsOverlay hoặc màn hình Thắng/Thua.
/// </summary>
public class PauseMenuOverlay : MonoBehaviour
{
    [Header("Gameplay References (Tự tìm trong Scene nếu để trống)")]
    [SerializeField] private PlayerMovement playerMovement;
    [SerializeField] private PlayerAttackController playerAttackController;
    [SerializeField] private PlayerDodge playerDodge;
    [SerializeField] private PlayerHealth playerHealth;
    [SerializeField] private BossAttackController bossAttackController;
    [SerializeField] private BattleLoopManager battleLoopManager;
    [SerializeField] private FirstTimeControlsOverlay controlsOverlay;

    [Header("Optional Pre-built UI (Để trống sẽ tự sinh runtime chỉ gồm nền tối + chữ 'Tạm Dừng')")]
    [SerializeField] private GameObject pauseOverlayRoot;

    private InputAction pauseEscAction;
    private InputAction resumeAnyKeyAction;
    private bool isPaused;
    private float pausedRealtime;

    // Lưu trạng thái khóa trước khi Pause để trả lại chính xác khi Resume
    private bool hadMovementLocked;
    private bool hadPlayerAttackEnabled;
    private bool hadPlayerDodgeEnabled;
    private bool hadBossAttackEnabled;

    private const float ResumeGraceSeconds = 0.15f;

    private static int lastResumedFrame = -1;

    /// <summary>Cho các overlay khác kiểm tra xem game có đang Tạm Dừng hay không.</summary>
    public static bool IsGamePaused { get; private set; }
    public static bool JustResumedThisFrame => lastResumedFrame == Time.frameCount;

    public bool IsPaused => isPaused;

    private void Awake()
    {
        CacheReferences();
        EnsurePauseUI();

        if (pauseOverlayRoot != null)
        {
            pauseOverlayRoot.SetActive(false);
        }

        // Input System mới: bật trong OnEnable, tắt trong OnDisable
        pauseEscAction = new InputAction("PauseEscapeAction", InputActionType.Button, "<Keyboard>/escape");
        resumeAnyKeyAction = new InputAction("ResumeAnyKeyAction", InputActionType.Button, "<Keyboard>/anyKey");
    }

    private void OnEnable()
    {
        if (pauseEscAction != null)
        {
            pauseEscAction.performed += OnEscapePerformed;
            pauseEscAction.Enable();
        }

        if (resumeAnyKeyAction != null)
        {
            resumeAnyKeyAction.performed += OnAnyKeyWhilePaused;
            resumeAnyKeyAction.Enable();
        }
    }

    private void OnDisable()
    {
        if (pauseEscAction != null)
        {
            pauseEscAction.performed -= OnEscapePerformed;
            pauseEscAction.Disable();
        }

        if (resumeAnyKeyAction != null)
        {
            resumeAnyKeyAction.performed -= OnAnyKeyWhilePaused;
            resumeAnyKeyAction.Disable();
        }

        if (isPaused)
        {
            ResumeImmediately();
        }
    }

    private void OnDestroy()
    {
        pauseEscAction?.Dispose();
        resumeAnyKeyAction?.Dispose();
    }

    private void OnEscapePerformed(InputAction.CallbackContext context)
    {
        if (isPaused)
        {
            return;
        }

        // Không bật Pause khi trận đấu đã kết thúc hoặc bảng hướng dẫn lần đầu đang mở
        if (battleLoopManager != null && battleLoopManager.IsBattleEnded)
        {
            return;
        }

        if (controlsOverlay != null && (controlsOverlay.IsOverlayOpen || controlsOverlay.JustClosedThisFrame))
        {
            return;
        }

        PauseGame();
    }

    private void OnAnyKeyWhilePaused(InputAction.CallbackContext context)
    {
        if (!isPaused)
        {
            return;
        }

        // Tránh phím ESC vừa bấm để mở Pause lại tắt ngay ở cùng khung hình
        if (Time.realtimeSinceStartup - pausedRealtime < ResumeGraceSeconds)
        {
            return;
        }

        ResumeGame();
    }

    /// <summary>Ngưng thời gian game và hiển thị đúng chữ "Tạm Dừng".</summary>
    public void PauseGame()
    {
        if (isPaused) return;

        CacheReferences();
        EnsurePauseUI();

        isPaused = true;
        IsGamePaused = true;
        pausedRealtime = Time.realtimeSinceStartup;

        LockCombatInputs(true);
        Time.timeScale = 0f;
        AudioListener.pause = true;

        if (pauseOverlayRoot != null)
        {
            pauseOverlayRoot.SetActive(true);
            pauseOverlayRoot.transform.SetAsLastSibling();
        }
    }

    /// <summary>Tiếp tục game sau khi người chơi bấm phím bất kỳ.</summary>
    public void ResumeGame()
    {
        if (!isPaused) return;

        isPaused = false;
        IsGamePaused = false;
        lastResumedFrame = Time.frameCount;

        if (pauseOverlayRoot != null)
        {
            pauseOverlayRoot.SetActive(false);
        }

        Time.timeScale = 1f;
        AudioListener.pause = false;

        // Trả lại quyền điều khiển ở frame kế tiếp để phím Space/chuột vừa bấm không kích hoạt hành động ngay
        if (isActiveAndEnabled && Application.isPlaying)
        {
            StartCoroutine(UnlockCombatNextFrame());
        }
        else
        {
            LockCombatInputs(false);
        }
    }

    private void ResumeImmediately()
    {
        isPaused = false;
        IsGamePaused = false;
        lastResumedFrame = Time.frameCount;
        if (pauseOverlayRoot != null)
        {
            pauseOverlayRoot.SetActive(false);
        }
        Time.timeScale = 1f;
        AudioListener.pause = false;
        LockCombatInputs(false);
    }

    private IEnumerator UnlockCombatNextFrame()
    {
        yield return null;
        if (!isPaused)
        {
            LockCombatInputs(false);
        }
    }

    private void LockCombatInputs(bool locked)
    {
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

    private void CacheReferences()
    {
        if (playerMovement == null) playerMovement = FindFirstObjectByType<PlayerMovement>();
        if (playerAttackController == null) playerAttackController = FindFirstObjectByType<PlayerAttackController>();
        if (playerDodge == null) playerDodge = FindFirstObjectByType<PlayerDodge>();
        if (playerHealth == null) playerHealth = FindFirstObjectByType<PlayerHealth>();
        if (bossAttackController == null) bossAttackController = FindFirstObjectByType<BossAttackController>();
        if (battleLoopManager == null) battleLoopManager = FindFirstObjectByType<BattleLoopManager>();
        if (controlsOverlay == null) controlsOverlay = FindFirstObjectByType<FirstTimeControlsOverlay>();
    }

    private void EnsurePauseUI()
    {
        if (pauseOverlayRoot != null) return;

        Canvas canvas = GetComponent<Canvas>();
        if (canvas == null) canvas = GetComponentInParent<Canvas>();
        if (canvas == null) canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null) return;

        // Canvas phủ mờ chỉ chứa DUY NHẤT 1 Text "Tạm Dừng" theo đúng yêu cầu
        pauseOverlayRoot = new GameObject("PauseOverlayRoot", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        pauseOverlayRoot.transform.SetParent(canvas.transform, false);

        RectTransform rootRect = pauseOverlayRoot.GetComponent<RectTransform>();
        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.one;
        rootRect.offsetMin = Vector2.zero;
        rootRect.offsetMax = Vector2.zero;

        Image backdrop = pauseOverlayRoot.GetComponent<Image>();
        backdrop.color = new Color(0f, 0f, 0f, 0.72f);
        backdrop.raycastTarget = true;

        GameObject textObj = new GameObject("PauseText", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textObj.transform.SetParent(pauseOverlayRoot.transform, false);

        RectTransform textRect = textObj.GetComponent<RectTransform>();
        textRect.anchorMin = new Vector2(0.5f, 0.5f);
        textRect.anchorMax = new Vector2(0.5f, 0.5f);
        textRect.pivot = new Vector2(0.5f, 0.5f);
        textRect.anchoredPosition = Vector2.zero;
        textRect.sizeDelta = new Vector2(600f, 120f);

        TextMeshProUGUI tmp = textObj.GetComponent<TextMeshProUGUI>();
        TMP_Text existingTmp = canvas.GetComponentInChildren<TMP_Text>(true);
        if (existingTmp != null && existingTmp.font != null)
        {
            tmp.font = existingTmp.font;
        }

        tmp.text = "Tạm Dừng";
        tmp.fontSize = 64f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.raycastTarget = false;
    }
}
