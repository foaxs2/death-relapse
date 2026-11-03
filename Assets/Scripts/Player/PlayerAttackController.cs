using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Quản lý chu kỳ tấn công bằng kiếm của nhân vật chính (Player).
/// Xoay mặt nhân vật về hướng con trỏ chuột khi ra đòn, kích hoạt animation chém (Slash),
/// điều khiển mở/đóng SwordHitbox và trừ máu Boss đúng 1 lần mỗi nhát chém.
/// </summary>
public class PlayerAttackController : MonoBehaviour
{
    [Header("Attack Configuration")]
    [Tooltip("Sát thương mỗi nhát chém cơ bản (mặc định: 150 HP, có thể chỉnh thành 75 HP trong Inspector)")]
    [Min(0f)]
    [SerializeField] private float attackDamage = 150f;

    [Tooltip("Tổng thời lượng thực hiện 1 nhát chém (giây)")]
    [Min(0.1f)]
    [SerializeField] private float attackDuration = 0.65f;

    [Tooltip("Thời gian hồi giữa 2 nhát chém liên tiếp (giây)")]
    [Min(0f)]
    [SerializeField] private float attackCooldown = 0.15f;

    [Tooltip("Khóa di chuyển WASD trong lúc đang vung kiếm để đòn chém dứt khoát")]
    [SerializeField] private bool lockMovementDuringAttack = true;

    [Header("Hitbox Timing (Fallback nếu chưa gắn Animation Event trên clip Slash)")]
    [Tooltip("Thời điểm tự động mở hitbox kiếm tính từ lúc bấm chém (giây)")]
    [Min(0f)]
    [SerializeField] private float fallbackHitWindowStart = 0.15f;

    [Tooltip("Thời điểm tự động đóng hitbox kiếm tính từ lúc bấm chém (giây)")]
    [Min(0.05f)]
    [SerializeField] private float fallbackHitWindowEnd = 0.45f;

    [Header("Animation")]
    [Tooltip("Tên Trigger trong Animator của Player để kích hoạt đòn chém")]
    [SerializeField] private string attackTriggerName = "Attack";

    [Header("References")]
    [Tooltip("Hitbox kiếm gắn trên vũ khí tay phải của hiệp sĩ")]
    [SerializeField] private SwordHitbox swordHitbox;

    [Tooltip("Animator của nhân vật")]
    [SerializeField] private Animator animator;

    [Tooltip("Input Action kích hoạt tấn công (Player/Attack - Chuột trái)")]
    [SerializeField] private InputActionReference attackAction;

    // Tham chiếu các component trên Player
    private PlayerMovement playerMovement;
    private PlayerDodge playerDodge;
    private PlayerHealth playerHealth;

    // Trạng thái runtime
    private bool isAttacking = false;
    private bool hitWindowOpened = false;
    private bool hitWindowClosed = false;
    private bool hasResolvedHitThisSwing = false;
    private float lastAttackEndTime = -999f;
    private int attackTriggerHash;
    private Coroutine attackCoroutine;

    public float AttackDamage
    {
        get => attackDamage;
        set => attackDamage = Mathf.Max(0f, value);
    }

    public bool IsAttacking => isAttacking;

    /// <summary>
    /// Sự kiện phát ra khi Player chém trúng Boss thành công.
    /// </summary>
    public event Action<BossHealth, float> OnHitBoss;

    private void Awake()
    {
        playerMovement = GetComponent<PlayerMovement>();
        playerDodge = GetComponent<PlayerDodge>();
        playerHealth = GetComponent<PlayerHealth>();

        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        if (swordHitbox == null)
        {
            swordHitbox = GetComponentInChildren<SwordHitbox>();
        }

        if (!string.IsNullOrEmpty(attackTriggerName))
        {
            attackTriggerHash = Animator.StringToHash(attackTriggerName);
        }
    }

    private void OnEnable()
    {
        if (attackAction != null && attackAction.action != null)
        {
            attackAction.action.Enable();
            attackAction.action.performed += OnAttackPerformed;
        }
    }

    private void OnDisable()
    {
        if (attackAction != null && attackAction.action != null)
        {
            attackAction.action.performed -= OnAttackPerformed;
            attackAction.action.Disable();
        }

        CancelAttack();
    }

    private void Update()
    {
        // Fallback an toàn: nếu chưa kéo attackAction vào Inspector thì vẫn nhận chuột trái qua Input System mới
        if (attackAction == null && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            TryAttack();
        }
    }

    private void OnAttackPerformed(InputAction.CallbackContext context)
    {
        TryAttack();
    }

