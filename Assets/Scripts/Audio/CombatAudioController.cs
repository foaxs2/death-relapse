using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Quản lý phản hồi âm thanh chiến đấu và bước chân của Knight với 0 GC Alloc trong gameplay.
/// Sử dụng các tệp .mp3 trong Assets/Audio/SFX/:
/// - Tự động cắt khoảng lặng đầu clip một lần trong Awake() để âm thanh phát tức thì đúng nhịp animation.
/// - Tách riêng các tiếng bước chân đơn lẻ từ FootSteps_Knight.mp3 để phát khớp từng bước chạm đất của Knight.
/// - Boss KHÔNG phát tiếng bước chân; các âm báo gồng/chém của Boss phát đồng đều cho cả Group A và Group B.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class CombatAudioController : MonoBehaviour
{
    [Header("Combat References (Tự tìm trong Scene nếu để trống)")]
    [SerializeField] private PlayerAttackController playerAttackController;
    [SerializeField] private PlayerDodge playerDodge;
    [SerializeField] private PlayerHealth playerHealth;
    [SerializeField] private PlayerAnimationEventRelay playerAnimationRelay;
    [SerializeField] private BossAttackController bossAttackController;
    [SerializeField] private BossHealth bossHealth;

    [Header("Player SFX Clips (.mp3)")]
    [SerializeField] private AudioClip swordSlash1Clip;
    [SerializeField] private AudioClip swordSlash2Clip;
    [SerializeField] private AudioClip dodgeRollClip;
    [SerializeField] private AudioClip playerHurtClip;
    [SerializeField] private AudioClip knightFootstepsClip;

    [Header("Boss SFX Clips (.mp3 - Boss không có tiếng bước chân)")]
    [SerializeField] private AudioClip bossWindupClip;
    [SerializeField] private AudioClip bossSlash1Clip;
    [SerializeField] private AudioClip bossSlash2Clip;

    [Header("Volume Settings")]
    [Range(0f, 1f)] [SerializeField] private float masterVolume = 0.85f;
    [Range(0f, 1f)] [SerializeField] private float swordSlashVolume = 0.85f;
    [Range(0f, 1f)] [SerializeField] private float dodgeRollVolume = 0.78f;
    [Range(0f, 1f)] [SerializeField] private float playerHurtVolume = 0.92f;
    [Range(0f, 1f)] [SerializeField] private float knightFootstepVolume = 0.55f;
    [Range(0f, 1f)] [SerializeField] private float bossWindupVolume = 0.85f;
    [Range(0f, 1f)] [SerializeField] private float bossSlashVolume = 0.95f;

    [Header("Audio Alignment & Footstep Slicing (Chạy 1 lần ở Awake)")]
    [Tooltip("Cắt bỏ khoảng lặng đầu của các clip SFX để âm thanh không bị trễ nhịp khi vung kiếm/gồng đòn")]
    [SerializeField] private bool trimLeadingSilence = true;

    [Tooltip("Thời lượng tối đa giữ lại cho âm gồng đòn Boss (giây) để không kéo dài lê thê")]
    [Min(0.2f)]
    [SerializeField] private float maxBossWindupDuration = 1.6f;

    [Tooltip("Thời lượng mỗi tiếng bước chân đơn lẻ trích xuất từ FootSteps_Knight.mp3 (giây)")]
    [Range(0.12f, 0.35f)]
    [SerializeField] private float singleFootstepDuration = 0.22f;

    [Tooltip("Biên thiên nhẹ cao độ (pitch) cho từng bước chân của Knight để nghe tự nhiên hơn")]
    [Range(0f, 0.12f)]
    [SerializeField] private float footstepPitchJitter = 0.05f;

    private AudioSource sfxSource;
    private AudioSource footstepSource;
    private AudioSource windupSource;

    // Các clip đã cắt khoảng lặng sẵn trong Awake (0 GC Alloc khi chiến đấu)
    private AudioClip preparedSlash1;
    private AudioClip preparedSlash2;
    private AudioClip preparedRoll;
    private AudioClip preparedHurt;
    private AudioClip preparedBossWindup;
    private AudioClip preparedBossSlash1;
    private AudioClip preparedBossSlash2;
    private AudioClip[] preparedFootsteps;
    private readonly List<AudioClip> runtimeGeneratedClips = new List<AudioClip>();
    private int nextFootstepVariantIndex;

    public int FootstepVariantCount => preparedFootsteps != null ? preparedFootsteps.Length : 0;

    private void Awake()
    {
        sfxSource = GetComponent<AudioSource>();
        Configure2DAudioSource(sfxSource);

        footstepSource = gameObject.AddComponent<AudioSource>();
        Configure2DAudioSource(footstepSource);

        windupSource = gameObject.AddComponent<AudioSource>();
        Configure2DAudioSource(windupSource);

        if (playerAttackController == null) playerAttackController = FindFirstObjectByType<PlayerAttackController>();
        if (playerDodge == null) playerDodge = FindFirstObjectByType<PlayerDodge>();
        if (playerHealth == null) playerHealth = FindFirstObjectByType<PlayerHealth>();
        if (playerAnimationRelay == null) playerAnimationRelay = FindFirstObjectByType<PlayerAnimationEventRelay>();
        if (bossAttackController == null) bossAttackController = FindFirstObjectByType<BossAttackController>();
        if (bossHealth == null) bossHealth = FindFirstObjectByType<BossHealth>();

        PrepareAllClips();
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

        if (playerAnimationRelay != null)
        {
            playerAnimationRelay.OnFootstepLanded -= HandleKnightFootstep;
            playerAnimationRelay.OnFootstepLanded += HandleKnightFootstep;
        }

        if (bossAttackController != null)
        {
            bossAttackController.OnAttackWindupStarted -= HandleBossWindup;
            bossAttackController.OnAttackWindupStarted += HandleBossWindup;
            bossAttackController.OnAttackImpact -= HandleBossImpact;
            bossAttackController.OnAttackImpact += HandleBossImpact;
        }

        if (bossHealth != null)
        {
            bossHealth.OnDied -= StopBossWindupSound;
            bossHealth.OnDied += StopBossWindupSound;
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

        if (playerAnimationRelay != null)
        {
            playerAnimationRelay.OnFootstepLanded -= HandleKnightFootstep;
        }

        if (bossAttackController != null)
        {
            bossAttackController.OnAttackWindupStarted -= HandleBossWindup;
            bossAttackController.OnAttackImpact -= HandleBossImpact;
        }

        if (bossHealth != null)
        {
            bossHealth.OnDied -= StopBossWindupSound;
        }
    }

    private void OnDestroy()
    {
        for (int i = 0; i < runtimeGeneratedClips.Count; i++)
        {
            if (runtimeGeneratedClips[i] != null)
            {
                Destroy(runtimeGeneratedClips[i]);
            }
        }
        runtimeGeneratedClips.Clear();
    }

    private void HandlePlayerSwordSlash(int stepIndex)
    {
        AudioClip chosen = stepIndex % 2 == 0
            ? (preparedSlash1 != null ? preparedSlash1 : preparedSlash2)
            : (preparedSlash2 != null ? preparedSlash2 : preparedSlash1);
        PlayOneShotSafe(sfxSource, chosen, swordSlashVolume);
    }

    private void HandlePlayerDodgeRoll(Vector3 rollDirection)
    {
        PlayOneShotSafe(sfxSource, preparedRoll, dodgeRollVolume);
    }

    private void HandlePlayerHurt(float damageAmount, float remainingHealth)
    {
        PlayOneShotSafe(sfxSource, preparedHurt, playerHurtVolume);
    }

    private void HandleKnightFootstep()
    {
        if (preparedFootsteps == null || preparedFootsteps.Length == 0 || footstepSource == null)
        {
            return;
        }

        AudioClip stepClip = preparedFootsteps[nextFootstepVariantIndex];
        nextFootstepVariantIndex = (nextFootstepVariantIndex + 1) % preparedFootsteps.Length;

        if (stepClip == null) return;
        footstepSource.pitch = footstepPitchJitter > 0f
            ? 1f + Random.Range(-footstepPitchJitter, footstepPitchJitter)
            : 1f;
        footstepSource.PlayOneShot(stepClip, Mathf.Clamp01(masterVolume * knightFootstepVolume));
    }

    private void HandleBossWindup(BossAttackData attackData)
    {
        if (windupSource == null || preparedBossWindup == null) return;
        windupSource.Stop();
        windupSource.clip = preparedBossWindup;
        windupSource.volume = Mathf.Clamp01(masterVolume * bossWindupVolume);
        windupSource.Play();
    }

    private void HandleBossImpact(BossAttackData attackData)
    {
        StopBossWindupSound();

        bool isStrongAttack = attackData != null &&
                              (attackData.GroundShockwave || attackData.ShapeType == TelegraphShapeType.BoxLine);
        AudioClip impactClip = isStrongAttack
            ? (preparedBossSlash2 != null ? preparedBossSlash2 : preparedBossSlash1)
            : (preparedBossSlash1 != null ? preparedBossSlash1 : preparedBossSlash2);

        PlayOneShotSafe(sfxSource, impactClip, bossSlashVolume);
    }

    private void StopBossWindupSound()
    {
        if (windupSource != null && windupSource.isPlaying)
        {
            windupSource.Stop();
        }
    }

    private void PlayOneShotSafe(AudioSource source, AudioClip clip, float relativeVolume)
    {
        if (clip == null || source == null) return;
        source.PlayOneShot(clip, Mathf.Clamp01(masterVolume * relativeVolume));
    }

    private void PrepareAllClips()
    {
        preparedSlash1 = PrepareSingleClip(swordSlash1Clip, "Prepared_Slash1", 0.02f, -1f);
        preparedSlash2 = PrepareSingleClip(swordSlash2Clip, "Prepared_Slash2", 0.02f, -1f);
        preparedRoll = PrepareSingleClip(dodgeRollClip, "Prepared_Roll", 0.02f, -1f);
        preparedHurt = PrepareSingleClip(playerHurtClip, "Prepared_PlayerHurt", 0.02f, -1f);
        preparedBossWindup = PrepareSingleClip(bossWindupClip, "Prepared_BossWindup", 0.02f, maxBossWindupDuration);
        preparedBossSlash1 = PrepareSingleClip(bossSlash1Clip, "Prepared_BossSlash1", 0.02f, -1f);
        preparedBossSlash2 = PrepareSingleClip(bossSlash2Clip, "Prepared_BossSlash2", 0.02f, -1f);
        preparedFootsteps = ExtractFootstepVariants(knightFootstepsClip);
    }

    private AudioClip PrepareSingleClip(AudioClip source, string clipName, float threshold, float maxDurationSeconds)
    {
        if (source == null) return null;
        if (!trimLeadingSilence && maxDurationSeconds <= 0f) return source;
        if (source.loadState != AudioDataLoadState.Loaded && !source.LoadAudioData()) return source;

        int totalFrames = source.samples;
        int channels = source.channels;
        int freq = source.frequency;
        if (totalFrames <= 0 || channels <= 0 || freq <= 0) return source;

        float[] raw = new float[totalFrames * channels];
        if (!source.GetData(raw, 0)) return source;

        int startFrame = 0;
        if (trimLeadingSilence)
        {
            int firstActive = FindFirstActiveFrame(raw, totalFrames, channels, threshold);
            if (firstActive > 0)
            {
                // Giữ lại 8ms đệm trước xung âm để tiếng đánh bật lên tự nhiên
                int preRoll = Mathf.RoundToInt(0.008f * freq);
                startFrame = Mathf.Max(0, firstActive - preRoll);
            }
        }

        int endFrame = FindLastActiveFrame(raw, totalFrames, channels, 0.006f);
        if (endFrame <= startFrame) endFrame = totalFrames - 1;

        // Giữ 30ms đuôi fade-out
        endFrame = Mathf.Min(totalFrames - 1, endFrame + Mathf.RoundToInt(0.03f * freq));

        if (maxDurationSeconds > 0f)
        {
            int maxEnd = startFrame + Mathf.RoundToInt(maxDurationSeconds * freq);
            if (endFrame > maxEnd) endFrame = maxEnd;
        }

        int trimmedFrames = endFrame - startFrame + 1;
        if (trimmedFrames <= 16) return source;

        // Nếu clip đã gọn sẵn ở đầu và không bị cắt đuôi thì dùng trực tiếp asset gốc
        if (startFrame <= Mathf.RoundToInt(0.025f * freq) && trimmedFrames >= totalFrames - Mathf.RoundToInt(0.15f * freq))
        {
            return source;
        }

        float[] slice = new float[trimmedFrames * channels];
        System.Array.Copy(raw, startFrame * channels, slice, 0, slice.Length);
        ApplySmoothFades(slice, trimmedFrames, channels, freq, 0.004f, 0.035f);

        AudioClip prepared = AudioClip.Create(clipName, trimmedFrames, channels, freq, false);
        prepared.SetData(slice, 0);
        runtimeGeneratedClips.Add(prepared);
        return prepared;
    }

    private AudioClip[] ExtractFootstepVariants(AudioClip source)
    {
        if (source == null) return System.Array.Empty<AudioClip>();
        if (source.loadState != AudioDataLoadState.Loaded && !source.LoadAudioData())
        {
            return new[] { source };
        }

        int totalFrames = source.samples;
        int channels = source.channels;
        int freq = source.frequency;
        if (totalFrames <= 0 || channels <= 0 || freq <= 0) return new[] { source };

        // Nếu clip ngắn sẵn (< 0.6s) thì coi như 1 tiếng bước chân đơn
        if (source.length <= 0.6f)
        {
            AudioClip single = PrepareSingleClip(source, "Prepared_FootstepSingle", 0.015f, -1f);
            return single != null ? new[] { single } : new[] { source };
        }

        float[] raw = new float[totalFrames * channels];
        if (!source.GetData(raw, 0)) return new[] { source };

        // Tìm đỉnh biên độ cục bộ trong từng cửa sổ 0.45s để tách riêng các tiếng bước chân rõ nhất
        int sliceFrames = Mathf.Max(128, Mathf.RoundToInt(singleFootstepDuration * freq));
        int prePeakFrames = Mathf.RoundToInt(0.045f * freq);
        int minSeparationFrames = Mathf.RoundToInt(0.45f * freq);

        List<int> selectedCenters = new List<int>(8);
        List<float> selectedPeaks = new List<float>(8);

        int windowFrames = Mathf.RoundToInt(0.5f * freq);
        for (int start = 0; start < totalFrames; start += windowFrames)
        {
            int end = Mathf.Min(totalFrames, start + windowFrames);
            float localPeak = 0f;
            int localPeakFrame = -1;

            for (int f = start; f < end; f++)
            {
                float frameAbs = 0f;
                int baseIdx = f * channels;
                for (int c = 0; c < channels; c++)
                {
                    float a = Mathf.Abs(raw[baseIdx + c]);
                    if (a > frameAbs) frameAbs = a;
                }

                if (frameAbs > localPeak)
                {
                    localPeak = frameAbs;
                    localPeakFrame = f;
                }
            }

            if (localPeak >= 0.055f && localPeakFrame >= 0)
            {
                if (selectedCenters.Count > 0 && localPeakFrame - selectedCenters[selectedCenters.Count - 1] < minSeparationFrames)
                {
                    int lastIdx = selectedCenters.Count - 1;
                    if (localPeak > selectedPeaks[lastIdx])
                    {
                        selectedCenters[lastIdx] = localPeakFrame;
                        selectedPeaks[lastIdx] = localPeak;
                    }
                }
                else
                {
                    selectedCenters.Add(localPeakFrame);
                    selectedPeaks.Add(localPeak);
                }
            }
        }

        if (selectedCenters.Count == 0)
        {
            AudioClip fallback = PrepareSingleClip(source, "Prepared_FootstepFallback", 0.01f, singleFootstepDuration);
            return fallback != null ? new[] { fallback } : new[] { source };
        }

        int maxVariants = Mathf.Min(6, selectedCenters.Count);
        AudioClip[] variants = new AudioClip[maxVariants];

        for (int i = 0; i < maxVariants; i++)
        {
            int centerFrame = selectedCenters[i];
            int startFrame = Mathf.Clamp(centerFrame - prePeakFrames, 0, Mathf.Max(0, totalFrames - sliceFrames));
            int countFrames = Mathf.Min(sliceFrames, totalFrames - startFrame);

            float[] slice = new float[countFrames * channels];
            System.Array.Copy(raw, startFrame * channels, slice, 0, slice.Length);

            // Chuẩn hóa biên độ bước chân về 0.78f vì file gốc khá nhỏ (~0.08)
            float peak = selectedPeaks[i];
            if (peak > 0.001f)
            {
                float gain = Mathf.Min(10f, 0.78f / peak);
                for (int s = 0; s < slice.Length; s++)
                {
                    slice[s] = Mathf.Clamp(slice[s] * gain, -0.95f, 0.95f);
                }
            }

            ApplySmoothFades(slice, countFrames, channels, freq, 0.008f, 0.05f);

            AudioClip stepClip = AudioClip.Create($"Prepared_KnightStep_{i + 1}", countFrames, channels, freq, false);
            stepClip.SetData(slice, 0);
            runtimeGeneratedClips.Add(stepClip);
            variants[i] = stepClip;
        }

        return variants;
    }

    private static int FindFirstActiveFrame(float[] raw, int totalFrames, int channels, float threshold)
    {
        for (int f = 0; f < totalFrames; f++)
        {
            int baseIdx = f * channels;
            for (int c = 0; c < channels; c++)
            {
                if (Mathf.Abs(raw[baseIdx + c]) >= threshold)
                {
                    return f;
                }
            }
        }
        return -1;
    }

    private static int FindLastActiveFrame(float[] raw, int totalFrames, int channels, float threshold)
    {
        for (int f = totalFrames - 1; f >= 0; f--)
        {
            int baseIdx = f * channels;
            for (int c = 0; c < channels; c++)
            {
                if (Mathf.Abs(raw[baseIdx + c]) >= threshold)
                {
                    return f;
                }
            }
        }
        return -1;
    }

    private static void ApplySmoothFades(float[] samples, int frameCount, int channels, int freq, float fadeInSeconds, float fadeOutSeconds)
    {
        int fadeInFrames = Mathf.Min(frameCount / 3, Mathf.RoundToInt(fadeInSeconds * freq));
        int fadeOutFrames = Mathf.Min(frameCount / 2, Mathf.RoundToInt(fadeOutSeconds * freq));

        for (int f = 0; f < fadeInFrames; f++)
        {
            float w = (float)f / fadeInFrames;
            int baseIdx = f * channels;
            for (int c = 0; c < channels; c++) samples[baseIdx + c] *= w;
        }

        for (int f = 0; f < fadeOutFrames; f++)
        {
            float w = (float)f / fadeOutFrames;
            int frameIdx = frameCount - 1 - f;
            int baseIdx = frameIdx * channels;
            for (int c = 0; c < channels; c++) samples[baseIdx + c] *= w;
        }
    }

    private static void Configure2DAudioSource(AudioSource src)
    {
        if (src == null) return;
        src.playOnAwake = false;
        src.loop = false;
        src.spatialBlend = 0f;
    }
}
