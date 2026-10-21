using UnityEngine;

/// <summary>
/// Chứa toàn bộ dữ liệu cấu hình hình học và thời gian của một đòn tấn công của Boss.
/// Phục vụ thí nghiệm A/B: Vùng báo đòn (telegraph) và hitbox gây sát thương 
/// PHẢI dùng chung một nguồn dữ liệu hình học này để đảm bảo tính công bằng.
/// </summary>
[CreateAssetMenu(fileName = "NewBossAttackData", menuName = "Boss/Attack Data")]
public class BossAttackData : ScriptableObject
{
    [Header("General Info")]
    [Tooltip("Tên định danh đòn đánh (ví dụ: RedSlash_0.6s)")]
    [SerializeField] private string attackName = "Horizontal Slash";

    [Tooltip("Tên Trigger trong Animator của Boss để kích hoạt animation đánh")]
    [SerializeField] private string animatorTrigger = "Slash";

    [Header("Telegraph & Geometry (Dùng chung cho hiển thị và hitbox)")]
    [Tooltip("Thời lượng báo đòn trước khi tính sát thương (giây). Thông số chính cần thay đổi trong thí nghiệm A/B.")]
    [SerializeField] private float telegraphDuration = 1.0f;

    [Tooltip("Bán kính vùng quét hình quạt (mét)")]
    [SerializeField] private float radius = 3.1f;

    [Tooltip("Góc mở của cung quạt (độ, từ 1 đến 360)")]
    [Range(1f, 360f)]
    [SerializeField] private float arcAngleDegrees = 150f;

    [Tooltip("Góc lệch tâm cung so với hướng mặt Boss (+: xoay theo chiều kim đồng hồ, -: ngược chiều kim đồng hồ)")]
    [SerializeField] private float facingOffsetDegrees = -10f;

    [Header("Animation Synchronization")]
    [Tooltip("Không còn dùng (giữ lại để tương thích asset)")]
    [SerializeField] private float animationSecondsToImpact = 1.0f;

    // Public getters để truy xuất an toàn dữ liệu, tránh bị code khác vô tình chỉnh sửa lúc runtime
    public string AttackName => attackName;
    public string AnimatorTrigger => animatorTrigger;
    public float TelegraphDuration => telegraphDuration;
    public float Radius => radius;
    public float ArcAngleDegrees => arcAngleDegrees;
    public float FacingOffsetDegrees => facingOffsetDegrees;
    public float AnimationSecondsToImpact => animationSecondsToImpact;
}
