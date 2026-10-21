using UnityEngine;

/// <summary>
/// Cầu nối nhận Animation Event từ GameObject chứa Animator và chuyển tiếp
/// lên BossAttackController ở cấp cha.
/// </summary>
public class BossAnimationEventRelay : MonoBehaviour
{
    private BossAttackController attackController;

    private void Awake()
    {
        attackController = GetComponentInParent<BossAttackController>();
        if (attackController == null)
        {
            Debug.LogError($"[BossAnimationEventRelay] Không tìm thấy BossAttackController ở '{gameObject.name}' hoặc các GameObject cha! Hãy kiểm tra lại cấu trúc Hierarchy của Boss.");
        }
    }

    /// <summary>
    /// Được gọi bởi Animation Event tại frame kiếm bắt đầu gây sát thương.
    /// </summary>
    public void OnHitWindowOpen()
    {
        if (attackController == null) return;
        attackController.NotifyHitWindowOpen();
    }

    /// <summary>
    /// Được gọi bởi Animation Event tại frame kiếm kết thúc pha gây sát thương.
    /// </summary>
    public void OnHitWindowClose()
    {
        if (attackController == null) return;
        attackController.NotifyHitWindowClose();
    }
}
