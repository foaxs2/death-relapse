using UnityEngine;

/// <summary>
/// Quản lý phản hồi âm thanh chiến đấu (SFX) với 0 GC Alloc trong gameplay bằng AudioSource.PlayOneShot.
/// Lắng nghe trực tiếp sự kiện từ Player và Boss để đảm bảo cả Build A (không Telegraph hình ảnh)
/// lẫn Build B (có Telegraph hình ảnh) đều nhận phản hồi âm thanh công bằng.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class CombatAudioController : MonoBehaviour
{
    [Header("Combat References (Tự tìm trong Scene nếu để trống)")]
    [SerializeField] private PlayerAttackController playerAttackController;
    [SerializeField] private PlayerDodge playerDodge;
    [SerializeField] private PlayerHealth playerHealth;
    [SerializeField] private BossAttackController bossAttackController;

    [Header("SFX Clips (Assets/Audio/SFX/)")]
    [SerializeField] private AudioClip swordSlashClip;
    [SerializeField] private AudioClip dodgeRollClip;
    [SerializeField] private AudioClip playerHurtClip;
    [SerializeField] private AudioClip bossWindupClip;
    [SerializeField] private AudioClip bossImpactClip;

    [Header("Volume Settings")]
    [Range(0f, 1f)] [SerializeField] private float masterVolume = 0.85f;
    [Range(0f, 1f)] [SerializeField] private float swordSlashVolume = 0.85f;
    [Range(0f, 1f)] [SerializeField] private float dodgeRollVolume = 0.75f;
    [Range(0f, 1f)] [SerializeField] private float playerHurtVolume = 0.95f;
    [Range(0f, 1f)] [SerializeField] private float bossWindupVolume = 0.90f;
    [Range(0f, 1f)] [SerializeField] private float bossImpactVolume = 1.0f;

    private AudioSource audioSource;

    private void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.loop = false;
        // 2D spatialBlend = 0 để âm thanh báo đòn của Boss luôn rõ và đồng đều cho cả Group A và Group B
        audioSource.spatialBlend = 0f;

        if (playerAttackController == null) playerAttackController = FindFirstObjectByType<PlayerAttackController>();
        if (playerDodge == null) playerDodge = FindFirstObjectByType<PlayerDodge>();
        if (playerHealth == null) playerHealth = FindFirstObjectByType<PlayerHealth>();
        if (bossAttackController == null) bossAttackController = FindFirstObjectByType<BossAttackController>();
    }

    private void OnEnable()
    {
        if (playerAttackController != null)
        {
            playerAttackController.OnAttackSwingStarted -= HandlePlayerSwordSlash;
            playerAttackController.OnAttackSwingStarted += HandlePlayerSwordSlash;
        }

        if (playerDodge != null)
        {
            playerDodge.OnRollStarted -= HandlePlayerDodgeRoll;
            playerDodge.OnRollStarted += HandlePlayerDodgeRoll;
        }

        if (playerHealth != null)
        {
            playerHealth.OnDamaged -= HandlePlayerHurt;
            playerHealth.OnDamaged += HandlePlayerHurt;
        }

        if (bossAttackController != null)
        {
            bossAttackController.OnAttackWindupStarted -= HandleBossWindup;
            bossAttackController.OnAttackWindupStarted += HandleBossWindup;
            bossAttackController.OnAttackImpact -= HandleBossImpact;
            bossAttackController.OnAttackImpact += HandleBossImpact;
        }
    }

    private void OnDisable()
    {
        if (playerAttackController != null)
        {
            playerAttackController.OnAttackSwingStarted -= HandlePlayerSwordSlash;
        }

        if (playerDodge != null)
        {
            playerDodge.OnRollStarted -= HandlePlayerDodgeRoll;
        }

        if (playerHealth != null)
        {
            playerHealth.OnDamaged -= HandlePlayerHurt;
        }

        if (bossAttackController != null)
        {
            bossAttackController.OnAttackWindupStarted -= HandleBossWindup;
            bossAttackController.OnAttackImpact -= HandleBossImpact;
        }
    }

    private void HandlePlayerSwordSlash(int stepIndex)
    {
        PlayClip(swordSlashClip, swordSlashVolume);
    }

    private void HandlePlayerDodgeRoll(Vector3 rollDirection)
    {
        PlayClip(dodgeRollClip, dodgeRollVolume);
    }

    private void HandlePlayerHurt(float damageAmount, float remainingHealth)
    {
        PlayClip(playerHurtClip, playerHurtVolume);
    }

    private void HandleBossWindup(BossAttackData attackData)
    {
        PlayClip(bossWindupClip, bossWindupVolume);
    }

    private void HandleBossImpact(BossAttackData attackData)
    {
        PlayClip(bossImpactClip, bossImpactVolume);
    }

    private void PlayClip(AudioClip clip, float relativeVolume)
    {
        if (clip == null || audioSource == null) return;
        audioSource.PlayOneShot(clip, Mathf.Clamp01(masterVolume * relativeVolume));
    }
}
