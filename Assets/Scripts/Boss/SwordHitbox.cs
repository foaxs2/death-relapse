using System;
using UnityEngine;

/// <summary>
/// Quản lý hitbox lưỡi kiếm của Boss theo kiểu Dark Souls.
/// Chỉ kiểm tra va chạm trong cửa sổ trúng (hit window) được mở/đóng bởi Animation Event.
/// Quét trong LateUpdate với nội suy nhiều bước (substeps) để tránh kiếm vung nhanh xuyên qua người chơi.
/// </summary>
public class SwordHitbox : MonoBehaviour
{
    [Header("Blade Geometry")]
    [Tooltip("Điểm gốc lưỡi kiếm (sát chuôi kiếm)")]
    [SerializeField] private Transform bladeBase;

    [Tooltip("Điểm mũi lưỡi kiếm")]
    [SerializeField] private Transform bladeTip;

    [Tooltip("Bán kính độ dày hình trụ bọc lưỡi kiếm (mét)")]
    [SerializeField] private float bladeRadius = 0.25f;

    [Header("Detection Settings")]
    [Tooltip("Layer của Player để lọc va chạm")]
    [SerializeField] private LayerMask playerMask;

    [Tooltip("Số bước nội suy giữa frame trước và frame hiện tại để chống xuyên (tunneling)")]
    [Min(1)]
    [SerializeField] private int substeps = 4;

    private bool isOpen;
    private bool hasTriggeredHit;
    private Action<Collider> onHitCallback;

    private Vector3 prevBasePos;
    private Vector3 prevTipPos;
    private bool hasPrevPose;

    // Buffer cấp sẵn để tránh cấp phát bộ nhớ (GC Alloc) mỗi frame
    private readonly Collider[] hitBuffer = new Collider[8];

    public bool IsOpen => isOpen;

    /// <summary>
    /// Mở cửa sổ kiểm tra va chạm lưỡi kiếm.
    /// </summary>
    public void OpenWindow(Action<Collider> onHit)
    {
        if (bladeBase == null || bladeTip == null)
        {
            Debug.LogError("[SwordHitbox] Chưa gán 'bladeBase' hoặc 'bladeTip' trên " + gameObject.name);
            return;
        }

        onHitCallback = onHit;
        hasTriggeredHit = false;
        isOpen = true;

        // Ghi nhận tư thế ngay lúc mở cửa sổ làm mốc xuất phát cho frame đầu tiên
        prevBasePos = bladeBase.position;
        prevTipPos = bladeTip.position;
        hasPrevPose = true;
    }

    /// <summary>
    /// Đóng cửa sổ kiểm tra va chạm lưỡi kiếm.
    /// </summary>
    public void CloseWindow()
    {
        isOpen = false;
        hasPrevPose = false;
        onHitCallback = null;
    }

    private void OnDisable()
    {
        CloseWindow();
    }

    private void LateUpdate()
    {
        // Kiểm tra trong LateUpdate vì Animator cập nhật vị trí xương trước LateUpdate
        if (!isOpen || hasTriggeredHit) return;
        if (bladeBase == null || bladeTip == null) return;

        Vector3 currentBasePos = bladeBase.position;
        Vector3 currentTipPos = bladeTip.position;

        if (!hasPrevPose)
        {
            prevBasePos = currentBasePos;
            prevTipPos = currentTipPos;
            hasPrevPose = true;
        }

        int steps = Mathf.Max(1, substeps);

        // Nội suy từng bước nhỏ giữa tư thế frame trước và frame này để không bỏ sót khi kiếm quét nhanh
        for (int i = 1; i <= steps; i++)
        {
            float t = (float)i / steps;
            Vector3 stepBase = Vector3.Lerp(prevBasePos, currentBasePos, t);
            Vector3 stepTip = Vector3.Lerp(prevTipPos, currentTipPos, t);

            int hitCount = Physics.OverlapCapsuleNonAlloc(
                stepBase,
                stepTip,
                bladeRadius,
                hitBuffer,
                playerMask,
                QueryTriggerInteraction.Ignore
            );

            if (hitCount > 0)
            {
                Collider firstCollider = hitBuffer[0];
                hasTriggeredHit = true; // Ngừng kiểm tra trong phần còn lại của cửa sổ này

                Action<Collider> callback = onHitCallback;
                callback?.Invoke(firstCollider);
                break;
            }
        }

        prevBasePos = currentBasePos;
        prevTipPos = currentTipPos;
    }

    private void OnDrawGizmos()
    {
        if (bladeBase == null || bladeTip == null) return;

        // Đỏ khi cửa sổ mở (đang gây sát thương), xám khi đóng
        Gizmos.color = isOpen ? new Color(1f, 0.15f, 0.15f, 0.9f) : new Color(0.5f, 0.5f, 0.5f, 0.7f);

        Vector3 basePos = bladeBase.position;
        Vector3 tipPos = bladeTip.position;

        Gizmos.DrawLine(basePos, tipPos);
        Gizmos.DrawWireSphere(basePos, bladeRadius);
        Gizmos.DrawWireSphere(tipPos, bladeRadius);
    }
}
