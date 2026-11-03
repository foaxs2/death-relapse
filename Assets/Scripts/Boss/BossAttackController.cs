using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Điều khiển đòn đánh báo trước (telegraph) hình quạt của Boss theo kiểu Dark Souls.
/// Vùng quạt trên sàn chỉ là CẢNH BÁO (không gây sát thương); sát thương thực tế đến từ
/// thân kiếm (SwordHitbox) trong cửa sổ trúng do Animation Event mở và đóng.
/// </summary>
public class BossAttackController : MonoBehaviour
{
    [Header("Attack Configuration")]
    [Tooltip("Dữ liệu cấu hình đòn đánh hiện tại (ScriptableObject)")]
    [SerializeField] private BossAttackData currentAttack;

    [Tooltip("Danh sách các đòn đánh để Boss tự chọn ngẫu nhiên mỗi lần ra đòn (nếu để trống sẽ dùng currentAttack)")]
    [SerializeField] private BossAttackData[] attackPool;

    [Tooltip("Bật/tắt hiển thị vùng quạt đỏ trên sàn (Mặc định tắt cho Thí nghiệm 1 không vùng đỏ)")]
    [SerializeField] private bool showTelegraphVisuals = false;

    [Header("Boss AI & Movement (Tuần 4)")]
    [Tooltip("Bật hành vi AI tự động xoay mặt, đi bộ đuổi theo Player và ra đòn")]
    [SerializeField] private bool enableAutoAI = true;

    [Tooltip("Tốc độ đi bộ của Boss khi tiến về phía Player (m/s)")]
    [Min(0f)]
    [SerializeField] private float moveSpeed = 2.8f;

    [Tooltip("Tốc độ xoay người của Boss hướng về phía Player (độ/giây)")]
    [Min(0f)]
    [SerializeField] private float rotationSpeed = 360f;

    [Tooltip("Khoảng cách (m) mà Boss dừng đi bộ và bắt đầu tung đòn đánh")]
    [Min(0.5f)]
    [SerializeField] private float attackRange = 2.5f;

    [Tooltip("Thời gian nghỉ giữa 2 đòn đánh liên tiếp của Boss (giây)")]
    [Min(0f)]
    [SerializeField] private float attackCooldown = 1.5f;

    [Tooltip("Tên parameter Float trong Animator của Boss để điều khiển chuyển trạng thái đi bộ (GreatAxeWalk)")]
    [SerializeField] private string walkSpeedParam = "Speed";

    [Header("References")]
    [Tooltip("Animator của Boss để kích hoạt animation chém")]
    [SerializeField] private Animator animator;

    [Tooltip("Transform của Player để kiểm tra tính hợp lệ với vùng cảnh báo")]
    [SerializeField] private Transform player;

    [Tooltip("Hitbox lưỡi kiếm thực tế gắn trên vũ khí của Boss")]
    [SerializeField] private SwordHitbox swordHitbox;

    [Tooltip("MeshFilter của GameObject hiển thị quạt đỏ điền dần (Fill)")]
    [SerializeField] private MeshFilter telegraphFillMesh;

    [Tooltip("MeshFilter của GameObject hiển thị viền/khung quạt tối đa (Outline)")]
    [SerializeField] private MeshFilter telegraphOutlineMesh;

    [Header("Animation & Timing State")]
    [Tooltip("Tên State đòn đánh mặc định trong Animator Controller (dùng nếu BossAttackData không khai báo riêng)")]
    [SerializeField] private string attackStateName = "Red Attack";

    [Tooltip("Thời gian chờ tối đa (giây) sau khi Fill đầy nếu chưa nhận được OnHitWindowOpen")]
    [SerializeField] private float hitWindowTimeoutSeconds = 1.5f;

    [Tooltip("Thời gian chờ tối đa (giây) để Animator đóng cửa sổ và quay về Idle")]
    [SerializeField] private float endOfAttackTimeoutSeconds = 8f;

    /// <summary>
    /// Sự kiện C# phát ra ngay khi lưỡi kiếm chạm trúng Player trong cửa sổ trúng.
    /// Tham số: BossAttackData của đòn, bool insideZone (true nếu lúc trúng Player nằm trong vùng cảnh báo đã chốt).
    /// </summary>
    public event Action<BossAttackData, bool> PlayerHitByAttack;

