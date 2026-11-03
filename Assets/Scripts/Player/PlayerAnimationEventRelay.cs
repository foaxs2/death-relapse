using UnityEngine;

/// <summary>
/// Cầu nối nhận Animation Event từ GameObject chứa Animator của Player (Knight)
/// và chuyển tiếp lên PlayerAttackController.
/// </summary>
public class PlayerAnimationEventRelay : MonoBehaviour
{
    private PlayerAttackController attackController;

    private void Awake()
    {
        attackController = GetComponentInParent<PlayerAttackController>();
        if (attackController == null)
        {
            attackController = GetComponent<PlayerAttackController>();
        }
    }

    /// <summary>
    /// Được gọi bởi Animation Event trên clip Slash tại frame kiếm bắt đầu gây sát thương.
    /// </summary>
    public void OnPlayerHitWindowOpen()
    {
        if (attackController == null) return;
        attackController.NotifyHitWindowOpen();
    }

    /// <summary>
    /// Được gọi bởi Animation Event trên clip Slash tại frame kiếm kết thúc pha gây sát thương.
    /// </summary>
    public void OnPlayerHitWindowClose()
    {
        if (attackController == null) return;
        attackController.NotifyHitWindowClose();
    }

    /// <summary>
    /// Alias tương thích nếu người dùng đặt tên Animation Event giống bên Boss (OnHitWindowOpen).
    /// </summary>
    public void OnHitWindowOpen()
    {
        OnPlayerHitWindowOpen();
    }

    /// <summary>
    /// Alias tương thích nếu người dùng đặt tên Animation Event giống bên Boss (OnHitWindowClose).
    /// </summary>
    public void OnHitWindowClose()
    {
        OnPlayerHitWindowClose();
    }
}

