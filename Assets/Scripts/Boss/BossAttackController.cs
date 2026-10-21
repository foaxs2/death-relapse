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
    [Tooltip("Tên State đòn đánh trong Animator Controller (dùng để chờ kết thúc đòn)")]
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

    // Trạng thái đòn đánh
    private bool isAttacking = false;
    private bool hitWindowOpened = false;
    private bool hitWindowClosed = false;
    private bool hasHitPlayerThisWindow = false;
    private float attackTriggerRealtime = 0f;
    private Coroutine attackCoroutine;

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

        HideTelegraphVisuals();
    }

    private void Update()
    {
        // Phím thử nghiệm: Số 1 trên bàn phím (Input System mới)
        if (Keyboard.current != null && Keyboard.current.digit1Key.wasPressedThisFrame)
        {
            if (!isAttacking)
            {
                TriggerAttack();
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

        HideTelegraphVisuals();
        isAttacking = false;
        hitWindowOpened = false;
        hitWindowClosed = false;
        hasHitPlayerThisWindow = false;
    }

    private void OnDestroy()
    {
        if (sharedSectorMesh != null)
        {
            Destroy(sharedSectorMesh);
        }
    }

    /// <summary>
    /// Kích hoạt chu kỳ đòn đánh báo trước dựa theo currentAttack.
    /// </summary>
    public void TriggerAttack()
    {
        if (isAttacking) return;

        if (currentAttack == null)
        {
            Debug.LogWarning("[BossAttackController] Chưa gán 'currentAttack' (BossAttackData) trong Inspector!");
            return;
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
        hasHitPlayerThisWindow = false;

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

        // 2. Kích hoạt Animator trigger và ghi mốc thời gian thực
        if (animator != null && !string.IsNullOrEmpty(currentAttack.AnimatorTrigger))
        {
            animator.ResetTrigger(currentAttack.AnimatorTrigger);
            animator.SetTrigger(currentAttack.AnimatorTrigger);
        }
        attackTriggerRealtime = Time.realtimeSinceStartup;

        // 3. Chuẩn bị Mesh hình quạt đơn vị và hiện vùng quạt cảnh báo
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

        Debug.Log($"[BossAttackController] BẮT ĐẦU đòn '{currentAttack.AttackName}' | " +
                  $"Telegraph: {lockedDuration:F2}s | Bán kính: {lockedRadius:F2}m | " +
                  $"Góc quét: {lockedArcAngle:F1}° | Offset: {currentAttack.FacingOffsetDegrees:F1}°");

        // 4. Tiến trình điền đầy (Fill) theo telegraphDuration bằng Time.deltaTime
        // Nếu nhận OnHitWindowOpen sớm hơn dự kiến thì NotifyHitWindowOpen sẽ tự ẩn vùng cảnh báo ngay
        float elapsedTime = 0f;
        while (elapsedTime < lockedDuration && !hitWindowOpened)
        {
            elapsedTime += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsedTime / lockedDuration);
            float currentFillRadius = lockedRadius * progress;

            if (telegraphFillMesh != null)
            {
                telegraphFillMesh.transform.localScale = new Vector3(currentFillRadius, 1f, currentFillRadius);
            }

            yield return null;
        }

        // Khi Fill đã đầy mà chưa nhận OnHitWindowOpen: giữ vùng đầy và chờ tiếp tối đa hitWindowTimeoutSeconds
        if (!hitWindowOpened)
        {
            if (telegraphFillMesh != null)
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
                HideTelegraphVisuals();
                isAttacking = false;
                attackCoroutine = null;
                yield break;
            }
        }

        // 5. Chờ đóng cửa sổ trúng (OnHitWindowClose) và chờ Animator rời state attackStateName
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

        if (animator != null && !string.IsNullOrEmpty(attackStateName))
        {
            while (postOpenTimer < maxPostOpenWait)
            {
                AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);
                bool inAttackState = stateInfo.IsName(attackStateName);
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
        attackCoroutine = null;
    }

    /// <summary>
    /// Được gọi từ BossAnimationEventRelay khi Animation Event 'OnHitWindowOpen' kích hoạt.
    /// </summary>
    public void NotifyHitWindowOpen()
    {
        if (!isAttacking || hitWindowOpened) return;

        hitWindowOpened = true;
        hasHitPlayerThisWindow = false;

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

        Debug.Log($"[BossAttackController] OnHitWindowClose -> KẾT THÚC pha chém '{currentAttack.AttackName}' | " +
                  $"Kết quả: {(hasHitPlayerThisWindow ? "<color=red>TRÚNG</color>" : "<color=green>HỤT</color>")}");
    }

    /// <summary>
    /// Xử lý khi SwordHitbox chạm trúng collider của Player (chỉ gọi tối đa 1 lần mỗi cửa sổ).
    /// </summary>
    private void HandlePlayerHit(Collider hitCollider)
    {
        if (hasHitPlayerThisWindow) return;
        hasHitPlayerThisWindow = true;

        bool insideZone = IsInsideLockedZone(
            lockedCenter,
            lockedForward,
            lockedRadius,
            lockedArcAngle,
            out float distanceXZ,
            out float angleToPlayer
        );

        Debug.Log($"[BossAttackController] Kiếm chạm '{hitCollider.name}' | " +
                  $"Trong vùng cảnh báo: {(insideZone ? "CÓ" : "KHÔNG")} | " +
                  $"Khoảng cách XZ: {distanceXZ:F2}m/{lockedRadius:F2}m | Góc lệch: {angleToPlayer:F1}°/{lockedArcAngle * 0.5f:F1}°");

        if (!insideZone)
        {
            Debug.LogWarning($"[VALIDITY] Trúng NGOÀI vùng cảnh báo! Khoảng cách XZ = {distanceXZ:F2}m (bán kính = {lockedRadius:F2}m), " +
                             $"Góc lệch = {angleToPlayer:F1}° (nửa góc quạt = {lockedArcAngle * 0.5f:F1}°). " +
                             "Cần kiểm tra lại bán kính/góc quạt của BossAttackData cho khớp với đường kiếm quét.");
        }

        PlayerHitByAttack?.Invoke(currentAttack, insideZone);
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