    /// <summary>
    /// Sự kiện C# phát ra khi cửa sổ trúng đóng lại (kết thúc pha gây sát thương của đòn).
    /// Tham số: BossAttackData của đòn, bool isHit (true nếu đã trúng người chơi trong cửa sổ).
    /// </summary>
    public event Action<BossAttackData, bool> AttackResolved;

    // Trạng thái đòn đánh & AI
    private bool isAttacking = false;
    private bool hitWindowOpened = false;
    private bool hitWindowClosed = false;
    private bool hasProcessedPlayerContactThisWindow = false;
    private bool hasHitPlayerThisWindow = false;
    private bool hasDodgedThisWindow = false;
    private float attackTriggerRealtime = 0f;
    private float lastAttackEndTime = -999f;
    private Coroutine attackCoroutine;

    // Tham chiếu máu để kiểm tra trạng thái sống/chết của Boss và Player
    private BossHealth bossHealth;
    private PlayerHealth playerHealth;
    private int walkSpeedHash;

    // Dữ liệu hình học được chốt MỘT LẦN khi bắt đầu đòn (Snap-shot)
    private Vector3 lockedCenter;
    private Vector3 lockedForward;
    private float lockedRadius;
    private float lockedArcAngle;
    private float lockedDuration;

    // Cache Mesh quạt đơn vị (bán kính = 1m) dùng chung cho cả Fill và Outline
    private Mesh sharedSectorMesh;
    private float cachedMeshArcAngle = -1f;

    // Độ lệch Y nhỏ để tránh Z-fighting (nhấp nháy xuyên mặt sàn)
    private const float FloorYOffset = 0.015f;
    private const float FillHeightBias = 0.002f;
    private const float TelegraphSyncToleranceSeconds = 0.05f;

    public bool IsAttacking => isAttacking;
    public bool ShowTelegraphVisuals
    {
        get => showTelegraphVisuals;
        set => showTelegraphVisuals = value;
    }

    private void Awake()
    {
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        if (swordHitbox == null)
        {
            swordHitbox = GetComponentInChildren<SwordHitbox>();
        }

        bossHealth = GetComponent<BossHealth>();
        if (bossHealth == null)
        {
            bossHealth = GetComponentInParent<BossHealth>();
        }

        if (player == null)
        {
            PlayerMovement foundPlayer = FindFirstObjectByType<PlayerMovement>();
            if (foundPlayer != null)
            {
                player = foundPlayer.transform;
            }
        }

        if (player != null)
        {
            playerHealth = player.GetComponentInParent<PlayerHealth>();
        }

        if (!string.IsNullOrEmpty(walkSpeedParam))
        {
            walkSpeedHash = Animator.StringToHash(walkSpeedParam);
        }

        HideTelegraphVisuals();
    }

    private void Update()
    {
        if (bossHealth != null && bossHealth.IsDead)
        {
            UpdateWalkAnimator(0f);
            return;
        }

        // Phím thử nghiệm: Số 1 trên bàn phím (Input System mới)
        if (Keyboard.current != null && Keyboard.current.digit1Key.wasPressedThisFrame)
        {
            if (!isAttacking)
            {
                TriggerAttack();
            }
        }

        if (enableAutoAI)
        {
            UpdateAutoAI();
        }
    }

