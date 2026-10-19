using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    [Header("Target Tracking")]
    [Tooltip("Transform của Player mục tiêu cần camera bám theo (kéo thả từ Hierarchy vào đây, không dùng Find)")]
    [SerializeField] private Transform target;

    [Header("Offset Settings")]
    [Tooltip("Khoảng cách lệch cố định giữa Camera và Player theo trục thế giới")]
    [SerializeField] private Vector3 offset = new Vector3(0f, 12f, -10f);

    [Header("Smooth Settings")]
    [Tooltip("Thời gian làm mượt chuyển động của Camera (giây). Giá trị càng nhỏ camera bám càng sát)")]
    [SerializeField] private float smoothTime = 0.2f;

    // Vận tốc tức thời nội bộ của Camera được hàm Vector3.SmoothDamp tự tính toán và cập nhật qua từng frame
    private Vector3 currentVelocity;

    private void Start()
    {
        // Nếu offset chưa được thiết lập thủ công trong Inspector (giá trị Vector3.zero),
        // tự động ghi nhận vị trí lệch hiện tại giữa Camera và Player trong Scene để giữ nguyên góc nhìn đã căn chỉnh.
        if (offset == Vector3.zero && target != null)
        {
            offset = transform.position - target.position;
        }
    }

    private void LateUpdate()
    {
        // Không xử lý nếu chưa gán target để tránh lỗi NullReferenceException
        if (target == null)
        {
            return;
        }

        // Điểm mục tiêu lý tưởng mà camera cần đến
        Vector3 targetPosition = target.position + offset;

        // Dùng LateUpdate và SmoothDamp để camera di chuyển êm ái, bám sát nhân vật ngay sau khi logic di chuyển của Player ở Update đã hoàn tất
        transform.position = Vector3.SmoothDamp(transform.position, targetPosition, ref currentVelocity, smoothTime);

        // Giữ nguyên góc xoay ban đầu của Camera (không thay đổi transform.rotation)
    }
}
