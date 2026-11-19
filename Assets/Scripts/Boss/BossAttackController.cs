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

    [Tooltip("MeshFilter của vệt nứt mặt đất (đòn xung kích). Gán 1 GameObject có MeshRenderer + material vết nứt, để tắt sẵn. Có thể để trống.")]
    [SerializeField] private MeshFilter groundCrackMesh;

    [Header("Animation & Timing State")]
    [Tooltip("Tên State đòn đánh mặc định trong Animator Controller (dùng nếu BossAttackData không khai báo riêng)")]
    [SerializeField] private string attackStateName = "Red Attack";

    [Tooltip("Thời gian chờ tối đa (giây) sau khi Fill đầy nếu chưa nhận được OnHitWindowOpen")]
    [SerializeField] private float hitWindowTimeoutSeconds = 1.5f;

    [Tooltip("Thời gian chờ tối đa (giây) để Animator đóng cửa sổ và quay về Idle")]
    [SerializeField] private float endOfAttackTimeoutSeconds = 8f;

    [Tooltip("Thời lượng luồng sóng xung kích gây sát thương tính từ lúc đập đất (giây). Sau thời gian này luồng sóng tan, đi bộ vào sẽ không dính đòn.")]
    [SerializeField] private float shockwaveActiveDuration = 0.35f;

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
    private PlayerDodge playerDodge;
    private Collider playerCollider;

    // Dữ liệu hình học được chốt MỘT LẦN khi bắt đầu đòn (Snap-shot)
    private Vector3 lockedCenter;
    private Vector3 lockedForward;
    private float lockedRadius;
    private float lockedArcAngle;
    private float lockedDuration;
    private TelegraphShapeType lockedShape;
    private float lockedBoxWidth;

    // Thống kê từng loại đòn trong trận (reset khi Replay)
    [Serializable]
    public class AttackStatEntry
    {
        public string attackName;
        public int totalCasts;
        public int hits;
        public int iframeDodges;
        public int misses;
    }

    private readonly System.Collections.Generic.List<AttackStatEntry> attackStats = new System.Collections.Generic.List<AttackStatEntry>();

    [Header("Terrain & Obstacle Navigation")]
    [Tooltip("Tự động bám cao độ địa hình (raycast xuống sàn) và né chướng ngại vật thay vì đi xuyên địa hình Y=0")]
    [SerializeField] private bool snapToTerrain = true;
    [SerializeField] private float groundCheckUpOffset = 2.0f;
    [SerializeField] private float groundCheckDistance = 8.0f;
    [SerializeField] private float bossObstacleRadius = 0.55f;

    private readonly RaycastHit[] _bossGroundHits = new RaycastHit[16];
    private readonly RaycastHit[] _bossSweepHits = new RaycastHit[16];

    // Độ lệch Y để tránh Z-fighting và nổi trên thảm hoa/cỏ địa hình 3D
    private const float FloorYOffset = 0.08f;
    private const float FillHeightBias = 0.004f;
    private const float TelegraphSyncToleranceSeconds = 0.05f;

    // Các Mesh dynamic tự động uốn lượn bám sát địa hình mấp mô (chống chìm dưới đất / bay lơ lửng)
    private Mesh conformedOutlineMesh;
    private Mesh conformedFillMesh;
    private Mesh conformedCrackMesh;

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
            playerDodge = player.GetComponentInParent<PlayerDodge>();
            playerCollider = player.GetComponentInChildren<Collider>();
        }

        if (!string.IsNullOrEmpty(walkSpeedParam))
        {
            walkSpeedHash = Animator.StringToHash(walkSpeedParam);
        }

        HideTelegraphVisuals();
        HideGroundCrack();
    }

    private void Start()
    {
        if (snapToTerrain)
        {
            SnapBossToGround(true);
        }
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
            float stepDist = moveSpeed * Time.deltaTime;
            if (snapToTerrain)
            {
                MoveBossOnTerrain(moveDirection, stepDist);
            }
            else
            {
                transform.position += moveDirection * stepDist;
            }
            UpdateWalkAnimator(moveSpeed);
        }
        else
        {
            // 3. Khi đã vào tầm đánh: dừng đi bộ và kiểm tra thời gian hồi chiêu để ra đòn
            UpdateWalkAnimator(0f);
            if (snapToTerrain)
            {
                SnapBossToGround(false);
            }

            if (Time.time >= lastAttackEndTime + attackCooldown)
            {
                TriggerAttack();
            }
        }
    }

    private bool IsSelfOrPlayerCollider(Collider col)
    {
        if (col == null) return true;
        Transform r = col.transform.root;
        if (r == transform.root) return true;
        if (player != null && r == player.root) return true;
        return false;
    }

    private bool TryFindGroundY(Vector3 refPos, out float bestY)
    {
        bestY = refPos.y;
        Vector3 origin = new Vector3(refPos.x, refPos.y + groundCheckUpOffset, refPos.z);
        int count = Physics.RaycastNonAlloc(origin, Vector3.down, _bossGroundHits, groundCheckDistance, ~0, QueryTriggerInteraction.Ignore);
        bool found = false;
        float highestValid = float.NegativeInfinity;
        float maxAllowedY = refPos.y + 1.5f;

        for (int i = 0; i < count; i++)
        {
            RaycastHit h = _bossGroundHits[i];
            if (IsSelfOrPlayerCollider(h.collider)) continue;
            if (h.normal.y < 0.55f) continue;
            if (h.point.y > maxAllowedY) continue;
            if (h.point.y > highestValid)
            {
                highestValid = h.point.y;
                found = true;
            }
        }

        if (found)
        {
            bestY = highestValid;
        }
        return found;
    }

    private bool CheckHorizontalBlocked(Vector3 originPos, Vector3 dirXZ, float dist, out Vector3 hitNormalXZ)
    {
        hitNormalXZ = Vector3.zero;
        Vector3 sphereCenter = originPos + Vector3.up * 0.95f;
        int count = Physics.SphereCastNonAlloc(sphereCenter, bossObstacleRadius, dirXZ, _bossSweepHits, dist, ~0, QueryTriggerInteraction.Ignore);
        float nearestDist = float.MaxValue;
        bool blocked = false;

        for (int i = 0; i < count; i++)
        {
            RaycastHit h = _bossSweepHits[i];
            if (h.distance <= 0.0001f) continue;
            if (IsSelfOrPlayerCollider(h.collider)) continue;
            // Bỏ qua mặt đất thoai thoải (slope đi lên được)
            if (h.normal.y >= 0.55f) continue;

            if (h.distance < nearestDist)
            {
                nearestDist = h.distance;
                hitNormalXZ = new Vector3(h.normal.x, 0f, h.normal.z).normalized;
                blocked = true;
            }
        }
        return blocked;
    }

    private void MoveBossOnTerrain(Vector3 moveDirXZ, float stepDist)
    {
        if (stepDist <= 0.00001f) return;

        Vector3 curPos = transform.position;
        Vector3 desiredDir = moveDirXZ;

        if (CheckHorizontalBlocked(curPos, desiredDir, stepDist + 0.15f, out Vector3 n1))
        {
            Vector3 slidDir = Vector3.ProjectOnPlane(desiredDir, n1);
            slidDir.y = 0f;
            if (slidDir.sqrMagnitude > 0.01f)
            {
                slidDir.Normalize();
                if (!CheckHorizontalBlocked(curPos, slidDir, stepDist + 0.12f, out _))
                {
                    desiredDir = slidDir;
                }
                else
                {
                    desiredDir = Vector3.zero;
                }
            }
            else
            {
                desiredDir = Vector3.zero;
            }
        }

        Vector3 nextPos = curPos + desiredDir * stepDist;
        if (TryFindGroundY(nextPos, out float groundY))
        {
            nextPos.y = Mathf.MoveTowards(curPos.y, groundY, 14f * Time.deltaTime);
        }
        transform.position = nextPos;
    }

    private void SnapBossToGround(bool immediate)
    {
        Vector3 pos = transform.position;
        if (TryFindGroundY(pos, out float groundY))
        {
            pos.y = immediate ? groundY : Mathf.MoveTowards(pos.y, groundY, 14f * Time.deltaTime);
            transform.position = pos;
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
        HideGroundCrack();
        isAttacking = false;
        hitWindowOpened = false;
        hitWindowClosed = false;
        hasProcessedPlayerContactThisWindow = false;
        hasHitPlayerThisWindow = false;
        hasDodgedThisWindow = false;
    }

    private void OnDestroy()
    {
        if (conformedOutlineMesh != null)
        {
            Destroy(conformedOutlineMesh);
        }
        if (conformedFillMesh != null)
        {
            Destroy(conformedFillMesh);
        }
        if (conformedCrackMesh != null)
        {
            Destroy(conformedCrackMesh);
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
        Vector3 bossForwardXZ = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (bossForwardXZ.sqrMagnitude < 0.0001f)
        {
            bossForwardXZ = Vector3.forward;
        }
        bossForwardXZ.Normalize();

        lockedForward = Quaternion.AngleAxis(currentAttack.FacingOffsetDegrees, Vector3.up) * bossForwardXZ;

        // Tâm vùng = vị trí Boss dời ra trước theo ZoneForwardOffset (điểm vũ khí chạm đất)
        lockedCenter = new Vector3(transform.position.x, transform.position.y + FloorYOffset, transform.position.z)
                       + lockedForward * currentAttack.ZoneForwardOffset;
        lockedRadius = currentAttack.Radius;
        lockedArcAngle = currentAttack.ArcAngleDegrees;
        lockedDuration = Mathf.Max(0.01f, currentAttack.TelegraphDuration);
        lockedShape = currentAttack.ShapeType;
        lockedBoxWidth = Mathf.Max(0.1f, currentAttack.BoxWidth);

        // 2. Kích hoạt Animator trigger trước (để ưu tiên chuyển thẳng Walk -> Red Attack thay vì bị kẹt qua Walk -> Idle 0.25s)
        if (animator != null && !string.IsNullOrEmpty(currentAttack.AnimatorTrigger))
        {
            animator.ResetTrigger(currentAttack.AnimatorTrigger);
            animator.SetTrigger(currentAttack.AnimatorTrigger);
        }
        attackTriggerRealtime = Time.realtimeSinceStartup;
        UpdateWalkAnimator(0f);

        // 3. Chuẩn bị Mesh bám địa hình và hiện vùng cảnh báo (chỉ hiện khi showTelegraphVisuals == true)
        if (showTelegraphVisuals)
        {
            EnsureConformedMeshes();

            if (telegraphOutlineMesh != null)
            {
                telegraphOutlineMesh.sharedMesh = conformedOutlineMesh;
                telegraphOutlineMesh.transform.localScale = Vector3.one;
                if (lockedShape == TelegraphShapeType.BoxLine)
                    BuildConformedBoxMesh(telegraphOutlineMesh, conformedOutlineMesh, lockedCenter, lockedForward, lockedRadius, lockedBoxWidth, FloorYOffset);
                else
                    BuildConformedSectorMesh(telegraphOutlineMesh, conformedOutlineMesh, lockedCenter, lockedForward, lockedRadius, lockedArcAngle, FloorYOffset);
                telegraphOutlineMesh.gameObject.SetActive(true);
            }

            if (telegraphFillMesh != null)
            {
                telegraphFillMesh.sharedMesh = conformedFillMesh;
                telegraphFillMesh.transform.localScale = Vector3.one;
                if (lockedShape == TelegraphShapeType.BoxLine)
                    BuildConformedBoxMesh(telegraphFillMesh, conformedFillMesh, lockedCenter, lockedForward, 0.05f, lockedBoxWidth, FloorYOffset + FillHeightBias);
                else
                    BuildConformedSectorMesh(telegraphFillMesh, conformedFillMesh, lockedCenter, lockedForward, 0.05f, lockedArcAngle, FloorYOffset + FillHeightBias);
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

            if (showTelegraphVisuals && telegraphFillMesh != null)
            {
                float curProgressDist = Mathf.Max(0.05f, lockedRadius * progress);
                if (lockedShape == TelegraphShapeType.BoxLine)
                    BuildConformedBoxMesh(telegraphFillMesh, conformedFillMesh, lockedCenter, lockedForward, curProgressDist, lockedBoxWidth, FloorYOffset + FillHeightBias);
                else
                    BuildConformedSectorMesh(telegraphFillMesh, conformedFillMesh, lockedCenter, lockedForward, curProgressDist, lockedArcAngle, FloorYOffset + FillHeightBias);
            }

            yield return null;
        }

        // Khi Fill đã đầy mà chưa nhận OnHitWindowOpen: giữ vùng đầy và chờ tiếp tối đa hitWindowTimeoutSeconds
        if (!hitWindowOpened)
        {
            if (showTelegraphVisuals && telegraphFillMesh != null)
            {
                if (lockedShape == TelegraphShapeType.BoxLine)
                    BuildConformedBoxMesh(telegraphFillMesh, conformedFillMesh, lockedCenter, lockedForward, lockedRadius, lockedBoxWidth, FloorYOffset + FillHeightBias);
                else
                    BuildConformedSectorMesh(telegraphFillMesh, conformedFillMesh, lockedCenter, lockedForward, lockedRadius, lockedArcAngle, FloorYOffset + FillHeightBias);
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
            // Chỉ gây sát thương trong khoảng shockwaveActiveDuration (mặc định 0.35s) lúc sóng bùng nổ, sau đó sóng tan thì không gây sát thương nữa
            if (currentAttack.GroundShockwave && postOpenTimer <= shockwaveActiveDuration)
            {
                CheckShockwaveContact();
            }

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

        // Đếm 1 lần tung đòn khi cửa sổ trúng thực sự mở (đòn bị hủy do thiếu event không tính)
        GetOrCreateStat(currentAttack).totalCasts++;

        // Đo thời gian thực từ lúc SetTrigger đến khi mở cửa sổ trúng
        float actualWindupSeconds = Time.realtimeSinceStartup - attackTriggerRealtime;
        float diff = actualWindupSeconds - lockedDuration;

        Debug.Log($"[BossAttackController] OnHitWindowOpen | Thời gian gồng thực tế: {actualWindupSeconds:F3}s " +
                  $"(Mục tiêu telegraphDuration: {lockedDuration:F3}s | Lệch: {diff:+0.000;-0.000;0.000}s)");

        if (Mathf.Abs(diff) > TelegraphSyncToleranceSeconds)
        {
            Debug.LogWarning($"[BossAttackController] Thời gian gồng thực tế ({actualWindupSeconds:F3}s) lệch quá {TelegraphSyncToleranceSeconds:F2}s " +
                             $"so với telegraphDuration ({lockedDuration:F3}s). Hãy điều chỉnh Speed của state '{GetActiveAttackStateName()}' trong Animator!");
        }

        // Ẩn vùng cảnh báo NGAY khi kiếm bắt đầu có sát thương
        HideTelegraphVisuals();
        ShowGroundCrack();

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

        AttackStatEntry stat = GetOrCreateStat(currentAttack);
        if (hasHitPlayerThisWindow) stat.hits++;
        else if (hasDodgedThisWindow) stat.iframeDodges++;
        else stat.misses++;

        AttackResolved?.Invoke(currentAttack, hasHitPlayerThisWindow);

        string outcomeText = hasHitPlayerThisWindow
            ? "<color=red>TRÚNG</color>"
            : (hasDodgedThisWindow ? "<color=cyan>ĐÃ NÉ (I-FRAME)</color>" : "<color=green>HỤT</color>");

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

        if (lockedShape == TelegraphShapeType.BoxLine)
        {
            // Hình chữ nhật: chiếu vị trí Player lên trục dọc (forward) và trục ngang (right)
            float along = Vector3.Dot(toPlayerXZ, forward);
            float across = Vector3.Dot(toPlayerXZ, Vector3.Cross(Vector3.up, forward));
            return along >= 0f && along <= radius && Mathf.Abs(across) <= lockedBoxWidth * 0.5f;
        }

        if (distanceXZ > radius)
        {
            return false;
        }

        return angleToPlayer <= (arcAngle * 0.5f);
    }

    /// <summary>
    /// Khởi tạo các Mesh dynamic uốn lượn bám địa hình.
    /// </summary>
    private void EnsureConformedMeshes()
    {
        if (conformedOutlineMesh == null)
        {
            conformedOutlineMesh = new Mesh { name = "ConformedOutlineMesh" };
            conformedOutlineMesh.MarkDynamic();
        }
        if (conformedFillMesh == null)
        {
            conformedFillMesh = new Mesh { name = "ConformedFillMesh" };
            conformedFillMesh.MarkDynamic();
        }
        if (conformedCrackMesh == null)
        {
            conformedCrackMesh = new Mesh { name = "ConformedCrackMesh" };
            conformedCrackMesh.MarkDynamic();
        }
    }

    /// <summary>
    /// Dựng lưới hình chữ nhật uốn lượn bám sát địa hình 3D (dùng cho cả BoxLine và GroundShockwave).
    /// Từng đốt dọc theo chiều dài đều raycast tìm mặt đất thật để không bao giờ bị chìm dưới gò đất hay bay lơ lửng.
    /// </summary>
    private void BuildConformedBoxMesh(MeshFilter targetFilter, Mesh mesh, Vector3 center, Vector3 forward, float length, float width, float heightOffset, int segments = 24)
    {
        if (targetFilter == null || mesh == null) return;
        mesh.Clear();
        if (length <= 0.05f || width <= 0.05f) return;

        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        float halfWidth = width * 0.5f;

        int vertCount = (segments + 1) * 2;
        Vector3[] vertices = new Vector3[vertCount];
        Vector3[] normals = new Vector3[vertCount];
        Vector2[] uvs = new Vector2[vertCount];
        int[] triangles = new int[segments * 6];

        Transform tr = targetFilter.transform;

        for (int i = 0; i <= segments; i++)
        {
            float t = (float)i / segments;
            float dist = length * t;
            Vector3 spine = center + forward * dist;

            Vector3 leftWorld = spine - right * halfWidth;
            Vector3 rightWorld = spine + right * halfWidth;

            float groundYLeft = TryFindGroundY(leftWorld, out float yL) ? yL : spine.y;
            float groundYRight = TryFindGroundY(rightWorld, out float yR) ? yR : spine.y;

            leftWorld.y = groundYLeft + heightOffset;
            rightWorld.y = groundYRight + heightOffset;

            int idx = i * 2;
            vertices[idx] = tr.InverseTransformPoint(leftWorld);
            vertices[idx + 1] = tr.InverseTransformPoint(rightWorld);

            normals[idx] = Vector3.up;
            normals[idx + 1] = Vector3.up;

            uvs[idx] = new Vector2(0f, t);
            uvs[idx + 1] = new Vector2(1f, t);

            if (i < segments)
            {
                int tri = i * 6;
                triangles[tri + 0] = idx;
                triangles[tri + 1] = idx + 2;
                triangles[tri + 2] = idx + 1;

                triangles[tri + 3] = idx + 1;
                triangles[tri + 4] = idx + 2;
                triangles[tri + 5] = idx + 3;
            }
        }

        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
    }

    /// <summary>
    /// Dựng lưới hình quạt uốn lượn bám sát địa hình 3D (chia theo vành đai và góc, mỗi đỉnh đều raycast tìm mặt đất).
    /// </summary>
    private void BuildConformedSectorMesh(MeshFilter targetFilter, Mesh mesh, Vector3 center, Vector3 forward, float radius, float arcAngle, float heightOffset, int segments = 16, int rings = 5)
    {
        if (targetFilter == null || mesh == null) return;
        mesh.Clear();
        if (radius <= 0.05f || arcAngle <= 1f) return;

        int vertCount = (rings + 1) * (segments + 1);
        Vector3[] vertices = new Vector3[vertCount];
        Vector3[] normals = new Vector3[vertCount];
        Vector2[] uvs = new Vector2[vertCount];
        int[] triangles = new int[rings * segments * 6];

        float stepAngle = arcAngle / segments;
        float startAngle = -arcAngle * 0.5f;
        Transform tr = targetFilter.transform;

        int vertIdx = 0;
        for (int r = 0; r <= rings; r++)
        {
            float tR = (float)r / rings;
            float curR = radius * tR;

            for (int s = 0; s <= segments; s++)
            {
                float angle = startAngle + s * stepAngle;
                Vector3 dir = Quaternion.AngleAxis(angle, Vector3.up) * forward;
                Vector3 ptWorld = center + dir * curR;

                float groundY = TryFindGroundY(ptWorld, out float y) ? y : center.y;
                ptWorld.y = groundY + heightOffset;

                vertices[vertIdx] = tr.InverseTransformPoint(ptWorld);
                normals[vertIdx] = Vector3.up;
                uvs[vertIdx] = new Vector2(dir.x * 0.5f * tR + 0.5f, dir.z * 0.5f * tR + 0.5f);
                vertIdx++;
            }
        }

        int triIdx = 0;
        int rowStride = segments + 1;
        for (int r = 0; r < rings; r++)
        {
            for (int s = 0; s < segments; s++)
            {
                int c0 = r * rowStride + s;
                int c1 = c0 + 1;
                int n0 = (r + 1) * rowStride + s;
                int n1 = n0 + 1;

                triangles[triIdx + 0] = c0;
                triangles[triIdx + 1] = n0;
                triangles[triIdx + 2] = c1;

                triangles[triIdx + 3] = c1;
                triangles[triIdx + 4] = n0;
                triangles[triIdx + 5] = n1;
                triIdx += 6;
            }
        }

        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
    }

    private AttackStatEntry GetOrCreateStat(BossAttackData data)
    {
        string name = data != null ? data.AttackName : "Unknown";
        for (int i = 0; i < attackStats.Count; i++)
        {
            if (attackStats[i].attackName == name) return attackStats[i];
        }
        AttackStatEntry entry = new AttackStatEntry { attackName = name };
        attackStats.Add(entry);
        return entry;
    }

    /// <summary>Xóa thống kê đòn đánh (gọi khi bắt đầu trận mới / Replay).</summary>
    public void ResetAttackStatistics()
    {
        attackStats.Clear();
    }

    /// <summary>
    /// Trả về tổng hợp thống kê: tổng số đòn, tổng trúng, tổng né I-frame và chuỗi theo từng đòn,
    /// ví dụ "HorizontalSlash(T:4,H:1,D:2,M:1);StrongRedAttack(T:3,H:2,D:1,M:0)".
    /// Dấu ';' ngăn cách đòn nên không đụng dấu ',' của CSV khi bọc trong ngoặc kép.
    /// </summary>
    public void GetAttackStatisticsSummary(out int totalCasts, out int totalHits, out int totalIframeDodges, out string breakdown)
    {
        totalCasts = 0;
        totalHits = 0;
        totalIframeDodges = 0;
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        for (int i = 0; i < attackStats.Count; i++)
        {
            AttackStatEntry s = attackStats[i];
            totalCasts += s.totalCasts;
            totalHits += s.hits;
            totalIframeDodges += s.iframeDodges;
            if (i > 0) sb.Append(';');
            sb.Append(s.attackName.Replace(" ", "")).Append("(T:").Append(s.totalCasts)
              .Append(",H:").Append(s.hits).Append(",D:").Append(s.iframeDodges)
              .Append(",M:").Append(s.misses).Append(')');
        }
        breakdown = sb.ToString();
    }

    /// <summary>BattleLoopManager gọi để đồng bộ bật/tắt vùng cảnh báo theo Build A/B.</summary>
    public void SetShowTelegraphVisuals(bool show)
    {
        showTelegraphVisuals = show;
        if (!show) HideTelegraphVisuals();
    }

    /// <summary>
    /// Đòn xung kích: mỗi khung hình trong cửa sổ trúng, nếu Player đứng trong vùng đã chốt và không I-frame
    /// thì tính trúng (HandlePlayerHit tự chặn lần thứ 2). Đang I-frame thì chỉ ghi nhận "né", chưa tiêu thụ lượt trúng.
    /// </summary>
    private void CheckShockwaveContact()
    {
        if (hasProcessedPlayerContactThisWindow || player == null || playerCollider == null) return;
        if (playerHealth != null && playerHealth.IsDead) return;

        if (!IsInsideLockedZone(lockedCenter, lockedForward, lockedRadius, lockedArcAngle, out _, out _)) return;

        if (playerDodge != null && playerDodge.IsInvulnerable)
        {
            hasDodgedThisWindow = true;
            return;
        }

        HandlePlayerHit(playerCollider);
    }

    private static readonly int BaseColorPropId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorPropId = Shader.PropertyToID("_Color");
    private MaterialPropertyBlock crackPropertyBlock;
    private Renderer groundCrackRenderer;
    private Color groundCrackBaseColor = Color.white;
    private Coroutine groundCrackCoroutine;

    /// <summary>
    /// Kích hoạt hiệu ứng vệt nứt mặt đất: tự động kéo dãn từ tâm ra đủ 10m (hoặc lockedRadius),
    /// giữ nguyên trong cửa sổ gây sát thương, sau đó mờ dần (Fade out alpha) rồi biến mất.
    /// </summary>
    private void ShowGroundCrack()
    {
        if (groundCrackMesh == null || currentAttack == null || !currentAttack.GroundShockwave) return;

        if (groundCrackCoroutine != null)
        {
            StopCoroutine(groundCrackCoroutine);
            groundCrackCoroutine = null;
        }

        float targetLength = lockedRadius;
        float targetWidth = lockedShape == TelegraphShapeType.BoxLine
            ? lockedBoxWidth
            : Mathf.Max(1f, 2f * lockedRadius * Mathf.Sin(lockedArcAngle * 0.5f * Mathf.Deg2Rad));

        groundCrackCoroutine = StartCoroutine(AnimateGroundCrackRoutine(lockedCenter, lockedForward, targetLength, targetWidth));
    }

    private IEnumerator AnimateGroundCrackRoutine(Vector3 center, Vector3 forward, float targetLength, float targetWidth)
    {
        EnsureConformedMeshes();

        groundCrackMesh.sharedMesh = conformedCrackMesh;
        groundCrackMesh.transform.localScale = Vector3.one;

        if (groundCrackRenderer == null)
        {
            groundCrackRenderer = groundCrackMesh.GetComponent<Renderer>();
            if (groundCrackRenderer != null && groundCrackRenderer.sharedMaterial != null)
            {
                if (groundCrackRenderer.sharedMaterial.HasProperty(BaseColorPropId))
                    groundCrackBaseColor = groundCrackRenderer.sharedMaterial.GetColor(BaseColorPropId);
                else if (groundCrackRenderer.sharedMaterial.HasProperty(ColorPropId))
                    groundCrackBaseColor = groundCrackRenderer.sharedMaterial.GetColor(ColorPropId);
            }
        }

        if (crackPropertyBlock == null)
        {
            crackPropertyBlock = new MaterialPropertyBlock();
        }

        SetCrackAlpha(1f);
        groundCrackMesh.gameObject.SetActive(true);

        // Pha 1: Kéo dãn phóng ra từ tâm chạm kiếm (0 -> 10m) trong 0.18 giây bám sát địa hình
        float stretchDuration = 0.18f;
        float stretchTimer = 0f;
        while (stretchTimer < stretchDuration)
        {
            stretchTimer += Time.deltaTime;
            float t = Mathf.Clamp01(stretchTimer / stretchDuration);
            // Ease-Out: phóng mạnh mẽ lúc đầu rồi hãm nhẹ ở đỉnh đòn
            float eased = 1f - (1f - t) * (1f - t);
            float curLen = Mathf.Max(0.1f, targetLength * eased);
            float curWidth = Mathf.Max(0.1f, targetWidth * eased);

            BuildConformedBoxMesh(groundCrackMesh, conformedCrackMesh, center, forward, curLen, curWidth, FloorYOffset + FillHeightBias * 2f);
            yield return null;
        }

        BuildConformedBoxMesh(groundCrackMesh, conformedCrackMesh, center, forward, targetLength, targetWidth, FloorYOffset + FillHeightBias * 2f);

        // Pha 2: Giữ nguyên trong suốt thời gian gây sát thương
        while (!hitWindowClosed && isAttacking)
        {
            yield return null;
        }

        // Pha 3: Mờ dần (Fade Out) từ 1 về 0 trong 0.35 giây rồi biến mất
        float fadeDuration = 0.35f;
        float fadeTimer = 0f;
        while (fadeTimer < fadeDuration)
        {
            fadeTimer += Time.deltaTime;
            float alpha = Mathf.Clamp01(1f - (fadeTimer / fadeDuration));
            SetCrackAlpha(alpha);
            yield return null;
        }

        SetCrackAlpha(0f);
        groundCrackMesh.gameObject.SetActive(false);
        groundCrackCoroutine = null;
    }

    private void SetCrackAlpha(float alpha)
    {
        if (groundCrackRenderer == null || crackPropertyBlock == null) return;
        groundCrackRenderer.GetPropertyBlock(crackPropertyBlock);
        Color c = groundCrackBaseColor;
        c.a = alpha;
        crackPropertyBlock.SetColor(BaseColorPropId, c);
        crackPropertyBlock.SetColor(ColorPropId, c);
        groundCrackRenderer.SetPropertyBlock(crackPropertyBlock);
    }

    private void HideGroundCrack()
    {
        if (groundCrackCoroutine != null)
        {
            StopCoroutine(groundCrackCoroutine);
            groundCrackCoroutine = null;
        }

        if (groundCrackMesh != null)
        {
            groundCrackMesh.gameObject.SetActive(false);
        }
    }

    private void HideTelegraphVisuals()    {
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

        if (currentAttack.ShapeType == TelegraphShapeType.BoxLine)
        {
            Vector3 right = Vector3.Cross(Vector3.up, forward) * (currentAttack.BoxWidth * 0.5f);
            Vector3 far = center + forward * radius;
            Gizmos.DrawLine(center + right, far + right);
            Gizmos.DrawLine(far + right, far - right);
            Gizmos.DrawLine(far - right, center - right);
            Gizmos.DrawLine(center - right, center + right);
            return;
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
