using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Điều khiển đòn đánh báo trước (telegraph) hình quạt của Boss.
/// Vùng đỏ cảnh báo trên sàn và vùng tính toán va chạm (hitbox) dùng chung
/// chính xác một bộ dữ liệu hình học đã được "chốt" tại thời điểm bắt đầu đòn.
/// </summary>
public class BossAttackController : MonoBehaviour
{
    [Header("Attack Configuration")]
    [Tooltip("Dữ liệu cấu hình đòn đánh hiện tại (ScriptableObject)")]
    [SerializeField] private BossAttackData currentAttack;

    [Header("References")]
    [Tooltip("Animator của Boss để kích hoạt animation chém")]
    [SerializeField] private Animator animator;

    [Tooltip("Transform của Player để kiểm tra va chạm trúng/hụt")]
    [SerializeField] private Transform player;

    [Tooltip("MeshFilter của GameObject hiển thị quạt đỏ điền dần (Fill)")]
    [SerializeField] private MeshFilter telegraphFillMesh;

    [Tooltip("MeshFilter của GameObject hiển thị viền/khung quạt tối đa (Outline)")]
    [SerializeField] private MeshFilter telegraphOutlineMesh;

    [Header("Animator Scaling")]
    [Tooltip("Nếu bật, tốc độ Animator sẽ được co giãn để khớp đúng thời lượng telegraphDuration")]
    [SerializeField] private bool scaleAnimatorToTelegraph = false;

    /// <summary>
    /// Sự kiện C# phát ra khi hết thời gian báo đòn và hoàn tất kiểm tra va chạm.
    /// Tham số: BossAttackData của đòn, bool isHit (true nếu trúng người chơi).
    /// </summary>
    public event Action<BossAttackData, bool> AttackResolved;

    // Trạng thái đòn đánh
    private bool isAttacking = false;
    private Coroutine attackCoroutine;

    // Dữ liệu hình học được chốt MỘT LẦN khi bắt đầu đòn (Snap-shot)
    // Việc chốt này đảm bảo vùng vẽ và vùng hitbox luôn khớp 100%, kể cả khi boss xoay hay di chuyển sau đó
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

    public bool IsAttacking => isAttacking;

    private void Awake()
    {
        // Tự động tìm Animator trong chính Boss hoặc con nếu chưa được kéo thả
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        // Tắt hiển thị vùng báo đòn lúc khởi đầu
        HideTelegraphVisuals();
    }

    private void Update()
    {
        // Phím thử nghiệm: Số 1 trên bàn phím (Input System mới)
        // Bỏ qua nếu đang trong đòn đánh hoặc không có dữ liệu đòn
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
        // Khi Boss bị tắt, hủy đòn đang chạy để tránh kẹt trạng thái hoặc rò rỉ Coroutine
        if (attackCoroutine != null)
        {
            StopCoroutine(attackCoroutine);
            attackCoroutine = null;
        }

        HideTelegraphVisuals();
        isAttacking = false;

        if (animator != null)
        {
            animator.speed = 1f;
        }
    }

    private void OnDestroy()
    {
        // Giải phóng Mesh sinh động trong bộ nhớ khi đối tượng bị hủy
        if (sharedSectorMesh != null)
        {
            Destroy(sharedSectorMesh);
        }
    }

