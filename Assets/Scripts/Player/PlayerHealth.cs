using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Quản lý máu (HP), trạng thái nhận sát thương (CharacterImpact), miễn nhiễm sát thương nhờ I-frame (PlayerDodge)
/// và trạng thái chết (Death) của nhân vật.
/// Mọi thông số đều chỉnh được trong Inspector.
/// </summary>
[RequireComponent(typeof(PlayerMovement))]
public class PlayerHealth : MonoBehaviour
{
    [Header("Health Settings")]
    [Tooltip("Máu tối đa của nhân vật (mặc định: 300 HP)")]
    [Min(1f)]
    [SerializeField] private float maxHealth = 300f;

    [Tooltip("Máu hiện tại của nhân vật (chỉ xem trong lúc chạy)")]
    [SerializeField] private float currentHealth;

    [Header("Hit Reaction & Stun")]
    [Tooltip("Thời gian nhân vật bị khựng không thể di chuyển khi trúng đòn (giây)")]
    [Min(0f)]
    [SerializeField] private float hitStunDuration = 0.4f;

    [Tooltip("Tên Trigger trong Animator để phát animation bị trúng đòn (CharacterImpact)")]
    [SerializeField] private string hurtTriggerName = "Hurt";

    [Tooltip("Tên Trigger trong Animator để phát animation chết (Death)")]
    [SerializeField] private string deathTriggerName = "Death";

    [Header("References")]
    [Tooltip("Tham chiếu tới BossAttackController để tự động nhận sự kiện khi lưỡi kiếm chạm trúng Player")]
    [SerializeField] private BossAttackController bossAttackController;

    // Component tham chiếu trên Player
    private PlayerMovement playerMovement;
    private PlayerDodge playerDodge;
    private Animator animator;

    // Cache mã băm Animator Trigger
    private int hurtTriggerHash;
    private int deathTriggerHash;

    // Trạng thái runtime
    private bool isDead = false;
    private Coroutine hitStunCoroutine;

    /// <summary>
    /// Máu hiện tại của nhân vật.
    /// </summary>
    public float CurrentHealth => currentHealth;

    /// <summary>
    /// Máu tối đa của nhân vật.
    /// </summary>
    public float MaxHealth => maxHealth;

    /// <summary>
    /// Tỉ lệ máu hiện tại từ 0 đến 1 (dùng cho thanh máu UI ở Phần 3).
    /// </summary>
    public float HealthNormalized => maxHealth > 0f ? Mathf.Clamp01(currentHealth / maxHealth) : 0f;

    /// <summary>
    /// Cho biết nhân vật đã chết hay chưa.
    /// </summary>
    public bool IsDead => isDead;

    /// <summary>
    /// Sự kiện phát ra mỗi khi máu thay đổi. Tham số: (currentHealth, maxHealth).
    /// </summary>
    public event Action<float, float> OnHealthChanged;

    /// <summary>
    /// Sự kiện phát ra khi nhân vật né đòn thành công nhờ I-frame.
    /// </summary>
    public event Action<BossAttackData> OnAttackDodged;

    /// <summary>
    /// Sự kiện phát ra khi nhân vật nhận sát thương thực tế. Tham số: (sát thương nhận vào, máu còn lại).
    /// </summary>
    public event Action<float, float> OnDamaged;

    /// <summary>
    /// Sự kiện phát ra khi máu nhân vật về 0.
    /// </summary>
    public event Action OnDied;

    private void Awake()
    {
        playerMovement = GetComponent<PlayerMovement>();
        playerDodge = GetComponent<PlayerDodge>();
        animator = GetComponentInChildren<Animator>();

        // Tự động tìm BossAttackController nếu trong Inspector chưa kịp kéo thả
        if (bossAttackController == null)
        {
            bossAttackController = FindFirstObjectByType<BossAttackController>();
        }

        if (!string.IsNullOrEmpty(hurtTriggerName))
        {
            hurtTriggerHash = Animator.StringToHash(hurtTriggerName);
        }

        if (!string.IsNullOrEmpty(deathTriggerName))
        {
            deathTriggerHash = Animator.StringToHash(deathTriggerName);
        }

        // Xóa các Trigger nếu vô tình bị bật sẵn trong cửa sổ Animator Editor
        if (animator != null)
        {
            if (hurtTriggerHash != 0) animator.ResetTrigger(hurtTriggerHash);
            if (deathTriggerHash != 0) animator.ResetTrigger(deathTriggerHash);
        }

        currentHealth = maxHealth;
    }

    private void Start()
    {
        // Đảm bảo nhân vật bắt đầu trận ở trạng thái đứng (idle) nếu Trigger Death vô tình bị tích sẵn trong Editor
        if (!isDead && animator != null)
        {
            if (deathTriggerHash != 0) animator.ResetTrigger(deathTriggerHash);
            if (hurtTriggerHash != 0) animator.ResetTrigger(hurtTriggerHash);
            animator.Play("idle", 0, 0f);
        }

        // Cập nhật trạng thái máu ban đầu cho UI (nếu có đăng ký)
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
    }

    private void OnEnable()
    {
        if (bossAttackController == null)
        {
            bossAttackController = FindFirstObjectByType<BossAttackController>();
        }

        if (bossAttackController != null)
        {
            bossAttackController.PlayerHitByAttack -= HandleBossAttackHit;
            bossAttackController.PlayerHitByAttack += HandleBossAttackHit;
        }
        else
        {
            Debug.LogWarning("[PlayerHealth] Không tìm thấy BossAttackController trong Scene! Hãy chắc chắn có Boss trong Scene.");
        }
    }

