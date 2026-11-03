using System;
using UnityEngine;

/// <summary>
/// Quản lý thanh máu (HP) và trạng thái tử trận của Boss.
/// Class khung (stub) phục vụ quy trình TDD Bước 1:
/// Chỉ định nghĩa cấu trúc dữ liệu, properties, events và chữ ký hàm, chưa cài đặt logic xử lý bên trong.
/// </summary>
public class BossHealth : MonoBehaviour
{
    [Header("Health Settings")]
    [Tooltip("Máu tối đa của Boss (mặc định: 3000 HP)")]
    [Min(1f)]
    [SerializeField] private float maxHealth = 3000f;

    [Tooltip("Máu hiện tại của Boss (chỉ đọc lúc runtime)")]
    [SerializeField] private float currentHealth;

    // Trạng thái sống/chết của Boss
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
    /// Tỉ lệ máu từ 0.0 đến 1.0 (phục vụ thanh máu UI của Boss).
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
    /// Sự kiện phát ra khi Boss nhận sát thương thực tế. Tham số: (sát thương, máu còn lại).
    /// </summary>
    public event Action<float, float> OnDamaged;

    /// <summary>
    /// Sự kiện phát ra khi máu Boss về 0.
    /// </summary>
    public event Action OnDied;

    private void Awake()
    {
        // Tạm để trống thân hàm theo TDD Red (chưa gán currentHealth = maxHealth)
    }

    /// <summary>
    /// Trừ máu của Boss và kích hoạt các sự kiện OnDamaged, OnHealthChanged, OnDied.
    /// Tạm để trống thân hàm để bài test báo đỏ (Red) chuẩn TDD.
    /// </summary>
    public void TakeDamage(float damageAmount)
    {
        // TODO: Sẽ triển khai logic trừ máu và kích hoạt sự kiện ở Bước 2 TDD
    }

    /// <summary>
    /// Hồi đầy máu và đặt lại trạng thái sống của Boss (phục vụ test hoặc reset arena).
    /// </summary>
    [ContextMenu("Reset Full Health")]
    public void ResetFullHealth()
    {
        // TODO: Sẽ triển khai logic reset máu ở Bước 2 TDD
    }
}

