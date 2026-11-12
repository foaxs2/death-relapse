using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Quản lý hành động né đòn (Roll Dodge) của nhân vật bằng animation Sprinting Forward Roll.
/// Cung cấp trạng thái bất tử (I-frame) để tránh đòn tấn công của kẻ thù.
/// Mọi thông số tốc độ, thời lượng và i-frame đều tinh chỉnh được trong Inspector.
/// </summary>
[RequireComponent(typeof(PlayerMovement))]
[RequireComponent(typeof(CharacterController))]
public class PlayerDodge : MonoBehaviour
{
    [Header("Input References")]
    [Tooltip("Tham chiếu Input Action dùng để kích hoạt né (hoặc phím Space mặc định)")]
    [SerializeField] private InputActionReference dodgeAction;

    [Header("Roll Mechanics")]
    [Tooltip("Tốc độ di chuyển lướt về phía trước khi đang lăn (m/s)")]
    [SerializeField] private float rollSpeed = 8.5f;

    [Tooltip("Tổng thời gian thực hiện động tác lăn (giây) - nên khớp với độ dài animation lăn")]
    [SerializeField] private float rollDuration = 0.75f;

    [Tooltip("Thời gian hồi giữa 2 lần lăn né liên tiếp (giây)")]
    [SerializeField] private float rollCooldown = 0.2f;

