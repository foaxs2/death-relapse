using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Quản lý hiển thị thanh máu của nhân vật trên Canvas UI (dạng thanh đỏ nguyên bản không cần text).
/// Tự động cập nhật khi nhận sự kiện OnHealthChanged từ PlayerHealth.
/// Mọi thông số (màu sắc, tốc độ tụt mượt) đều tinh chỉnh được trong Inspector.
/// </summary>
public class PlayerHealthBarUI : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Tham chiếu tới PlayerHealth của nhân vật (nếu để trống, script sẽ tự động tìm trong Scene)")]
    [SerializeField] private PlayerHealth playerHealth;

    [Tooltip("Image hiển thị phần điền máu màu đỏ (Yêu cầu Image Type = Filled)")]
    [SerializeField] private Image healthFillImage;

    [Header("Animation Settings")]
    [Tooltip("Bật hiệu ứng tụt máu mượt mà thay vì tụt tức thì")]
    [SerializeField] private bool smoothTransition = true;

    [Tooltip("Tốc độ trượt mượt về giá trị máu mới")]
    [Min(0.1f)]
    [SerializeField] private float smoothSpeed = 10f;

    // Tỉ lệ máu mục tiêu (từ 0.0 đến 1.0)
    private float targetFillAmount = 1f;

    private void Awake()
    {
        // Tự động tìm PlayerHealth nếu chưa được kéo thả trong Inspector
        if (playerHealth == null)
        {
            playerHealth = FindFirstObjectByType<PlayerHealth>();
        }
    }

    private void OnEnable()
    {
        if (playerHealth == null)
        {
            playerHealth = FindFirstObjectByType<PlayerHealth>();
        }

        if (playerHealth != null)
        {
            playerHealth.OnHealthChanged += HandleHealthChanged;

            // Cập nhật ngay giá trị máu hiện tại khi thanh UI được bật
            HandleHealthChanged(playerHealth.CurrentHealth, playerHealth.MaxHealth);
        }
    }

    private void OnDisable()
    {
        if (playerHealth != null)
        {
            playerHealth.OnHealthChanged -= HandleHealthChanged;
        }
    }

    private void Update()
    {
        if (healthFillImage == null) return;

        if (smoothTransition)
        {
            // Trượt mượt fillAmount về targetFillAmount bằng Lerp
            if (Mathf.Abs(healthFillImage.fillAmount - targetFillAmount) > 0.001f)
            {
                healthFillImage.fillAmount = Mathf.Lerp(healthFillImage.fillAmount, targetFillAmount, smoothSpeed * Time.deltaTime);
            }
            else
            {
                healthFillImage.fillAmount = targetFillAmount;
            }
        }
        else
        {
            healthFillImage.fillAmount = targetFillAmount;
        }
    }

    /// <summary>
    /// Xử lý khi nhận sự kiện OnHealthChanged từ PlayerHealth.
    /// </summary>
    private void HandleHealthChanged(float currentHealth, float maxHealth)
    {
        targetFillAmount = maxHealth > 0f ? Mathf.Clamp01(currentHealth / maxHealth) : 0f;

        if (!smoothTransition && healthFillImage != null)
        {
            healthFillImage.fillAmount = targetFillAmount;
        }
    }
}