    /// <summary>
    /// Vòng lặp AI tự động: xoay hướng mặt về phía Player, đi bộ đuổi theo nếu ngoài tầm, và tung đòn khi vào tầm.
    /// </summary>
    private void UpdateAutoAI()
    {
        if (isAttacking || player == null || (playerHealth != null && playerHealth.IsDead))
        {
            UpdateWalkAnimator(0f);
            return;
        }

        Vector3 toPlayerXZ = Vector3.ProjectOnPlane(player.position - transform.position, Vector3.up);
        float distanceToPlayer = toPlayerXZ.magnitude;

        // 1. Luôn xoay mặt mượt mà hướng về phía Player trên mặt phẳng XZ
        if (toPlayerXZ.sqrMagnitude > 0.0001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(toPlayerXZ.normalized, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
        }

        // 2. Nếu ngoài tầm đánh: tiến về phía Player và phát animation đi bộ (GreatAxeWalk)
        if (distanceToPlayer > attackRange)
        {
            Vector3 moveDirection = toPlayerXZ.normalized;
            transform.position += moveDirection * (moveSpeed * Time.deltaTime);
            UpdateWalkAnimator(moveSpeed);
        }
        else
        {
            // 3. Khi đã vào tầm đánh: dừng đi bộ và kiểm tra thời gian hồi chiêu để ra đòn
            UpdateWalkAnimator(0f);

            if (Time.time >= lastAttackEndTime + attackCooldown)
            {
                TriggerAttack();
            }
        }
    }

    private void UpdateWalkAnimator(float currentSpeed)
    {
        if (animator == null || walkSpeedHash == 0 || animator.runtimeAnimatorController == null) return;

        foreach (AnimatorControllerParameter param in animator.parameters)
        {
            if (param.nameHash == walkSpeedHash && param.type == AnimatorControllerParameterType.Float)
            {
                animator.SetFloat(walkSpeedHash, currentSpeed);
                break;
            }
        }
    }

    private void OnDisable()
    {
        if (attackCoroutine != null)
        {
            StopCoroutine(attackCoroutine);
            attackCoroutine = null;
        }

        if (swordHitbox != null)
        {
            swordHitbox.CloseWindow();
        }

        UpdateWalkAnimator(0f);
        HideTelegraphVisuals();
        isAttacking = false;
        hitWindowOpened = false;
        hitWindowClosed = false;
        hasProcessedPlayerContactThisWindow = false;
        hasHitPlayerThisWindow = false;
        hasDodgedThisWindow = false;
    }

    private void OnDestroy()
    {
        if (sharedSectorMesh != null)
        {
            Destroy(sharedSectorMesh);
        }
    }

    /// <summary>
    /// Chọn ngẫu nhiên 1 đòn đánh hợp lệ từ attackPool (nếu có), ngược lại giữ nguyên currentAttack.
    /// </summary>
    private void SelectAttackFromPool()
    {
        if (attackPool == null || attackPool.Length == 0) return;

        int validCount = 0;
        for (int i = 0; i < attackPool.Length; i++)
        {
            if (attackPool[i] != null) validCount++;
        }

        if (validCount == 0) return;

        int randomPick = UnityEngine.Random.Range(0, validCount);
        int currentIndex = 0;
        for (int i = 0; i < attackPool.Length; i++)
        {
            if (attackPool[i] == null) continue;
            if (currentIndex == randomPick)
            {
                currentAttack = attackPool[i];
                return;
            }
            currentIndex++;
        }
    }

    private string GetActiveAttackStateName()
    {
        if (currentAttack != null && !string.IsNullOrEmpty(currentAttack.AttackStateName))
        {
            return currentAttack.AttackStateName;
        }
        return attackStateName;
    }

    /// <summary>
    /// Kích hoạt chu kỳ đòn đánh báo trước dựa theo attackPool hoặc currentAttack.
    /// </summary>
    public void TriggerAttack()
    {
        if (isAttacking) return;
        if (bossHealth != null && bossHealth.IsDead) return;

        SelectAttackFromPool();

        if (currentAttack == null)
        {
            Debug.LogWarning("[BossAttackController] Chưa gán 'currentAttack' hoặc 'attackPool' (BossAttackData) trong Inspector!");
            return;
        }

        string activeStateName = GetActiveAttackStateName();

        // Đảm bảo Animator đã hoàn toàn về Idle/Walk trước khi bắt đầu đòn mới (chống kẹt trigger)
        if (animator != null && !string.IsNullOrEmpty(activeStateName))
        {
            AnimatorStateInfo currentInfo = animator.GetCurrentAnimatorStateInfo(0);
            if (currentInfo.IsName(activeStateName) || animator.IsInTransition(0))
            {
                return;
            }
        }

        // Xoay hướng mặt Boss nhắm thẳng về phía Player ngay thời điểm bắt đầu gồng đòn
        if (player != null)
        {
            Vector3 toPlayerXZ = Vector3.ProjectOnPlane(player.position - transform.position, Vector3.up);
            if (toPlayerXZ.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(toPlayerXZ.normalized, Vector3.up);
            }
        }

        attackCoroutine = StartCoroutine(AttackRoutine());
    }

    /// <summary>
    /// Luồng chính xử lý đòn đánh:
    /// Chốt hình học -> Trigger Animator -> Điền vùng quạt theo telegraphDuration ->
    /// Chờ OnHitWindowOpen -> Chờ OnHitWindowClose và Animator rời state đòn đánh.
    /// </summary>
    private IEnumerator AttackRoutine()
    {
        isAttacking = true;
        hitWindowOpened = false;
        hitWindowClosed = false;
        hasProcessedPlayerContactThisWindow = false;
        hasHitPlayerThisWindow = false;
        hasDodgedThisWindow = false;

        if (swordHitbox != null)
        {
            swordHitbox.CloseWindow();
        }

        // 1. CHỐT MỘT LẦN toàn bộ thông số hình học và thời gian từ ScriptableObject
        lockedCenter = new Vector3(transform.position.x, transform.position.y + FloorYOffset, transform.position.z);

        Vector3 bossForwardXZ = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (bossForwardXZ.sqrMagnitude < 0.0001f)
        {
            bossForwardXZ = Vector3.forward;
        }
        bossForwardXZ.Normalize();

        lockedForward = Quaternion.AngleAxis(currentAttack.FacingOffsetDegrees, Vector3.up) * bossForwardXZ;
        lockedRadius = currentAttack.Radius;
        lockedArcAngle = currentAttack.ArcAngleDegrees;
        lockedDuration = Mathf.Max(0.01f, currentAttack.TelegraphDuration);

        // 2. Kích hoạt Animator trigger trước (để ưu tiên chuyển thẳng Walk -> Red Attack thay vì bị kẹt qua Walk -> Idle 0.25s)
        if (animator != null && !string.IsNullOrEmpty(currentAttack.AnimatorTrigger))
        {
            animator.ResetTrigger(currentAttack.AnimatorTrigger);
            animator.SetTrigger(currentAttack.AnimatorTrigger);
        }
        attackTriggerRealtime = Time.realtimeSinceStartup;
        UpdateWalkAnimator(0f);

        // 3. Chuẩn bị Mesh hình quạt đơn vị và hiện vùng quạt cảnh báo (chỉ hiện khi showTelegraphVisuals == true)
        if (showTelegraphVisuals)
        {
            EnsureSectorMesh(lockedArcAngle);

            Quaternion attackRotation = Quaternion.LookRotation(lockedForward, Vector3.up);

            if (telegraphOutlineMesh != null)
            {
                telegraphOutlineMesh.transform.position = lockedCenter;
                telegraphOutlineMesh.transform.rotation = attackRotation;
                telegraphOutlineMesh.transform.localScale = new Vector3(lockedRadius, 1f, lockedRadius);
                telegraphOutlineMesh.gameObject.SetActive(true);
            }

            if (telegraphFillMesh != null)
            {
                telegraphFillMesh.transform.position = lockedCenter + Vector3.up * FillHeightBias;
                telegraphFillMesh.transform.rotation = attackRotation;
                telegraphFillMesh.transform.localScale = new Vector3(0f, 1f, 0f);
                telegraphFillMesh.gameObject.SetActive(true);
            }
        }
        else
        {
            HideTelegraphVisuals();
        }

        Debug.Log($"[BossAttackController] BẮT ĐẦU đòn '{currentAttack.AttackName}' | " +
                  $"Telegraph: {lockedDuration:F2}s (Hiển thị vùng đỏ: {(showTelegraphVisuals ? "BẬT" : "TẮT")}) | " +
                  $"Bán kính: {lockedRadius:F2}m | Góc quét: {lockedArcAngle:F1}° | Offset: {currentAttack.FacingOffsetDegrees:F1}°");

        // 4. Tiến trình điền đầy (Fill) theo telegraphDuration bằng Time.deltaTime
        // Vẫn chờ đúng telegraphDuration ngay cả khi ẩn vùng đỏ để animation gồng đòn không đổi
        float elapsedTime = 0f;
        while (elapsedTime < lockedDuration && !hitWindowOpened)
        {
            elapsedTime += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsedTime / lockedDuration);
            float currentFillRadius = lockedRadius * progress;

            if (showTelegraphVisuals && telegraphFillMesh != null)
            {
                telegraphFillMesh.transform.localScale = new Vector3(currentFillRadius, 1f, currentFillRadius);
            }

            yield return null;
        }

        // Khi Fill đã đầy mà chưa nhận OnHitWindowOpen: giữ vùng đầy và chờ tiếp tối đa hitWindowTimeoutSeconds
        if (!hitWindowOpened)
        {
            if (showTelegraphVisuals && telegraphFillMesh != null)
            {
                telegraphFillMesh.transform.localScale = new Vector3(lockedRadius, 1f, lockedRadius);
            }

            float maxWaitForOpen = lockedDuration + Mathf.Max(0f, hitWindowTimeoutSeconds);
            while (!hitWindowOpened && elapsedTime < maxWaitForOpen)
            {
                elapsedTime += Time.deltaTime;
                yield return null;
            }

            if (!hitWindowOpened)
            {
                Debug.LogError($"[BossAttackController] HỦY ĐÒN '{currentAttack.AttackName}': Quá thời gian chờ ({maxWaitForOpen:F2}s) " +
                               "mà chưa nhận được Animation Event 'OnHitWindowOpen'. Hãy kiểm tra lại Event trên clip hoặc BossAnimationEventRelay.");
                
                // Thu hồi trigger để không bị kích hoạt trễ khi Animator về Idle
                if (animator != null && !string.IsNullOrEmpty(currentAttack.AnimatorTrigger))
                {
                    animator.ResetTrigger(currentAttack.AnimatorTrigger);
                }

                HideTelegraphVisuals();
                isAttacking = false;
                lastAttackEndTime = Time.time;
                attackCoroutine = null;
                yield break;
            }
        }

        // 5. Chờ đóng cửa sổ trúng (OnHitWindowClose) và chờ Animator rời state đòn đánh
        float postOpenTimer = 0f;
        float maxPostOpenWait = Mathf.Max(0.5f, endOfAttackTimeoutSeconds);

        // Chờ đến khi cửa sổ đóng (hoặc hết timeout)
        while (!hitWindowClosed && postOpenTimer < maxPostOpenWait)
        {
            postOpenTimer += Time.deltaTime;
            yield return null;
        }

        if (!hitWindowClosed)
        {
            Debug.LogWarning($"[BossAttackController] Quá hạn chờ 'OnHitWindowClose' ({maxPostOpenWait:F2}s). Tự động đóng cửa sổ hitbox.");
            NotifyHitWindowClose();
        }

        // Đợi ít nhất 1 frame để Animator cập nhật state ổn định rồi đợi tới khi hoàn toàn rời khỏi state đòn đánh
        yield return null;

        string activeStateName = GetActiveAttackStateName();
        if (animator != null && !string.IsNullOrEmpty(activeStateName))
        {
            while (postOpenTimer < maxPostOpenWait)
            {
                AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);
                bool inAttackState = stateInfo.IsName(activeStateName);
                bool inTransition = animator.IsInTransition(0);

                if (!inAttackState && !inTransition)
                {
                    break;
                }

                postOpenTimer += Time.deltaTime;
                yield return null;
            }
        }

