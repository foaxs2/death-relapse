using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Quản lý chu kỳ tấn công bằng kiếm của nhân vật chính (Player).
/// Class khung (stub) phục vụ quy trình TDD Bước 1:
/// Chỉ định nghĩa cấu trúc dữ liệu, references và chữ ký hàm, chưa cài đặt logic xử lý bên trong.
/// </summary>
public class PlayerAttackController : MonoBehaviour
{
    [Header("Attack Configuration")]
    [Tooltip("Sát thương mỗi nhát chém cơ bản (mặc định: 150 HP)")]
    [Min(0f)]
    [SerializeField] private float attackDamage = 150f;

    [Header("References")]
    [Tooltip("Hitbox kiếm trên tay phải của hiệp sĩ")]
    [SerializeField] private SwordHitbox swordHitbox;

    [Tooltip("Animator của nhân vật")]
    [SerializeField] private Animator animator;

    [Tooltip("Input Action kích hoạt tấn công (Chuột trái / Mouse Left Button)")]
    [SerializeField] private InputActionReference attackAction;

    // Trạng thái đòn đánh
    private bool isAttacking = false;

    public float AttackDamage => attackDamage;
    public bool IsAttacking => isAttacking;

    /// <summary>
    /// Thử kích hoạt đòn đánh chém kiếm.
    /// Tạm thời trả về false theo quy trình TDD Red.
    /// </summary>
    public bool TryAttack()
    {
        // TODO: Sẽ kiểm tra trạng thái và kích hoạt animation chém ở Bước 2 TDD
        return false;
    }

    /// <summary>
    /// Mở cửa sổ gây sát thương của lưỡi kiếm (gọi qua Animation Event).
    /// </summary>
    public void NotifyHitWindowOpen()
    {
        // TODO: Sẽ mở SwordHitbox ở Bước 2 TDD
    }

    /// <summary>
    /// Đóng cửa sổ gây sát thương của lưỡi kiếm (gọi qua Animation Event).
    /// </summary>
    public void NotifyHitWindowClose()
    {
        // TODO: Sẽ đóng SwordHitbox ở Bước 2 TDD
    }

    /// <summary>
    /// Áp dụng sát thương của đòn chém lên Boss mục tiêu.
    /// Tạm để trống thân hàm để bài test báo đỏ (Red) chuẩn TDD.
    /// </summary>
    public void ResolveHitOnBoss(BossHealth targetBoss)
    {
        // TODO: Sẽ gọi targetBoss.TakeDamage(attackDamage) ở Bước 2 TDD
    }
}

