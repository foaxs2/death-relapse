using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Vệt kiếm khí trắng bám theo quỹ đạo thật của lưỡi kiếm.
/// Mỗi frame trong lúc chém, ghi lại vị trí chuôi/mũi kiếm (kéo dài thêm một đoạn) rồi dựng dải mesh nối các mẫu,
/// nên kiếm chém ngang, xéo hay dọc thì vệt chém cũng đi đúng theo đó. Vệt mờ dần và thu hẹp rồi biến mất.
/// Hitbox là hình trụ quét dọc đoạn kiếm đã kéo dài, cùng đoạn với phần hiển thị.
/// </summary>
public class SwordSlashTrail : MonoBehaviour
{
    /// <summary>
    /// Thông số của vệt chém. Mỗi nhát trong combo có một bộ riêng.
    /// </summary>
    [Serializable]
    public class Settings
    {
        [Tooltip("Sát thương của vệt kiếm khí (độc lập với sát thương kiếm)")]
        [Min(0f)] public float damage = 40f;

        [Tooltip("Độ dài kéo thêm phía trước mũi kiếm (mét). Càng lớn thì tầm chém càng xa")]
        [Min(0f)] public float extraReach = 1.2f;

        [Tooltip("Vệt bắt đầu từ vị trí nào trên thân kiếm: 0 = sát chuôi, 1 = sát mũi. Nhỏ thì vệt rộng bản, lớn thì vệt mảnh")]
        [Range(0f, 0.95f)] public float innerFraction = 0.3f;

        [Tooltip("Bán kính hình trụ gây sát thương quanh đoạn kiếm đã kéo dài (mét)")]
        [Min(0.05f)] public float hitRadius = 0.35f;

        [Tooltip("Thời gian vệt tồn tại trước khi biến mất hoàn toàn (giây)")]
        [Min(0.05f)] public float lifetime = 0.25f;
    }

    private struct Sample
    {
        public Vector3 inner;
        public Vector3 outer;
        public float time;
        public bool breakBefore; // đánh dấu mẫu đầu của một nhát để không nối với nhát trước
    }

    [Header("References")]
    [Tooltip("Gốc lưỡi kiếm (BladeBase)")]
    [SerializeField] private Transform bladeBase;

    [Tooltip("Mũi kiếm (BladeTip)")]
    [SerializeField] private Transform bladeTip;

    [Tooltip("Material trắng, shader 'Universal Render Pipeline/Particles/Unlit' (Surface Type Transparent) để đọc được màu đỉnh làm mờ dần")]
    [SerializeField] private Material material;

    [Header("Detection")]
    [Tooltip("Layer của Boss. Để Nothing thì quét mọi layer nhưng chỉ nhận collider có BossHealth")]
    [SerializeField] private LayerMask targetMask;

    [Tooltip("Số bước nội suy giữa 2 frame để kiếm vung nhanh không xuyên qua Boss")]
    [Min(1)]
    [SerializeField] private int substeps = 4;

    [Tooltip("Số mẫu tối đa lưu cùng lúc")]
    [Min(8)]
    [SerializeField] private int maxSamples = 64;

    private readonly List<Sample> samples = new List<Sample>();
    private readonly List<Vector3> vertices = new List<Vector3>();
    private readonly List<Color32> colors = new List<Color32>();
    private readonly List<int> triangles = new List<int>();
    private readonly Collider[] hitBuffer = new Collider[8];

    private Mesh mesh;
    private Settings settings;
    private Action<BossHealth, float> onHitBoss;
    private bool isRecording;
    private bool hasHit;
    private bool firstSampleOfSwing;
    private Vector3 prevBase;
    private Vector3 prevOuter;

    private void Awake()
    {
        MeshFilter filter = GetComponent<MeshFilter>();
        if (filter == null) filter = gameObject.AddComponent<MeshFilter>();

        MeshRenderer meshRenderer = GetComponent<MeshRenderer>();
        if (meshRenderer == null) meshRenderer = gameObject.AddComponent<MeshRenderer>();

        meshRenderer.sharedMaterial = material;
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;

        mesh = new Mesh { name = "SwordSlashTrailMesh" };
        mesh.MarkDynamic();
        filter.sharedMesh = mesh;
    }

    /// <summary>
    /// Bắt đầu ghi vệt chém và bật hitbox kéo dài (gọi khi cửa sổ hitbox kiếm mở).
    /// </summary>
    public void BeginRecording(Settings newSettings, Action<BossHealth, float> hitCallback)
    {
        if (bladeBase == null || bladeTip == null)
        {
            Debug.LogError("[SwordSlashTrail] Chưa gán 'bladeBase' hoặc 'bladeTip' trên " + gameObject.name);
            return;
        }

        settings = newSettings;
        onHitBoss = hitCallback;
        isRecording = true;
        hasHit = false;
        firstSampleOfSwing = true;

        GetPose(out prevBase, out _, out prevOuter);
    }

