using UnityEngine;

/// <summary>
/// Điều khiển Camera bám theo Player theo góc nhìn trên xuống nghiêng kiểu Hades (Isometric / 2.5D).
/// Cho phép chỉnh góc nghiêng (Pitch), góc xoay chéo (Yaw), khoảng cách và FOV trực tiếp trong Inspector.
/// </summary>
public class CameraFollow : MonoBehaviour
{
    [Header("Target Tracking")]
    [Tooltip("Transform của Player mục tiêu cần camera bám theo (kéo thả từ Hierarchy vào đây, không dùng Find)")]
    [SerializeField] private Transform target;

    [Tooltip("Độ cao điểm ngắm tính từ chân Player (giúp nhân vật nằm cân đối ở giữa khung hình)")]
    [SerializeField] private float targetHeightOffset = 1f;

    [Header("Hades Camera Angle")]
    [Tooltip("Góc nhìn chúc xuống (độ). Kiểu Hades thường từ 40° đến 45°")]
    [Range(20f, 80f)]
    [SerializeField] private float pitchAngle = 42f;

    [Tooltip("Góc xoay ngang (độ). 45° hoặc -45° tạo góc nhìn chéo isometric giống Hades; 0° nhìn thẳng trục Z")]
    [Range(-180f, 180f)]
    [SerializeField] private float yawAngle = 45f;

    [Tooltip("Khoảng cách từ Camera đến Player (m). Kết hợp FOV nhỏ để giảm méo hình phối cảnh")]
    [Min(5f)]
    [SerializeField] private float distance = 20f;

    [Tooltip("Góc mở ống kính (Field of View). Kiểu Hades dùng FOV hẹp (khoảng 30°–40°) để tạo cảm giác giả isometric")]
    [Range(20f, 70f)]
    [SerializeField] private float fieldOfView = 35f;

    [Header("Smooth Settings")]
    [Tooltip("Thời gian làm mượt chuyển động của Camera (giây). Giá trị càng nhỏ camera bám càng sát")]
    [Min(0f)]
    [SerializeField] private float smoothTime = 0.15f;

    // Cache Camera component để chỉnh FOV không cần GetComponent lặp lại
    private Camera cachedCamera;

    // Vận tốc tức thời nội bộ của Camera được hàm Vector3.SmoothDamp tự tính toán và cập nhật qua từng frame
    private Vector3 currentVelocity;

    private void Awake()
    {
        cachedCamera = GetComponent<Camera>();
    }

    private void Start()
    {
        // Đưa Camera về đúng góc nhìn và vị trí chuẩn ngay khi vào game để tránh giật khung hình đầu tiên
        ApplyCameraSettingsImmediate();
    }

    private void OnValidate()
    {
        // Cho phép xem trước góc nhìn Hades ngay trong Scene View khi kéo thanh trượt ở Inspector
        if (cachedCamera == null)
        {
            cachedCamera = GetComponent<Camera>();
        }
        ApplyCameraSettingsImmediate();
    }

    private void LateUpdate()
    {
        if (target == null)
        {
            return;
        }

        // Khóa góc xoay theo cấu hình (Roll Z luôn bằng 0 để đường chân trời không bị nghiêng)
        Quaternion desiredRotation = CalculateCameraRotation();
        transform.rotation = desiredRotation;

        Vector3 desiredPosition = CalculateTargetPosition(desiredRotation);

        if (smoothTime <= 0f)
        {
            transform.position = desiredPosition;
        }
        else
        {
            // Dùng LateUpdate và SmoothDamp để bám sát Player sau khi PlayerMovement ở Update hoàn tất
            transform.position = Vector3.SmoothDamp(transform.position, desiredPosition, ref currentVelocity, smoothTime);
        }
    }

    /// <summary>
    /// Áp dụng ngay góc xoay, FOV và vị trí bám mục tiêu (dùng khi khởi tạo hoặc tinh chỉnh trong Editor).
    /// </summary>
    public void ApplyCameraSettingsImmediate()
    {
        if (cachedCamera != null)
        {
            cachedCamera.orthographic = false;
            cachedCamera.fieldOfView = fieldOfView;
        }

        Quaternion desiredRotation = CalculateCameraRotation();
        transform.rotation = desiredRotation;

        if (target != null)
        {
            transform.position = CalculateTargetPosition(desiredRotation);
        }
    }

    private Quaternion CalculateCameraRotation()
    {
        // Luôn giữ trục Z = 0 để camera không bị nghiêng chân trời
        return Quaternion.Euler(pitchAngle, yawAngle, 0f);
    }

    private Vector3 CalculateTargetPosition(Quaternion cameraRotation)
    {
        Vector3 focusPoint = target.position + Vector3.up * targetHeightOffset;
        // Lùi camera ra sau theo hướng ngược với hướng nhìn một khoảng bằng distance
        return focusPoint + (cameraRotation * Vector3.back * distance);
    }
}