    /// <summary>
    /// Kiểm tra điều kiện và kích hoạt đòn chém kiếm của Player.
    /// </summary>
    public bool TryAttack()
    {
        // Không cho phép tấn công nếu đang chém dở, đã chết, hoặc đang lăn né
        if (isAttacking) return false;
        if (playerHealth != null && playerHealth.IsDead) return false;
        if (playerDodge != null && playerDodge.IsRolling) return false;
        if (Time.time < lastAttackEndTime + attackCooldown) return false;

        // Xoay ngay mặt nhân vật về hướng con trỏ chuột trước khi vung kiếm
        if (playerMovement != null)
        {
            playerMovement.RotateTowardsMouseImmediate();
        }

        attackCoroutine = StartCoroutine(AttackRoutine());
        return true;
    }

    private IEnumerator AttackRoutine()
    {
        isAttacking = true;
        hitWindowOpened = false;
        hitWindowClosed = false;
        hasResolvedHitThisSwing = false;

        if (swordHitbox != null)
        {
            swordHitbox.CloseWindow();
        }

        if (lockMovementDuringAttack && playerMovement != null)
        {
            playerMovement.SetMovementLocked(true);
        }

        if (animator != null && attackTriggerHash != 0)
        {
            animator.ResetTrigger(attackTriggerHash);
            animator.SetTrigger(attackTriggerHash);
        }

        float elapsed = 0f;
        while (elapsed < attackDuration)
        {
            // Nếu giữa chừng nhân vật chết hoặc chuyển sang lăn né thì hủy đòn chém ngay
            if ((playerHealth != null && playerHealth.IsDead) || (playerDodge != null && playerDodge.IsRolling))
            {
                CancelAttack();
                yield break;
            }

            elapsed += Time.deltaTime;

            // Tự động mở/đóng cửa sổ hitbox theo mốc thời gian nếu người dùng chưa gắn Animation Event trên clip
            if (!hitWindowOpened && elapsed >= fallbackHitWindowStart)
            {
                NotifyHitWindowOpen();
            }

            if (hitWindowOpened && !hitWindowClosed && elapsed >= fallbackHitWindowEnd)
            {
                NotifyHitWindowClose();
            }

            yield return null;
        }

        if (!hitWindowClosed)
        {
            NotifyHitWindowClose();
        }

        EndAttack();
    }

    /// <summary>
    /// Mở cửa sổ gây sát thương của lưỡi kiếm (được gọi từ Animation Event hoặc bộ đếm fallback).
    /// </summary>
    public void NotifyHitWindowOpen()
    {
        if (!isAttacking || hitWindowOpened) return;

        hitWindowOpened = true;
        hasResolvedHitThisSwing = false;

        if (swordHitbox != null)
        {
            swordHitbox.OpenWindow(HandleSwordColliderHit);
        }
    }

    /// <summary>
    /// Đóng cửa sổ gây sát thương của lưỡi kiếm (được gọi từ Animation Event hoặc bộ đếm fallback).
    /// </summary>
    public void NotifyHitWindowClose()
    {
        if (!isAttacking || hitWindowClosed) return;

        hitWindowClosed = true;

        if (swordHitbox != null)
        {
            swordHitbox.CloseWindow();
        }
    }

    private void HandleSwordColliderHit(Collider hitCollider)
    {
        if (hitCollider == null || hasResolvedHitThisSwing) return;

        BossHealth targetBoss = hitCollider.GetComponentInParent<BossHealth>();
        if (targetBoss != null)
        {
            ResolveHitOnBoss(targetBoss);
        }
    }

    /// <summary>
    /// Áp dụng sát thương của đòn chém lên Boss mục tiêu (chỉ tính tối đa 1 lần trong mỗi nhát vung kiếm).
    /// </summary>
    public void ResolveHitOnBoss(BossHealth targetBoss)
    {
        if (targetBoss == null || targetBoss.IsDead || hasResolvedHitThisSwing)
        {
            return;
        }

        if (isAttacking)
        {
            hasResolvedHitThisSwing = true;
        }

        targetBoss.TakeDamage(attackDamage);
        OnHitBoss?.Invoke(targetBoss, attackDamage);
    }

    /// <summary>
    /// Hủy đòn tấn công ngay lập tức (khi bị ngắt đòn hoặc tắt script).
    /// </summary>
    public void CancelAttack()
    {
        if (attackCoroutine != null)
        {
            StopCoroutine(attackCoroutine);
            attackCoroutine = null;
        }

        if (swordHitbox != null)
        {
            swordHitbox.CloseWindow();
        }

        if (isAttacking)
        {
            EndAttack();
        }
    }

    private void EndAttack()
    {
        isAttacking = false;
        hitWindowOpened = false;
        hitWindowClosed = false;
        lastAttackEndTime = Time.time;
        attackCoroutine = null;

        if (lockMovementDuringAttack && playerMovement != null)
        {
            bool isRolling = playerDodge != null && playerDodge.IsRolling;
            bool isDead = playerHealth != null && playerHealth.IsDead;
            if (!isRolling && !isDead)
            {
                playerMovement.SetMovementLocked(false);
            }
        }
    }
}
