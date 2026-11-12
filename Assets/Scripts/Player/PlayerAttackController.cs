using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Quản lý chu kỳ tấn công bằng kiếm của nhân vật chính (Player).
/// Xoay mặt nhân vật về hướng con trỏ chuột khi ra đòn, kích hoạt animation chém theo combo (Slash2 rồi Slash),
/// điều khiển mở/đóng SwordHitbox, phóng kiếm khí và trừ máu Boss.
/// </summary>
public class PlayerAttackController : MonoBehaviour
{
    /// <summary>
    /// Thông số riêng của từng nhát trong combo, để chỉnh tốc độ/độ rộng/tầm xa từng nhát độc lập.
    /// </summary>
    [Serializable]
    public class SlashStep
    {
        [Header("Animation")]
        [Tooltip("Tên Trigger trong Animator của nhát này")]
        public string triggerName = "Attack";

        [Tooltip("Tốc độ phát animation (1 = gốc, >1 nhanh hơn, <1 chậm hơn). Mọi mốc thời gian bên dưới tự chia theo số này")]
        [Min(0.1f)] public float animationSpeed = 1f;

        [Tooltip("Tổng thời lượng nhát chém ở tốc độ 1 (giây). Phải >= độ dài clip để animation đi hết")]
        [Min(0.1f)] public float attackDuration = 0.6f;

        [Tooltip("Thời gian nghỉ sau nhát này trước khi chém được nhát kế tiếp (giây, ở tốc độ 1)")]
        [Min(0f)] public float restAfter = 0.15f;

        [Header("Hitbox kiếm (fallback nếu chưa gắn Animation Event, ở tốc độ 1)")]
        [Min(0f)] public float hitWindowStart = 0.15f;
        [Min(0.05f)] public float hitWindowEnd = 0.45f;

        [Header("Kiếm khí")]
        public bool spawnWave = true;

        [Tooltip("Hình dạng, tầm xa, độ rộng, sát thương của vệt chém trên mặt đất")]
        public SwordSlashTrail.Settings wave = new SwordSlashTrail.Settings();
    }

    [Header("Combo")]
    [Tooltip("Thứ tự các nhát. Nhát đầu = Slash2, nhát sau = Slash, rồi lặp lại")]
    [SerializeField]
    private SlashStep[] steps = new SlashStep[]
    {
        new SlashStep { triggerName = "Attack2" },
        new SlashStep { triggerName = "Attack" }
    };

    [Tooltip("Sau khi nhát chém kết thúc, nếu người chơi không bấm tiếp trong số giây này thì lần bấm sau quay lại nhát đầu (Slash2)")]
    [Min(0f)]
    [SerializeField] private float comboResetTime = 1.8f;

    [Header("Attack Configuration")]
    [Tooltip("Sát thương mỗi nhát chém bằng lưỡi kiếm")]
    [Min(0f)]
    [SerializeField] private float attackDamage = 75f;

    [Tooltip("Khóa di chuyển WASD trong lúc đang vung kiếm để đòn chém dứt khoát")]
    [SerializeField] private bool lockMovementDuringAttack = true;

    [Header("Slash Trail (vệt kiếm khí bám theo lưỡi kiếm)")]
    [Tooltip("Component SwordSlashTrail (đã gán BladeBase/BladeTip và Material)")]
    [SerializeField] private SwordSlashTrail slashTrail;

    [Header("References")]
    [Tooltip("Hitbox kiếm gắn trên vũ khí tay phải của hiệp sĩ")]
    [SerializeField] private SwordHitbox swordHitbox;

    [Tooltip("Animator của nhân vật")]
    [SerializeField] private Animator animator;

    [Tooltip("Trail bám lưỡi kiếm (tùy chọn), chỉ bật trong lúc hitbox mở")]
    [SerializeField] private TrailRenderer bladeTrail;

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
    private float lastAttackStartTime = -999f;
    private float lastRestTime = 0f;
    private int nextStepIndex = 0;
    private SlashStep currentStep;
    private int[] stepTriggerHashes;
    private Coroutine attackCoroutine;

    // Pool kiếm khí đã bỏ: vệt chém giờ là mesh bám theo kiếm (SwordSlashTrail)