    /// <summary>
    /// Kích hoạt chu kỳ đòn đánh báo trước dựa theo currentAttack.
    /// Có thể gọi qua phím số 1 hoặc từ các script AI sau này.
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
    /// Luồng chính xử lý telegraph: Chốt dữ liệu -> Dựng mesh -> Điền dần -> Kiểm tra trúng/hụt.
    /// </summary>
    private IEnumerator AttackRoutine()
    {
        isAttacking = true;

        // 1. CHỐT MỘT LẦN toàn bộ thông số hình học và thời gian từ ScriptableObject
        lockedCenter = new Vector3(transform.position.x, transform.position.y + FloorYOffset, transform.position.z);

        // Hướng mặt phẳng XZ (bỏ trục Y để cung quạt luôn phẳng trên mặt sàn)
        Vector3 bossForwardXZ = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (bossForwardXZ.sqrMagnitude < 0.0001f)
        {
            bossForwardXZ = Vector3.forward;
        }
        bossForwardXZ.Normalize();

        // Áp dụng góc lệch tâm đòn đánh (offset dương = xoay theo chiều kim đồng hồ)
        lockedForward = Quaternion.AngleAxis(currentAttack.FacingOffsetDegrees, Vector3.up) * bossForwardXZ;
        lockedRadius = currentAttack.Radius;
        lockedArcAngle = currentAttack.ArcAngleDegrees;
        lockedDuration = Mathf.Max(0.01f, currentAttack.TelegraphDuration);

        // 2. Kích hoạt Animator trigger
        if (animator != null && !string.IsNullOrEmpty(currentAttack.AnimatorTrigger))
        {
            animator.SetTrigger(currentAttack.AnimatorTrigger);
        }

        // 3. Điều chỉnh tốc độ Animator nếu được bật để đòn chém vung trúng đích đúng lúc telegraph kết thúc
        if (scaleAnimatorToTelegraph && animator != null && currentAttack.AnimationSecondsToImpact > 0f)
        {
            animator.speed = currentAttack.AnimationSecondsToImpact / lockedDuration;
        }

        // 4. Chuẩn bị Mesh hình quạt đơn vị (bán kính 1m, 3 độ mỗi đoạn)
        EnsureSectorMesh(lockedArcAngle);

        Quaternion attackRotation = Quaternion.LookRotation(lockedForward, Vector3.up);

        // Đặt vị trí, góc xoay và bật Outline (vùng tối đa của đòn)
        if (telegraphOutlineMesh != null)
        {
            telegraphOutlineMesh.transform.position = lockedCenter;
            telegraphOutlineMesh.transform.rotation = attackRotation;
            telegraphOutlineMesh.transform.localScale = new Vector3(lockedRadius, 1f, lockedRadius);
            telegraphOutlineMesh.gameObject.SetActive(true);
        }

        // Đặt vị trí, góc xoay và khởi tạo vùng Fill phình từ 0
        if (telegraphFillMesh != null)
        {
            // Nâng nhẹ Fill trên trục Y để nằm đè sắc nét lên Outline mà không bị Z-fighting
            telegraphFillMesh.transform.position = lockedCenter + Vector3.up * FillHeightBias;
            telegraphFillMesh.transform.rotation = attackRotation;
            telegraphFillMesh.transform.localScale = new Vector3(0f, 1f, 0f);
            telegraphFillMesh.gameObject.SetActive(true);
        }

        Debug.Log($"[BossAttackController] BẮT ĐẦU đòn '{currentAttack.AttackName}' | " +
                  $"Thời lượng: {lockedDuration:F2}s | Bán kính: {lockedRadius:F2}m | " +
                  $"Góc quét: {lockedArcAngle:F1}° | Offset: {currentAttack.FacingOffsetDegrees:F1}°");

        // 5. Tiến trình điền đầy (Fill) dùng Time.deltaTime làm nguồn đếm thời gian thực tế
        float elapsedTime = 0f;
        while (elapsedTime < lockedDuration)
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

        // Đảm bảo ở frame cuối Fill đạt đúng 100% bán kính
        if (telegraphFillMesh != null)
        {
            telegraphFillMesh.transform.localScale = new Vector3(lockedRadius, 1f, lockedRadius);
        }

        // 6. KIỂM TRA TRÚNG / HỤT bằng đúng các dữ liệu hình học đã chốt (không dùng Collider/Physics)
        bool isHit = CheckHitResult(lockedCenter, lockedForward, lockedRadius, lockedArcAngle);

        // 7. Phát sự kiện C# và log thời gian thực tế đo được
        AttackResolved?.Invoke(currentAttack, isHit);
        Debug.Log($"[BossAttackController] KẾT THÚC đòn '{currentAttack.AttackName}' | " +
                  $"Kết quả: {(isHit ? "<color=red>TRÚNG</color>" : "<color=green>HỤT</color>")} | " +
                  $"Thời gian thực tế: {elapsedTime:F3}s (Cấu hình: {lockedDuration:F2}s)");

        // 8. Dọn dẹp: ẩn vùng đỏ, khôi phục tốc độ Animator, mở khóa đòn
        HideTelegraphVisuals();

        if (animator != null)
        {
            animator.speed = 1f;
        }

        isAttacking = false;
        attackCoroutine = null;
    }

    /// <summary>
    /// Kiểm tra va chạm thuần túy theo hình học 2D trên mặt phẳng XZ:
    /// 1. Khoảng cách XZ từ Player tới Center <= Radius
    /// 2. Góc lệch giữa hướng chốt và hướng tới Player <= ArcAngle / 2
    /// </summary>
    private bool CheckHitResult(Vector3 center, Vector3 forward, float radius, float arcAngle)
    {
        if (player == null)
        {
            Debug.LogWarning("[BossAttackController] Player Transform chưa được gán, không thể tính va chạm!");
            return false;
        }

        // 1. Kiểm tra khoảng cách trên mặt phẳng XZ
        Vector2 playerXZ = new Vector2(player.position.x, player.position.z);
        Vector2 centerXZ = new Vector2(center.x, center.z);
        float distanceXZ = Vector2.Distance(playerXZ, centerXZ);

        if (distanceXZ > radius)
        {
            return false;
        }

        // 2. Kiểm tra góc mở hình quạt
        Vector3 toPlayer = player.position - center;
        Vector3 toPlayerXZ = Vector3.ProjectOnPlane(toPlayer, Vector3.up);

        // Nếu Player đứng sát rạt tâm đòn đánh (khoảng cách cực nhỏ) thì coi như nằm trọn trong góc
        if (toPlayerXZ.sqrMagnitude < 0.0001f)
        {
            return true;
        }

        float angleToPlayer = Vector3.Angle(forward, toPlayerXZ.normalized);
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

        // Gán mesh chung này vào cả 2 MeshFilter
        if (telegraphFillMesh != null)
        {
            telegraphFillMesh.mesh = sharedSectorMesh;
        }
        if (telegraphOutlineMesh != null)
        {
            telegraphOutlineMesh.mesh = sharedSectorMesh;
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