        isAttacking = false;
        lastAttackEndTime = Time.time;
        attackCoroutine = null;
    }

    /// <summary>
    /// Được gọi từ BossAnimationEventRelay khi Animation Event 'OnHitWindowOpen' kích hoạt.
    /// </summary>
    public void NotifyHitWindowOpen()
    {
        if (!isAttacking || hitWindowOpened) return;

        hitWindowOpened = true;
        hasProcessedPlayerContactThisWindow = false;
        hasHitPlayerThisWindow = false;
        hasDodgedThisWindow = false;

        // Đo thời gian thực từ lúc SetTrigger đến khi mở cửa sổ trúng
        float actualWindupSeconds = Time.realtimeSinceStartup - attackTriggerRealtime;
        float diff = actualWindupSeconds - lockedDuration;

        Debug.Log($"[BossAttackController] OnHitWindowOpen | Thời gian gồng thực tế: {actualWindupSeconds:F3}s " +
                  $"(Mục tiêu telegraphDuration: {lockedDuration:F3}s | Lệch: {diff:+0.000;-0.000;0.000}s)");

        if (Mathf.Abs(diff) > TelegraphSyncToleranceSeconds)
        {
            Debug.LogWarning($"[BossAttackController] Thời gian gồng thực tế ({actualWindupSeconds:F3}s) lệch quá {TelegraphSyncToleranceSeconds:F2}s " +
                             $"so với telegraphDuration ({lockedDuration:F3}s). Hãy điều chỉnh Speed của state '{attackStateName}' trong Animator!");
        }

        // Ẩn vùng cảnh báo NGAY khi kiếm bắt đầu có sát thương
        HideTelegraphVisuals();

        if (swordHitbox != null)
        {
            swordHitbox.OpenWindow(HandlePlayerHit);
        }
        else
        {
            Debug.LogError("[BossAttackController] Chưa gán 'swordHitbox' trong Inspector! Không thể kiểm tra va chạm kiếm.");
        }
    }

    /// <summary>
    /// Được gọi từ BossAnimationEventRelay khi Animation Event 'OnHitWindowClose' kích hoạt.
    /// </summary>
    public void NotifyHitWindowClose()
    {
        if (!isAttacking || hitWindowClosed) return;

        hitWindowClosed = true;

        if (swordHitbox != null)
        {
            swordHitbox.CloseWindow();
        }

        AttackResolved?.Invoke(currentAttack, hasHitPlayerThisWindow);

        string outcomeText = hasDodgedThisWindow
            ? "<color=cyan>ĐÃ NÉ (I-FRAME)</color>"
            : (hasHitPlayerThisWindow ? "<color=red>TRÚNG</color>" : "<color=green>HỤT</color>");

        Debug.Log($"[BossAttackController] OnHitWindowClose -> KẾT THÚC pha chém '{currentAttack.AttackName}' | " +
                  $"Kết quả: {outcomeText}");
    }

    /// <summary>
    /// Xử lý khi SwordHitbox chạm trúng collider của Player (chỉ gọi tối đa 1 lần mỗi cửa sổ).
    /// </summary>
    private void HandlePlayerHit(Collider hitCollider)
    {
        if (hasProcessedPlayerContactThisWindow) return;
        hasProcessedPlayerContactThisWindow = true;

        bool insideZone = IsInsideLockedZone(
            lockedCenter,
            lockedForward,
            lockedRadius,
            lockedArcAngle,
            out float distanceXZ,
            out float angleToPlayer
        );

        // Kiểm tra xem Player có đang lăn né (Roll / I-Frame) hay không
        PlayerDodge playerDodge = hitCollider.GetComponentInParent<PlayerDodge>();
        bool isInvulnerable = (playerDodge != null && playerDodge.IsInvulnerable);
        bool isPlayerRollingOrInvulnerable = (playerDodge != null && (playerDodge.IsInvulnerable || playerDodge.IsRolling));

        if (isInvulnerable)
        {
            hasDodgedThisWindow = true;
            hasHitPlayerThisWindow = false;
        }
        else
        {
            hasHitPlayerThisWindow = true;
        }

        Debug.Log($"[BossAttackController] Kiếm chạm '{hitCollider.name}' | " +
                  $"Trạng thái Player: {(isInvulnerable ? "<color=cyan>ĐANG I-FRAME (NÉ)</color>" : "BÌNH THƯỜNG")} | " +
                  $"Trong vùng cảnh báo: {(insideZone ? "CÓ" : "KHÔNG")} | " +
                  $"Khoảng cách XZ: {distanceXZ:F2}m/{lockedRadius:F2}m | Góc lệch: {angleToPlayer:F1}°/{lockedArcAngle * 0.5f:F1}°");

        // Chỉ cảnh báo [VALIDITY] nếu người chơi KHÔNG đang lăn né (vì khi lăn né người chơi có thể chủ động lăn vọt ra sau lưng Boss)
        if (!insideZone && !isPlayerRollingOrInvulnerable)
        {
            Debug.LogWarning($"[VALIDITY] Trúng NGOÀI vùng cảnh báo! Khoảng cách XZ = {distanceXZ:F2}m (bán kính = {lockedRadius:F2}m), " +
                             $"Góc lệch = {angleToPlayer:F1}° (nửa góc quạt = {lockedArcAngle * 0.5f:F1}°). " +
                             "Cần kiểm tra lại bán kính/góc quạt của BossAttackData cho khớp với đường kiếm quét.");
        }

        // Bắn sự kiện cho PlayerHealth (và trực tiếp gọi lên PlayerHealth trên collider nếu sự kiện chưa được nối)
        if (PlayerHitByAttack != null)
        {
            PlayerHitByAttack.Invoke(currentAttack, insideZone);
        }
        else
        {
            PlayerHealth targetHealth = hitCollider.GetComponentInParent<PlayerHealth>();
            if (targetHealth != null)
            {
                targetHealth.ReceiveBossAttackHit(currentAttack, insideZone);
            }
        }
    }

    /// <summary>
    /// Kiểm tra vị trí hiện tại của Player có nằm trong vùng hình quạt đã chốt hay không.
    /// Chỉ dùng để ghi log kiểm chứng độ khớp giữa vùng cảnh báo và lưỡi kiếm, KHÔNG quyết định sát thương.
    /// </summary>
    private bool IsInsideLockedZone(
        Vector3 center,
        Vector3 forward,
        float radius,
        float arcAngle,
        out float distanceXZ,
        out float angleToPlayer)
    {
        distanceXZ = 0f;
        angleToPlayer = 0f;

        if (player == null)
        {
            Debug.LogWarning("[BossAttackController] Player Transform chưa được gán, không thể kiểm tra IsInsideLockedZone!");
            return false;
        }

        Vector2 playerXZ = new Vector2(player.position.x, player.position.z);
        Vector2 centerXZ = new Vector2(center.x, center.z);
        distanceXZ = Vector2.Distance(playerXZ, centerXZ);

        Vector3 toPlayer = player.position - center;
        Vector3 toPlayerXZ = Vector3.ProjectOnPlane(toPlayer, Vector3.up);

        if (toPlayerXZ.sqrMagnitude >= 0.0001f)
        {
            angleToPlayer = Vector3.Angle(forward, toPlayerXZ.normalized);
        }

        if (distanceXZ > radius)
        {
            return false;
        }

        return angleToPlayer <= (arcAngle * 0.5f);
    }

    /// <summary>
    /// Tạo hoặc cập nhật Mesh quạt đơn vị (bán kính 1m, 3 độ/phân đoạn, triangle fan).
    /// </summary>
    private void EnsureSectorMesh(float arcAngle)
    {
        if (sharedSectorMesh != null && Mathf.Approximately(cachedMeshArcAngle, arcAngle))
        {
            return;
        }

        if (sharedSectorMesh == null)
        {
            sharedSectorMesh = new Mesh();
            sharedSectorMesh.name = "SectorUnitMesh";
        }

        cachedMeshArcAngle = arcAngle;
        BuildSectorMesh(sharedSectorMesh, arcAngle, 3f);

        if (telegraphFillMesh != null)
        {
            telegraphFillMesh.sharedMesh = sharedSectorMesh;
        }
        if (telegraphOutlineMesh != null)
        {
            telegraphOutlineMesh.sharedMesh = sharedSectorMesh;
        }
    }

    /// <summary>
    /// Dựng Mesh hình quạt dạng Triangle Fan:
    /// Tâm ở gốc (0,0,0), đối xứng qua trục +Z (hướng mặt phẳng), ngửa mặt lên trục +Y.
    /// </summary>
    private void BuildSectorMesh(Mesh mesh, float arcAngle, float degreesPerSegment)
    {
        mesh.Clear();

        int segments = Mathf.Max(1, Mathf.CeilToInt(arcAngle / degreesPerSegment));
        float stepAngle = arcAngle / segments;
        float startAngle = -arcAngle * 0.5f;

        Vector3[] vertices = new Vector3[segments + 2];
        Vector3[] normals = new Vector3[segments + 2];
        Vector2[] uvs = new Vector2[segments + 2];
        int[] triangles = new int[segments * 3];

        // Đỉnh 0 là gốc tâm quạt
        vertices[0] = Vector3.zero;
        normals[0] = Vector3.up;
        uvs[0] = new Vector2(0.5f, 0.5f);

        // Các đỉnh dọc theo viền cung quạt bán kính 1m
        for (int i = 0; i <= segments; i++)
        {
            float currentDeg = startAngle + i * stepAngle;
            float rad = currentDeg * Mathf.Deg2Rad;

            // X = sin, Z = cos để góc 0 độ trùng hướng +Z
            float x = Mathf.Sin(rad);
            float z = Mathf.Cos(rad);

            vertices[i + 1] = new Vector3(x, 0f, z);
            normals[i + 1] = Vector3.up;
            uvs[i + 1] = new Vector2(x * 0.5f + 0.5f, z * 0.5f + 0.5f);
        }

        // Tạo các tam giác fan: (0, i+1, i+2) cho pháp tuyến hướng lên trên (+Y)
        for (int i = 0; i < segments; i++)
        {
            triangles[i * 3 + 0] = 0;
            triangles[i * 3 + 1] = i + 1;
            triangles[i * 3 + 2] = i + 2;
        }

        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
    }

    private void HideTelegraphVisuals()
    {
        if (telegraphFillMesh != null)
        {
            telegraphFillMesh.gameObject.SetActive(false);
        }
        if (telegraphOutlineMesh != null)
        {
            telegraphOutlineMesh.gameObject.SetActive(false);
        }
    }

    private void OnDrawGizmos()
    {
        if (currentAttack == null) return;

        Vector3 center;
        Vector3 forward;
        float radius;
        float arcAngle;

        if (isAttacking)
        {
            // Trong lúc đang đánh: hiển thị đúng vùng hình học đã chốt
            center = lockedCenter;
            forward = lockedForward;
            radius = lockedRadius;
            arcAngle = lockedArcAngle;
            Gizmos.color = Color.red;
        }
        else
        {
            // Khi nghỉ: hiển thị xem trước đòn theo vị trí và hướng hiện tại của Boss
            center = transform.position;
            Vector3 bossForwardXZ = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (bossForwardXZ.sqrMagnitude < 0.0001f)
            {
                bossForwardXZ = Vector3.forward;
            }
            bossForwardXZ.Normalize();

            forward = Quaternion.AngleAxis(currentAttack.FacingOffsetDegrees, Vector3.up) * bossForwardXZ;
            radius = currentAttack.Radius;
            arcAngle = currentAttack.ArcAngleDegrees;
            Gizmos.color = new Color(1f, 0.5f, 0.1f, 0.85f); // Màu cam xem trước
        }

        DrawSectorGizmo(center, forward, radius, arcAngle);
    }

    /// <summary>
    /// Vẽ đường viền hình quạt phẳng trên Scene View để trực quan hóa
    /// </summary>
    private void DrawSectorGizmo(Vector3 center, Vector3 forward, float radius, float arcAngle)
    {
        float halfArc = arcAngle * 0.5f;
        Vector3 leftDir = Quaternion.AngleAxis(-halfArc, Vector3.up) * forward;
        Vector3 rightDir = Quaternion.AngleAxis(halfArc, Vector3.up) * forward;

        // Vẽ 2 cạnh biên nối từ tâm
        Gizmos.DrawLine(center, center + leftDir * radius);
        Gizmos.DrawLine(center, center + rightDir * radius);

        // Vẽ cung tròn phía ngoài
        int segments = Mathf.Max(1, Mathf.CeilToInt(arcAngle / 5f));
        float step = arcAngle / segments;
        Vector3 prevPoint = center + leftDir * radius;

        for (int i = 1; i <= segments; i++)
        {
            float currentAngle = -halfArc + i * step;
            Vector3 dir = Quaternion.AngleAxis(currentAngle, Vector3.up) * forward;
            Vector3 nextPoint = center + dir * radius;
            Gizmos.DrawLine(prevPoint, nextPoint);
            prevPoint = nextPoint;
        }
    }
}