    public float AttackDamage
    {
        get => attackDamage;
        set => attackDamage = Mathf.Max(0f, value);
    }

    public bool IsAttacking => isAttacking;

    /// <summary>
    /// Sự kiện phát ra khi Player chém trúng Boss thành công (bằng kiếm hoặc kiếm khí).
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

        // Băm tên Trigger một lần, tránh so sánh chuỗi mỗi lần chém
        stepTriggerHashes = new int[steps.Length];
        for (int i = 0; i < steps.Length; i++)
        {
            stepTriggerHashes[i] = Animator.StringToHash(steps[i].triggerName);
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
        if (steps == null || steps.Length == 0) return false;
        if (playerHealth != null && playerHealth.IsDead) return false;
        if (playerDodge != null && playerDodge.IsRolling) return false;
        if (Time.time < lastAttackEndTime + lastRestTime) return false;

        // Cửa sổ combo tính từ lúc bấm chuột của nhát trước: quá hạn thì quay lại nhát đầu
        if (Time.time > lastAttackStartTime + comboResetTime)
        {
            nextStepIndex = 0;
        }

        lastAttackStartTime = Time.time;

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
        int stepIndex = nextStepIndex;
        currentStep = steps[stepIndex];
        nextStepIndex = (stepIndex + 1) % steps.Length;

        // Chia mọi mốc thời gian cho tốc độ để animation nhanh/chậm vẫn khớp hitbox
        float speed = Mathf.Max(0.1f, currentStep.animationSpeed);
        float duration = currentStep.attackDuration / speed;
        float hitStart = currentStep.hitWindowStart / speed;
        float hitEnd = currentStep.hitWindowEnd / speed;

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

        if (animator != null)
        {
            animator.speed = speed;
            for (int i = 0; i < stepTriggerHashes.Length; i++)
            {
                animator.ResetTrigger(stepTriggerHashes[i]);
            }
            animator.SetTrigger(stepTriggerHashes[stepIndex]);
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            // Nếu giữa chừng nhân vật chết hoặc chuyển sang lăn né thì hủy đòn chém ngay
            if ((playerHealth != null && playerHealth.IsDead) || (playerDodge != null && playerDodge.IsRolling))
            {
                CancelAttack();
                yield break;
            }

            elapsed += Time.deltaTime;

            // Tự động mở/đóng cửa sổ hitbox theo mốc thời gian nếu người dùng chưa gắn Animation Event trên clip
            if (!hitWindowOpened && elapsed >= hitStart)
            {
                NotifyHitWindowOpen();
            }

            if (hitWindowOpened && !hitWindowClosed && elapsed >= hitEnd)
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
    /// Mở cửa sổ gây sát thương của lưỡi kiếm và phóng kiếm khí (được gọi từ Animation Event hoặc bộ đếm fallback).
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

        if (bladeTrail != null)
        {
            bladeTrail.emitting = true;
        }

        if (slashTrail != null && currentStep != null && currentStep.spawnWave)
        {
            slashTrail.BeginRecording(currentStep.wave, HandleWaveHitBoss);
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

        if (bladeTrail != null)
        {
            bladeTrail.emitting = false;
        }

        if (slashTrail != null)
        {
            slashTrail.EndRecording();
        }
    }

    private void HandleWaveHitBoss(BossHealth boss, float damage)
    {
        if (boss == null || boss.IsDead) return;

        boss.TakeDamage(damage);
        OnHitBoss?.Invoke(boss, damage);
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

        if (bladeTrail != null)
        {
            bladeTrail.emitting = false;
        }

        if (slashTrail != null)
        {
            slashTrail.EndRecording();
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

        // Rest được chia cho tốc độ để nhịp combo nhanh/chậm đồng bộ với animation
        float speed = currentStep != null ? Mathf.Max(0.1f, currentStep.animationSpeed) : 1f;
        lastRestTime = currentStep != null ? currentStep.restAfter / speed : 0f;

        // Trả tốc độ Animator về 1 để các animation khác (Idle, Run, Roll) không bị ảnh hưởng
        if (animator != null)
        {
            animator.speed = 1f;
        }

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