    /// <summary>
    /// Ngừng ghi mẫu mới (gọi khi cửa sổ hitbox đóng). Phần vệt đã ghi sẽ mờ dần rồi biến mất.
    /// </summary>
    public void EndRecording()
    {
        isRecording = false;
        onHitBoss = null;
    }

    private void GetPose(out Vector3 basePos, out Vector3 inner, out Vector3 outer)
    {
        basePos = bladeBase.position;
        Vector3 tipPos = bladeTip.position;
        Vector3 blade = tipPos - basePos;
        float extra = settings != null ? settings.extraReach : 0f;
        float inFrac = settings != null ? settings.innerFraction : 0f;

        inner = basePos + blade * inFrac;
        outer = tipPos + blade.normalized * extra;
    }

    // Dùng LateUpdate vì Animator cập nhật vị trí xương trước LateUpdate
    private void LateUpdate()
    {
        float now = Time.time;

        if (isRecording)
        {
            GetPose(out Vector3 currentBase, out Vector3 inner, out Vector3 outer);

            Sample sample = new Sample { inner = inner, outer = outer, time = now, breakBefore = firstSampleOfSwing };
            samples.Add(sample);
            firstSampleOfSwing = false;

            if (!hasHit)
            {
                CheckHit(currentBase, outer);
            }

            prevBase = currentBase;
            prevOuter = outer;
        }

        // Bỏ các mẫu đã quá hạn (tối đa lifetime) hoặc vượt số lượng cho phép
        float life = settings != null ? settings.lifetime : 0.25f;
        int remove = 0;
        while (remove < samples.Count && (now - samples[remove].time > life || samples.Count - remove > maxSamples))
        {
            remove++;
        }
        if (remove > 0) samples.RemoveRange(0, remove);

        RebuildMesh(now, life);
    }

    private void CheckHit(Vector3 currentBase, Vector3 currentOuter)
    {
        int steps = Mathf.Max(1, substeps);
        int mask = targetMask.value != 0 ? targetMask.value : Physics.DefaultRaycastLayers;

        for (int i = 1; i <= steps; i++)
        {
            float t = (float)i / steps;
            Vector3 stepBase = Vector3.Lerp(prevBase, currentBase, t);
            Vector3 stepOuter = Vector3.Lerp(prevOuter, currentOuter, t);

            int count = Physics.OverlapCapsuleNonAlloc(stepBase, stepOuter, settings.hitRadius, hitBuffer, mask,
                QueryTriggerInteraction.Ignore);

            for (int j = 0; j < count; j++)
            {
                if (hitBuffer[j] == null || hitBuffer[j].transform.root == transform.root) continue;

                BossHealth boss = hitBuffer[j].GetComponentInParent<BossHealth>();
                if (boss == null || boss.IsDead) continue;

                hasHit = true;
                onHitBoss?.Invoke(boss, settings.damage);
                return;
            }
        }
    }

    private void RebuildMesh(float now, float life)
    {
        vertices.Clear();
        colors.Clear();
        triangles.Clear();

        for (int i = 0; i < samples.Count; i++)
        {
            Sample s = samples[i];
            float age01 = Mathf.Clamp01((now - s.time) / life);

            // Mẫu càng cũ càng mờ và thu hẹp về phía mũi, tạo đuôi vệt nhọn dần
            Vector3 inner = Vector3.Lerp(s.inner, s.outer, age01);
            byte alpha = (byte)(255f * (1f - age01));

            vertices.Add(transform.InverseTransformPoint(s.outer));
            vertices.Add(transform.InverseTransformPoint(inner));
            colors.Add(new Color32(255, 255, 255, alpha));
            colors.Add(new Color32(255, 255, 255, alpha));

            if (i > 0 && !s.breakBefore)
            {
                int o0 = (i - 1) * 2, i0 = o0 + 1, o1 = i * 2, i1 = o1 + 1;
                // Hai mặt để nhìn từ trên hay dưới đều thấy
                triangles.Add(o0); triangles.Add(o1); triangles.Add(i0);
                triangles.Add(i0); triangles.Add(o1); triangles.Add(i1);
                triangles.Add(o0); triangles.Add(i0); triangles.Add(o1);
                triangles.Add(i0); triangles.Add(i1); triangles.Add(o1);
            }
        }

        mesh.Clear();
        if (vertices.Count == 0) return;

        mesh.SetVertices(vertices);
        mesh.SetColors(colors);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
    }

    private void OnDrawGizmosSelected()
    {
        if (bladeBase == null || bladeTip == null) return;

        // Vẽ đoạn kiếm đã kéo dài (vùng quét sát thương) để chỉnh extraReach
        Vector3 blade = bladeTip.position - bladeBase.position;
        float extra = settings != null ? settings.extraReach : 1.2f;
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(bladeBase.position, bladeTip.position + blade.normalized * extra);
    }
}