    [Tooltip("Đường cong suy giảm tốc độ trong lúc lăn (0 = bắt đầu cú lăn, 1 = kết thúc cú lăn)")]
    [SerializeField] private AnimationCurve speedCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0.4f);

    [Header("I-Frame (Invulnerability)")]
    [Tooltip("Thời điểm bắt đầu bật bất tử tính từ lúc bấm né (giây)")]
    [SerializeField] private float iFrameStartTime = 0.05f;

    [Tooltip("Thời lượng bất tử (I-frame) bảo vệ nhân vật khỏi sát thương (giây)")]
    [SerializeField] private float iFrameDuration = 0.45f;

    [Header("Animation")]
    [Tooltip("Tên Trigger trong Animator của Player để kích hoạt animation lăn")]
    [SerializeField] private string rollTriggerName = "Roll";

    // Trạng thái runtime
    private bool isRolling = false;
    private bool isInvulnerable = false;
    private float lastRollEndTime = -999f;
    private Coroutine rollCoroutine;

    // Component tham chiếu
    private PlayerMovement playerMovement;
    private CharacterController characterController;
    private Animator animator;

    // Cache mã hash trigger Animator để tối ưu CPU
    private int rollTriggerHash;

    /// <summary>
    /// Cho biết nhân vật có đang trong trạng thái bất tử né đòn (I-frame) hay không.
    /// Dùng cho hệ thống sát thương / nhận đòn để miễn nhiễm sát thương.
    /// </summary>
    public bool IsInvulnerable => isInvulnerable;

    /// <summary>
    /// Cho biết nhân vật có đang trong hoạt cảnh lăn né đòn hay không.
    /// </summary>
    public bool IsRolling => isRolling;

    /// <summary>
    /// Sự kiện C# phát ra khi bắt đầu né đòn (truyền vào hướng né).
    /// </summary>
    public event Action<Vector3> OnRollStarted;

    /// <summary>
    /// Sự kiện C# phát ra khi kết thúc né đòn.
    /// </summary>
    public event Action OnRollEnded;

    private void Awake()
    {
        playerMovement = GetComponent<PlayerMovement>();
        characterController = GetComponent<CharacterController>();
        animator = GetComponentInChildren<Animator>();

        if (!string.IsNullOrEmpty(rollTriggerName))
        {
            rollTriggerHash = Animator.StringToHash(rollTriggerName);
        }
    }

    private void OnEnable()
    {
        // Loại bỏ hoàn toàn phím Shift, chỉ lắng nghe phím Space trong Update
    }

    private void OnDisable()
    {
        if (rollCoroutine != null)
        {
            StopCoroutine(rollCoroutine);
            rollCoroutine = null;
        }

        // Đảm bảo trả lại quyền điều khiển nếu script bị tắt giữa chừng
        if (isRolling)
        {
            EndRoll();
        }
    }

    private void Update()
    {
        // Chỉ dùng phím Space (hoặc nút A trên tay cầm) để lăn né, loại bỏ hoàn toàn phím Shift
        bool spacePressed = Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;
        bool gamepadPressed = Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame;

        if (spacePressed || gamepadPressed)
        {
            TryStartRoll();
        }
    }

    /// <summary>
    /// Kiểm tra điều kiện và thực hiện cú lăn né đòn.
    /// </summary>
    public bool TryStartRoll()
    {
        // Không cho phép né nếu đang lăn, đang bị khóa di chuyển khác, hoặc chưa hồi chiêu
        if (isRolling || (playerMovement != null && playerMovement.IsMovementLocked))
        {
            return false;
        }

        if (Time.time < lastRollEndTime + rollCooldown)
        {
            return false;
        }

        // Xác định hướng lăn: ưu tiên hướng đang bấm phím WASD; nếu đứng yên thì lăn theo hướng mặt nhân vật
        Vector3 rollDirection = playerMovement != null ? playerMovement.CurrentInputDirection : Vector3.zero;
        if (rollDirection.sqrMagnitude < 0.001f)
        {
            rollDirection = transform.forward;
        }
        rollDirection = Vector3.ProjectOnPlane(rollDirection, Vector3.up).normalized;

        rollCoroutine = StartCoroutine(RollRoutine(rollDirection));
        return true;
    }

    /// <summary>
    /// Coroutine điều khiển toàn bộ tiến trình lăn né đòn:
    /// Khóa di chuyển thường -> Xoay ngay về hướng né -> Kích hoạt Animation -> Quản lý I-frame và di chuyển lướt.
    /// </summary>
    private IEnumerator RollRoutine(Vector3 rollDirection)
    {
        isRolling = true;
        isInvulnerable = false;

        // 1. Khóa di chuyển thường để PlayerMovement không can thiệp input
        if (playerMovement != null)
        {
            playerMovement.SetMovementLocked(true);
        }

        // 2. Xoay tức thì nhân vật về hướng lăn
        if (rollDirection.sqrMagnitude > 0.001f)
        {
            transform.rotation = Quaternion.LookRotation(rollDirection, Vector3.up);
        }

        // 3. Kích hoạt trigger trong Animator
        if (animator != null && rollTriggerHash != 0)
        {
            animator.ResetTrigger(rollTriggerHash);
            animator.SetTrigger(rollTriggerHash);
        }

        OnRollStarted?.Invoke(rollDirection);

        float elapsedTime = 0f;
        float iFrameEndTime = iFrameStartTime + iFrameDuration;

        // 4. Vòng lặp cập nhật di chuyển và quản lý I-frame theo thời gian
        while (elapsedTime < rollDuration)
        {
            float dt = Time.deltaTime;
            elapsedTime += dt;

            // Cập nhật trạng thái I-Frame bất tử
            bool shouldBeInvulnerable = (elapsedTime >= iFrameStartTime && elapsedTime <= iFrameEndTime);
            if (shouldBeInvulnerable != isInvulnerable)
            {
                isInvulnerable = shouldBeInvulnerable;
            }

            // Tính toán tốc độ lướt từ curve và di chuyển nhân vật
            float progress = Mathf.Clamp01(elapsedTime / rollDuration);
            float speedMultiplier = speedCurve.Evaluate(progress);
            Vector3 horizontalMove = rollDirection * (rollSpeed * speedMultiplier);

            // Tận dụng hàm MoveWithExternalVelocity của PlayerMovement để xử lý đồng bộ cả trọng lực
            if (playerMovement != null)
            {
                playerMovement.MoveWithExternalVelocity(horizontalMove);
            }
            else
            {
                characterController.Move((horizontalMove + Vector3.down * 9.81f) * dt);
            }

            yield return null;
        }

        EndRoll();
    }

    /// <summary>
    /// Hủy ngay lập tức cú lăn hiện tại (dùng khi bị trúng đòn ngoài khoảng I-frame hoặc khi chết).
    /// </summary>
    public void CancelRoll()
    {
        if (!isRolling && rollCoroutine == null) return;

        if (rollCoroutine != null)
        {
            StopCoroutine(rollCoroutine);
            rollCoroutine = null;
        }

        isRolling = false;
        isInvulnerable = false;
        lastRollEndTime = Time.time;
        OnRollEnded?.Invoke();
    }

    /// <summary>
    /// Kết thúc trạng thái lăn né, hoàn trả quyền di chuyển bình thường.
    /// </summary>
    private void EndRoll()
    {
        isRolling = false;
        isInvulnerable = false;
        lastRollEndTime = Time.time;

        if (playerMovement != null)
        {
            playerMovement.SetMovementLocked(false);
        }

        rollCoroutine = null;
        OnRollEnded?.Invoke();
    }

    private void OnDrawGizmosSelected()
    {
        // Hiển thị trực quan trạng thái I-Frame trong Scene View khi đang test
        if (isInvulnerable)
        {
            Gizmos.color = new Color(0f, 1f, 1f, 0.6f); // Xanh ngọc khi bất tử
            Gizmos.DrawWireSphere(transform.position + Vector3.up, 0.6f);
        }
    }
}

