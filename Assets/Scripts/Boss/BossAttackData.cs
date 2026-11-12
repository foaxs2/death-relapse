using UnityEngine;

/// <summary>Hình dạng vùng cảnh báo / hitbox của đòn đánh.</summary>
public enum TelegraphShapeType
{
    SectorArc,
    BoxLine
}

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

    [Tooltip("Tên State tương ứng trong Animator Controller của Boss (ví dụ: Red Attack, Strong Red Attack)")]
    [SerializeField] private string attackStateName = "Red Attack";

    [Header("Combat & Damage")]
    [Tooltip("Sát thương gây ra khi đòn đánh trúng nhân vật (mặc định: 75 HP)")]
    [Min(0f)]
    [SerializeField] private float damage = 75f;

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

    [Tooltip("Hình dạng vùng cảnh báo: SectorArc (quạt, mặc định) hoặc BoxLine (chữ nhật cho đòn bổ dọc)")]
    [SerializeField] private TelegraphShapeType shapeType = TelegraphShapeType.SectorArc;

    [Tooltip("Chiều rộng vùng chữ nhật (mét), chỉ dùng khi shapeType == BoxLine. Chiều dài dùng chung biến 'radius'.")]
    [Min(0.1f)]
    [SerializeField] private float boxWidth = 1.6f;

    [Tooltip("Dời tâm vùng cảnh báo/hitbox ra phía trước Boss (mét). 0 = tâm tại Boss. Dùng cho đòn bắt đầu từ điểm vũ khí chạm đất.")]
    [SerializeField] private float zoneForwardOffset = 0f;

    [Header("Ground Shockwave (đòn xung kích nứt đất)")]
    [Tooltip("Bật: trong cửa sổ trúng (OnHitWindowOpen -> OnHitWindowClose), Player đứng trong vùng sẽ bị trúng đòn (tối đa 1 lần mỗi đòn). Vùng dùng chung hình học với cảnh báo.")]
    [SerializeField] private bool groundShockwave = false;

    [Header("Animation Synchronization")]
    [Tooltip("Không còn dùng (giữ lại để tương thích asset)")]
    [SerializeField] private float animationSecondsToImpact = 1.0f;

    // Public getters để truy xuất an toàn dữ liệu, tránh bị code khác vô tình chỉnh sửa lúc runtime
    public string AttackName => attackName;
    public string AnimatorTrigger => animatorTrigger;
    public string AttackStateName => attackStateName;
    public float Damage => damage;
    public float TelegraphDuration => telegraphDuration;
    public float Radius => radius;
    public float ArcAngleDegrees => arcAngleDegrees;
    public float FacingOffsetDegrees => facingOffsetDegrees;
    public float AnimationSecondsToImpact => animationSecondsToImpact;
    public TelegraphShapeType ShapeType => shapeType;
    public float ZoneForwardOffset => zoneForwardOffset;
    public bool GroundShockwave => groundShockwave;
    public float BoxWidth => boxWidth;
}