    private void OnDisable()
    {
        if (bossAttackController != null)
        {
            bossAttackController.PlayerHitByAttack -= HandleBossAttackHit;
        }

        if (hitStunCoroutine != null)
        {
            StopCoroutine(hitStunCoroutine);
            hitStunCoroutine = null;
        }
    }

    /// <summary>
    /// Xử lý khi lưỡi kiếm của Boss chạm trúng collider của Player trong cửa sổ hitbox.
    /// </summary>
    public void ReceiveBossAttackHit(BossAttackData attackData, bool insideZone)
    {
        HandleBossAttackHit(attackData, insideZone);
    }

    private void HandleBossAttackHit(BossAttackData attackData, bool insideZone)
    {
        if (isDead) return;

        // 1. Kiểm tra nếu nhân vật đang trong trạng thái bất tử (I-frame) của cú lăn né
        if (playerDodge != null && playerDodge.IsInvulnerable)
        {
            string attackName = attackData != null ? attackData.AttackName : "Unknown";
            Debug.Log($"[PlayerHealth] <color=cyan>NÉ ĐÒN THÀNH CÔNG (I-FRAME)!</color> Miễn nhiễm sát thương từ đòn '{attackName}' (Time: {Time.time:F2}s) | Máu: {currentHealth:F0}/{maxHealth:F0}");
            OnAttackDodged?.Invoke(attackData);
            return;
        }

        // 2. Nếu không có I-frame -> Nhận sát thương theo cấu hình của đòn đánh
        float damage = attackData != null ? attackData.Damage : 75f;
        TakeDamage(damage);
    }

    /// <summary>
    /// Trừ máu nhân vật và kích hoạt phản ứng trúng đòn (CharacterImpact) hoặc chết (Death).
    /// </summary>
    public void TakeDamage(float damageAmount)
    {
        if (isDead) return;

        if (damageAmount <= 0f || float.IsNaN(damageAmount) || float.IsInfinity(damageAmount))
        {
            Debug.LogWarning("[PlayerHealth] Sát thương đầu vào không hợp lệ: " + damageAmount);
            return;
        }

        currentHealth = Mathf.Max(0f, currentHealth - damageAmount);
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
        OnDamaged?.Invoke(damageAmount, currentHealth);

        Debug.Log($"[PlayerHealth] <color=orange>TRÚNG ĐÒN!</color> - {damageAmount:F0} HP | Máu còn lại: {currentHealth:F0}/{maxHealth:F0}");

        if (currentHealth <= 0f)
        {
            Die();
        }
        else
        {
            TriggerHitReaction();
        }
    }

    /// <summary>
    /// Kích hoạt animation bị đánh trúng (CharacterImpact) và khóa di chuyển tạm thời.
    /// </summary>
    private void TriggerHitReaction()
    {
        if (animator != null && hurtTriggerHash != 0)
        {
            animator.ResetTrigger(hurtTriggerHash);
            animator.SetTrigger(hurtTriggerHash);
        }

        if (hitStunDuration > 0f)
        {
            if (hitStunCoroutine != null)
            {
                StopCoroutine(hitStunCoroutine);
            }
            hitStunCoroutine = StartCoroutine(HitStunRoutine());
        }
    }

    private IEnumerator HitStunRoutine()
    {
        if (playerMovement != null)
        {
            playerMovement.SetMovementLocked(true);
        }

        yield return new WaitForSeconds(hitStunDuration);

        // Chỉ mở khóa di chuyển nếu nhân vật chưa chết và không đang lăn
        if (!isDead && playerMovement != null && (playerDodge == null || !playerDodge.IsRolling))
        {
            playerMovement.SetMovementLocked(false);
        }

        hitStunCoroutine = null;
    }

    /// <summary>
    /// Xử lý khi máu nhân vật về 0: khóa mọi điều khiển và phát animation Death.
    /// </summary>
    private void Die()
    {
        if (isDead) return;
        isDead = true;

        if (hitStunCoroutine != null)
        {
            StopCoroutine(hitStunCoroutine);
            hitStunCoroutine = null;
        }

        // Khóa di chuyển và tắt khả năng lăn né
        if (playerMovement != null)
        {
            playerMovement.SetMovementLocked(true);
        }

        if (playerDodge != null)
        {
            playerDodge.enabled = false;
        }

        if (animator != null && deathTriggerHash != 0)
        {
            if (hurtTriggerHash != 0)
            {
                animator.ResetTrigger(hurtTriggerHash);
            }
            animator.ResetTrigger(deathTriggerHash);
            animator.SetTrigger(deathTriggerHash);
        }

        Debug.Log("[PlayerHealth] <color=red>NHÂN VẬT ĐÃ GỤC NGÃ (0 HP)!</color>");
        OnDied?.Invoke();
    }

    /// <summary>
    /// Hồi đầy máu và khôi phục trạng thái sống (dùng khi muốn reset nhanh để test).
    /// </summary>
    [ContextMenu("Reset Full Health")]
    public void ResetFullHealth()
    {
        isDead = false;
        currentHealth = maxHealth;

        if (playerDodge != null)
        {
            playerDodge.enabled = true;
        }

        if (playerMovement != null)
        {
            playerMovement.SetMovementLocked(false);
        }

        OnHealthChanged?.Invoke(currentHealth, maxHealth);
        Debug.Log($"[PlayerHealth] Đã hồi đầy máu: {currentHealth:F0}/{maxHealth:F0}");
    }
}

