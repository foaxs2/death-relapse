using System;
using UnityEngine;

/// <summary>
/// Quản lý thanh máu (HP), nhận sát thương và trạng thái tử trận của Boss.
/// Mọi thông số đều được cấu hình qua Inspector để dễ dàng cân bằng độ khó.
/// </summary>
public class BossHealth : MonoBehaviour
{
    [Header("Health Settings")]
    [Tooltip("Máu tối đa của Boss (mặc định: 3000 HP)")]
    [Min(1f)]
    [SerializeField] private float maxHealth = 3000f;

    [Tooltip("Máu hiện tại của Boss (chỉ xem trong lúc chạy)")]
    [SerializeField] private float currentHealth;

    [Header("Animation & References")]
    [Tooltip("Tên Trigger trong Animator của Boss để phát animation chết (nếu có)")]
    [SerializeField] private string deathTriggerName = "Death";

    [Tooltip("Tham chiếu BossAttackController để vô hiệu hóa hành vi tấn công khi Boss chết")]
    [SerializeField] private BossAttackController bossAttackController;

    private Animator animator;
    private int deathTriggerHash;
    private bool isDead = false;

    /// <summary>
    /// Máu hiện tại của Boss.
    /// </summary>
    public float CurrentHealth => currentHealth;

    /// <summary>
    /// Máu tối đa của Boss.
    /// </summary>
    public float MaxHealth => maxHealth;

    /// <summary>
    /// Tỉ lệ máu hiện tại từ 0.0 đến 1.0 (dùng cho thanh máu UI của Boss).
    /// </summary>
    public float HealthNormalized => maxHealth > 0f ? Mathf.Clamp01(currentHealth / maxHealth) : 0f;

    /// <summary>
    /// Cho biết Boss đã bị hạ gục hay chưa.
    /// </summary>
    public bool IsDead => isDead;

    /// <summary>
    /// Sự kiện phát ra mỗi khi máu Boss thay đổi. Tham số: (currentHealth, maxHealth).
    /// </summary>
    public event Action<float, float> OnHealthChanged;

    /// <summary>
    /// Sự kiện phát ra khi Boss nhận sát thương thực tế. Tham số: (sát thương nhận vào, máu còn lại).
    /// </summary>
    public event Action<float, float> OnDamaged;

    /// <summary>
    /// Sự kiện phát ra khi máu Boss về 0.
    /// </summary>
    public event Action OnDied;

    private void Awake()
    {
        animator = GetComponentInChildren<Animator>();

        if (bossAttackController == null)
        {
            bossAttackController = GetComponent<BossAttackController>();
            if (bossAttackController == null)
            {
                bossAttackController = GetComponentInChildren<BossAttackController>();
            }
        }

        if (!string.IsNullOrEmpty(deathTriggerName))
        {
            deathTriggerHash = Animator.StringToHash(deathTriggerName);
        }

        isDead = false;
        currentHealth = maxHealth;
    }

    private void Start()
    {
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
    }

    /// <summary>
    /// Trừ máu của Boss và kích hoạt các sự kiện OnDamaged, OnHealthChanged, OnDied.
    /// </summary>
    public void TakeDamage(float damageAmount)
    {
        // Bảo vệ an toàn trước đầu vào âm, bằng 0 hoặc NaN/Infinity để không bị lỗi cộng ngược máu
        if (isDead || damageAmount <= 0f || float.IsNaN(damageAmount) || float.IsInfinity(damageAmount))
        {
            return;
        }

        currentHealth = Mathf.Max(0f, currentHealth - damageAmount);
        OnHealthChanged?.Invoke(currentHealth, maxHealth);
        OnDamaged?.Invoke(damageAmount, currentHealth);

        Debug.Log($"[BossHealth] <color=yellow>BOSS TRÚNG ĐÒN!</color> -{damageAmount:F0} HP | Máu Boss còn: {currentHealth:F0}/{maxHealth:F0}");

        if (currentHealth <= 0f)
        {
            Die();
        }
    }

    private void Die()
    {
        if (isDead) return;
        isDead = true;

        if (bossAttackController != null)
        {
            bossAttackController.enabled = false;
        }

        if (animator != null && deathTriggerHash != 0 && HasParameter(animator, deathTriggerHash))
        {
            animator.ResetTrigger(deathTriggerHash);
            animator.SetTrigger(deathTriggerHash);
        }

        Debug.Log("[BossHealth] <color=green>BOSS ĐÃ BỊ HẠ GỤC (0 HP)!</color>");
        OnDied?.Invoke();
    }

    /// <summary>
    /// Kiểm tra xem Animator có chứa parameter tương ứng không để tránh cảnh báo vàng trong Console.
    /// </summary>
    private static bool HasParameter(Animator targetAnimator, int paramHash)
    {
        if (targetAnimator == null || targetAnimator.runtimeAnimatorController == null) return false;
        foreach (AnimatorControllerParameter param in targetAnimator.parameters)
        {
            if (param.nameHash == paramHash) return true;
        }
        return false;
    }

    /// <summary>
    /// Hồi đầy máu và đặt lại trạng thái sống của Boss (phục vụ test hoặc reset arena).
    /// </summary>
    [ContextMenu("Reset Full Health")]
    public void ResetFullHealth()
    {
        isDead = false;
        currentHealth = maxHealth;

        if (bossAttackController != null)
        {
            bossAttackController.enabled = true;
        }

        OnHealthChanged?.Invoke(currentHealth, maxHealth);
        Debug.Log($"[BossHealth] Đã hồi đầy máu Boss: {currentHealth:F0}/{maxHealth:F0}");
    }
}
