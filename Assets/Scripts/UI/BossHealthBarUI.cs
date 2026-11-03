using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Quản lý hiển thị thanh máu của Boss trên Canvas UI (dạng Filled Image tương tự PlayerHealthBarUI).
/// Tự động lắng nghe sự kiện OnHealthChanged từ BossHealth để cập nhật mượt mà.
/// </summary>
public class BossHealthBarUI : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Tham chiếu tới BossHealth của Boss (nếu để trống sẽ tự tìm trong Scene)")]
    [SerializeField] private BossHealth bossHealth;

    [Tooltip("Image hiển thị phần điền máu của Boss (Yêu cầu Image Type = Filled)")]
    [SerializeField] private Image healthFillImage;

    [Tooltip("Khung chứa toàn bộ thanh máu Boss để ẩn/hiện khi cần (tùy chọn)")]
    [SerializeField] private GameObject barContainer;

    [Header("Animation Settings")]
    [Tooltip("Bật hiệu ứng tụt máu mượt mà thay vì tụt tức thì")]
    [SerializeField] private bool smoothTransition = true;

    [Tooltip("Tốc độ trượt mượt về giá trị máu mới")]
    [Min(0.1f)]
    [SerializeField] private float smoothSpeed = 10f;

    [Tooltip("Tự động ẩn thanh máu Boss khi Boss đã bị hạ gục")]
    [SerializeField] private bool hideWhenBossDead = false;

    private float targetFillAmount = 1f;

    private void Awake()
    {
        if (bossHealth == null)
        {
            bossHealth = FindFirstObjectByType<BossHealth>();
        }
    }

    private void OnEnable()
    {
        if (bossHealth == null)
        {
            bossHealth = FindFirstObjectByType<BossHealth>();
        }

        if (bossHealth != null)
        {
            bossHealth.OnHealthChanged -= HandleHealthChanged;
            bossHealth.OnHealthChanged += HandleHealthChanged;

            bossHealth.OnDied -= HandleBossDied;
            bossHealth.OnDied += HandleBossDied;

            HandleHealthChanged(bossHealth.CurrentHealth, bossHealth.MaxHealth);
        }
    }

    private void OnDisable()
    {
        if (bossHealth != null)
        {
            bossHealth.OnHealthChanged -= HandleHealthChanged;
            bossHealth.OnDied -= HandleBossDied;
        }
    }

    private void Update()
    {
        if (healthFillImage == null) return;

        if (smoothTransition)
        {
            if (Mathf.Abs(healthFillImage.fillAmount - targetFillAmount) > 0.001f)
            {
                healthFillImage.fillAmount = Mathf.Lerp(
                    healthFillImage.fillAmount,
                    targetFillAmount,
                    smoothSpeed * Time.deltaTime
                );
            }
            else
            {
                healthFillImage.fillAmount = targetFillAmount;
            }
        }
    }

    private void HandleHealthChanged(float currentHealth, float maxHealth)
    {
        targetFillAmount = maxHealth > 0f ? Mathf.Clamp01(currentHealth / maxHealth) : 0f;

        if (!smoothTransition && healthFillImage != null)
        {
            healthFillImage.fillAmount = targetFillAmount;
        }
    }

    private void HandleBossDied()
    {
        if (hideWhenBossDead)
        {
            SetVisible(false);
        }
    }

    /// <summary>
    /// Cho phép ẩn hoặc hiện thanh máu Boss khi bắt đầu/kết thúc trận đấu.
    /// </summary>
    public void SetVisible(bool visible)
    {
        if (barContainer != null)
        {
            barContainer.SetActive(visible);
        }
        else
        {
            gameObject.SetActive(visible);
        }
    }
}

