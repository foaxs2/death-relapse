using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : MonoBehaviour
{
    [Header("Input References")]
    [Tooltip("Tham chiếu Input Action điều khiển di chuyển (Vector2, WASD / Left Stick)")]
    [SerializeField] private InputActionReference moveAction;

    [Header("Movement Settings")]
    [Tooltip("Tốc độ di chuyển trên mặt đất (đơn vị: m/s)")]
    [SerializeField] private float moveSpeed = 6f;

    [Tooltip("Tốc độ xoay người về hướng di chuyển (độ/giây)")]
    [SerializeField] private float rotationSpeed = 720f;

    [Header("Gravity & Grounding")]
    [Tooltip("Gia tốc trọng trường kéo nhân vật xuống khi ở trên không")]
    [SerializeField] private float gravity = 20f;

    [Tooltip("Lực ghì nhẹ xuống sàn khi đã chạm đất để giữ isGrounded ổn định trên địa hình")]
    [SerializeField] private float groundedGravity = -2f;

    [Header("State Control")]
    [Tooltip("Cờ khóa di chuyển (dành cho Dash, tấn công hoặc bị choáng sau này)")]
    [SerializeField] private bool isMovementLocked = false;

    // Component tham chiếu đã được cache sẵn trong Awake để tránh gọi GetComponent trong Update
    private CharacterController characterController;
    private Animator animator;

    // Vận tốc theo trục Y (thẳng đứng) do trọng lực tích lũy
    private float verticalVelocity;

    // Cache mã băm tên parameter của Animator để tối ưu hiệu năng CPU
    private static readonly int SpeedHash = Animator.StringToHash("Speed");

    public bool IsMovementLocked
    {
        get => isMovementLocked;
        set => isMovementLocked = value;
    }

    /// <summary>
    /// Cho phép các script khác (ví dụ: PlayerDash sau này) tạm thời khóa hoặc mở khóa di chuyển.
    /// </summary>
    public void SetMovementLocked(bool locked)
    {
        isMovementLocked = locked;
    }

    private void Awake()
    {
        // Cache CharacterController trên chính GameObject này
        characterController = GetComponent<CharacterController>();

        // Cache Animator từ chính GameObject hoặc từ prefab model con (như model Knight Mixamo)
        animator = GetComponentInChildren<Animator>();
    }

    private void OnEnable()
    {
        // Kích hoạt nhận tín hiệu Input khi đối tượng được bật
        if (moveAction != null && moveAction.action != null)
        {
            moveAction.action.Enable();
        }
    }

    private void OnDisable()
    {
        // Vô hiệu hóa nhận tín hiệu Input khi đối tượng bị tắt để tránh rò rỉ sự kiện/xử lý thừa
        if (moveAction != null && moveAction.action != null)
        {
            moveAction.action.Disable();
        }
    }

    private void Update()
    {
        HandleMovement();
    }

    /// <summary>
    /// Xử lý toàn bộ logic tính toán hướng đi, xoay người, trọng lực và gọi di chuyển vật lý.
    /// </summary>
    private void HandleMovement()
    {
        Vector2 input = Vector2.zero;

        // Chỉ đọc input khi người chơi không bị khóa di chuyển
        if (!isMovementLocked && moveAction != null && moveAction.action != null)
        {
            input = moveAction.action.ReadValue<Vector2>();
        }

        // Chuyển đổi input 2D thành vector hướng 3D theo trục thế giới (camera cố định)
        Vector3 moveDirection = new Vector3(input.x, 0f, input.y);

        // Chuẩn hóa vector nếu độ dài lớn hơn 1 để đi chéo không bị nhanh hơn đi thẳng
        if (moveDirection.sqrMagnitude > 1f)
        {
            moveDirection.Normalize();
        }

        // Xoay nhân vật theo hướng di chuyển (chỉ xoay khi có input)
        UpdateRotation(moveDirection);

        // Vận tốc ngang mong muốn
        Vector3 horizontalVelocity = moveDirection * moveSpeed;

        // Xử lý gia tốc trọng lực bám đất
        ApplyGravity();

        // Gộp vận tốc ngang và vận tốc rơi thành MỘT lần gọi CharacterController.Move duy nhất trong frame
        Vector3 motion = (horizontalVelocity + Vector3.up * verticalVelocity) * Time.deltaTime;
        characterController.Move(motion);

        // Cập nhật giá trị Speed cho Animator (0 khi đứng yên hoặc khi bị khóa di chuyển)
        UpdateAnimator(horizontalVelocity.magnitude);
    }

    /// <summary>
    /// Xoay mượt mà nhân vật về hướng di chuyển mong muốn bằng Quaternion.RotateTowards.
    /// Hàm này được tách riêng để sau này dễ dàng tái sử dụng hoặc thay thế bằng hướng ngắm chuột khi bắn súng.
    /// </summary>
    private void UpdateRotation(Vector3 direction)
    {
        // Chỉ thực hiện xoay khi có hướng di chuyển rõ ràng (tránh bị giật quay về góc mặc định khi nhả phím)
        if (direction.sqrMagnitude > 0.001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(direction, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
        }
    }

    /// <summary>
    /// Tính toán vận tốc rơi tự do và lực ép sát mặt đất để bám dốc/sàn ổn định.
    /// </summary>
    private void ApplyGravity()
    {
        if (characterController.isGrounded && verticalVelocity < 0f)
        {
            // Duy trì một lực ghì âm nhỏ khi chạm đất để isGrounded không bị chập chờn khi di chuyển
            verticalVelocity = groundedGravity;
        }
        else
        {
            // Tích lũy gia tốc trọng trường khi rơi tự do
            verticalVelocity -= gravity * Time.deltaTime;
        }
    }

    /// <summary>
    /// Đồng bộ tốc độ di chuyển hiện tại vào Animator Parameter "Speed".
    /// </summary>
    private void UpdateAnimator(float currentHorizontalSpeed)
    {
        if (animator != null)
        {
            animator.SetFloat(SpeedHash, currentHorizontalSpeed);
        }
    }
}
