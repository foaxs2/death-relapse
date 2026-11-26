using System;
using UnityEngine;

/// <summary>
/// Cầu nối nhận Animation Event từ GameObject chứa Animator của Player (Knight):
/// - Chuyển tiếp sự kiện mở/đóng hitbox chém lên PlayerAttackController.
/// - Phát sự kiện bước chân chạm đất đúng nhịp Animation cho CombatAudioController (chỉ khi đang chạy thật trên sàn).
/// </summary>
public class PlayerAnimationEventRelay : MonoBehaviour
{
    [Header("Footstep Sync Protection")]
    [Tooltip("Khoảng thời gian tối thiểu giữa 2 tiếng bước chân (giây) để chống phát trùng ở biên vòng lặp")]
    [Min(0.05f)]
    [SerializeField] private float minFootstepIntervalSeconds = 0.16f;

    [Tooltip("Trọng số tối thiểu của clip Run khi đang transition để bỏ qua event thừa lúc nhân vật đã dừng")]
    [Range(0f, 1f)]
    [SerializeField] private float minRunClipWeight = 0.45f;

    private PlayerAttackController attackController;
    private PlayerMovement playerMovement;
    private PlayerDodge playerDodge;
    private PlayerHealth playerHealth;
    private CharacterController characterController;
    private float lastFootstepTime = -999f;

    /// <summary>
    /// Sự kiện phát ra đúng thời điểm bàn chân Knight chạm đất trong animation chạy.
    /// </summary>
    public event Action OnFootstepLanded;

    private void Awake()
    {
        attackController = GetComponentInParent<PlayerAttackController>();
        if (attackController == null)
        {
            attackController = GetComponent<PlayerAttackController>();
        }

        playerMovement = GetComponentInParent<PlayerMovement>();
        playerDodge = GetComponentInParent<PlayerDodge>();
        playerHealth = GetComponentInParent<PlayerHealth>();
        characterController = GetComponentInParent<CharacterController>();
    }

    /// <summary>
    /// Được gọi bởi Animation Event trên clip Run With Sword tại đúng frame bàn chân chạm đất.
    /// </summary>
    public void OnPlayerFootstep(AnimationEvent animEvent)
    {
        // Bỏ qua nếu clip Run đang mờ dần lúc chuyển về Idle/Roll/Attack để tránh phát dư sau khi dừng chạy
        if (animEvent != null && animEvent.animatorClipInfo.clip != null && animEvent.animatorClipInfo.weight < minRunClipWeight)
        {
            return;
        }

        if (!CanEmitFootstepNow())
        {
            return;
        }

        lastFootstepTime = Time.time;
        OnFootstepLanded?.Invoke();
    }

    /// <summary>
    /// Overload không tham số để tương thích nếu Animation Event được thêm thủ công không truyền AnimationEvent.
    /// </summary>
    public void OnPlayerFootstep()
    {
        if (!CanEmitFootstepNow())
        {
            return;
        }

        lastFootstepTime = Time.time;
        OnFootstepLanded?.Invoke();
    }

    private bool CanEmitFootstepNow()
    {
        if (Time.time - lastFootstepTime < minFootstepIntervalSeconds)
        {
            return false;
        }

        if (playerHealth != null && playerHealth.IsDead) return false;
        if (playerDodge != null && playerDodge.IsRolling) return false;
        if (attackController != null && attackController.IsAttacking) return false;

        if (playerMovement != null)
        {
            if (playerMovement.IsMovementLocked) return false;
            if (playerMovement.CurrentInputDirection.sqrMagnitude < 0.01f) return false;
        }

        if (characterController != null)
        {
            // Trên dốc nhẹ của bãi hoa isGrounded đôi lúc rớt 1 frame; vẫn chặn khi rơi tự do từ trên cao
            bool onGroundOrGentleSlope = characterController.isGrounded || Mathf.Abs(characterController.velocity.y) < 3.5f;
            if (!onGroundOrGentleSlope) return false;

            Vector3 horizontalVel = characterController.velocity;
            horizontalVel.y = 0f;
            if (horizontalVel.sqrMagnitude < 0.25f) return false;
        }

        return true;
    }

    /// <summary>
    /// Được gọi bởi Animation Event trên clip Slash tại frame kiếm bắt đầu gây sát thương.
    /// </summary>
    public void OnPlayerHitWindowOpen()
    {
        if (attackController == null) return;
        attackController.NotifyHitWindowOpen();
    }

    /// <summary>
    /// Được gọi bởi Animation Event trên clip Slash tại frame kiếm kết thúc pha gây sát thương.
    /// </summary>
    public void OnPlayerHitWindowClose()
    {
        if (attackController == null) return;
        attackController.NotifyHitWindowClose();
    }

    /// <summary>
    /// Alias tương thích nếu người dùng đặt tên Animation Event giống bên Boss (OnHitWindowOpen).
    /// </summary>
    public void OnHitWindowOpen()
    {
        OnPlayerHitWindowOpen();
    }

    /// <summary>
    /// Alias tương thích nếu người dùng đặt tên Animation Event giống bên Boss (OnHitWindowClose).
    /// </summary>
    public void OnHitWindowClose()
    {
        OnPlayerHitWindowClose();
    }
}
