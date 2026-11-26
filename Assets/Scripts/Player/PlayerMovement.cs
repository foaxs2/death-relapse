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

    [Tooltip("Tốc độ xoay người về hướng di chuyển hoặc hướng chuột (độ/giây)")]
    [SerializeField] private float rotationSpeed = 720f;

    [Tooltip("Khi tắt (mặc định kiểu Hades), nhân vật xoay theo hướng chạy WASD và chỉ xoay về hướng chuột khi ra đòn. Khi bật, nhân vật luôn xoay theo chuột.")]
    [SerializeField] private bool rotateTowardsMouse = false;

    [Header("Gravity & Grounding")]
    [Tooltip("Gia tốc trọng trường kéo nhân vật xuống khi ở trên không")]
    [SerializeField] private float gravity = 20f;

    [Tooltip("Lực ghì nhẹ xuống sàn khi đã chạm đất để giữ isGrounded ổn định trên địa hình")]
    [SerializeField] private float groundedGravity = -2f;

    [Header("State Control")]
    [Tooltip("Cờ khóa di chuyển (dành cho Dash, lăn né, tấn công hoặc bị choáng)")]
    [SerializeField] private bool isMovementLocked = false;

    // Component tham chiếu đã được cache sẵn trong Awake để tránh gọi GetComponent trong Update
    private CharacterController characterController;
    private Animator animator;
    private Camera mainCamera;

    // Vận tốc theo trục Y (thẳng đứng) do trọng lực tích lũy
    private float verticalVelocity;

    // Hướng input thế giới hiện tại (dùng cho các cơ chế khác như né đòn)
    private Vector3 currentInputDirection = Vector3.zero;

    // Cache mã băm tên parameter của Animator để tối ưu hiệu năng CPU
    private static readonly int SpeedHash = Animator.StringToHash("Speed");

    public bool IsMovementLocked
    {
        get => isMovementLocked;
        set => isMovementLocked = value;
    }

    /// <summary>
    /// Hướng di chuyển mong muốn hiện tại từ input của người chơi (trên mặt phẳng XZ).
    /// Đọc trực tiếp từ moveAction nếu đang bật để PlayerDodge lấy được hướng phím tức thì không bị trễ frame.
    /// </summary>
    public Vector3 CurrentInputDirection
    {
        get
        {
            if (moveAction != null && moveAction.action != null && moveAction.action.enabled)
            {
                Vector2 liveInput = moveAction.action.ReadValue<Vector2>();
                return ConvertInputToCameraPlane(liveInput);
            }
            return currentInputDirection;
        }
    }

    /// <summary>
    /// Cho phép các script khác (ví dụ: PlayerDodge, PlayerAttackController) tạm thời khóa hoặc mở khóa di chuyển.
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

        // Cache Camera chính để phục vụ tính hướng con trỏ chuột trên mặt phẳng sàn
        mainCamera = Camera.main;
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

        // Luôn đọc input để cập nhật hướng currentInputDirection (phục vụ né theo hướng WASD)
        if (moveAction != null && moveAction.action != null)
        {
            input = moveAction.action.ReadValue<Vector2>();
        }

        // Chuyển đổi input 2D thành hướng 3D trên mặt phẳng sàn theo góc nhìn của Camera (chuẩn cho góc chéo Hades)
        currentInputDirection = ConvertInputToCameraPlane(input);

        // Nếu bị khóa di chuyển (đang né, choáng, hoặc animation đòn đánh), chỉ áp dụng trọng lực bám sàn
        if (isMovementLocked)
        {
            UpdateAnimator(0f);
            return;
        }

        // Xoay nhân vật: ưu tiên hướng con trỏ chuột nếu bật rotateTowardsMouse, ngược lại xoay theo hướng WASD
        if (rotateTowardsMouse && TryGetMouseWorldDirection(out Vector3 mouseDirection))
        {
            UpdateRotation(mouseDirection);
        }
        else
        {
            UpdateRotation(currentInputDirection);
        }

        // Vận tốc ngang mong muốn
        Vector3 horizontalVelocity = currentInputDirection * moveSpeed;

        // Xử lý gia tốc trọng lực bám đất
        ApplyGravity();

        // Gộp vận tốc ngang và vận tốc rơi thành MỘT lần gọi CharacterController.Move duy nhất trong frame
        Vector3 motion = (horizontalVelocity + Vector3.up * verticalVelocity) * Time.deltaTime;
        characterController.Move(motion);

        // Cập nhật giá trị Speed cho Animator
        UpdateAnimator(horizontalVelocity.magnitude);
    }

    /// <summary>
    /// Quy đổi input 2D (WASD / Left Stick) sang hướng di chuyển trên mặt phẳng ngang XZ theo góc xoay của Camera.
    /// Đảm bảo bấm W luôn đi thẳng lên phía trên màn hình dù Camera xoay chéo kiểu Hades.
    /// </summary>
    private Vector3 ConvertInputToCameraPlane(Vector2 input)
    {
        if (input.sqrMagnitude < 0.0001f)
        {
            return Vector3.zero;
        }

        if (mainCamera == null)
        {
            mainCamera = Camera.main;
        }

        Vector3 direction;
        if (mainCamera != null)
        {
            Vector3 camForward = mainCamera.transform.forward;
            camForward.y = 0f;
            if (camForward.sqrMagnitude > 0.0001f)
            {
                camForward.Normalize();
            }

            Vector3 camRight = mainCamera.transform.right;
            camRight.y = 0f;
            if (camRight.sqrMagnitude > 0.0001f)
            {
                camRight.Normalize();
            }

            direction = camRight * input.x + camForward * input.y;
        }
        else
        {
            direction = new Vector3(input.x, 0f, input.y);
        }

        if (direction.sqrMagnitude > 1f)
        {
            direction.Normalize();
        }

        return direction;
    }

    /// <summary>
    /// Xoay tức thì nhân vật hướng thẳng về phía con trỏ chuột trên mặt phẳng sàn đấu.
    /// Được gọi ngay khi bấm chuột trái tấn công để đòn chém luôn hướng đúng vào vị trí chuột.
    /// </summary>
    public void RotateTowardsMouseImmediate()
    {
        if (TryGetMouseWorldDirection(out Vector3 mouseDirection))
        {
            transform.rotation = Quaternion.LookRotation(mouseDirection, Vector3.up);
        }
    }

    /// <summary>
    /// Tính toán vector hướng từ nhân vật tới vị trí con trỏ chuột trên mặt phẳng ngang của sàn đấu.
    /// </summary>
    public bool TryGetMouseWorldDirection(out Vector3 direction)
    {
        direction = Vector3.zero;

        if (Mouse.current == null) return false;

        if (mainCamera == null)
        {
            mainCamera = Camera.main;
            if (mainCamera == null) return false;
        }

        Vector2 mouseScreenPos = Mouse.current.position.ReadValue();
        Ray ray = mainCamera.ScreenPointToRay(mouseScreenPos);
        Plane groundPlane = new Plane(Vector3.up, transform.position);

        if (groundPlane.Raycast(ray, out float enterDistance))
        {
            Vector3 hitPoint = ray.GetPoint(enterDistance);
            Vector3 lookDirection = hitPoint - transform.position;
            lookDirection.y = 0f;

            if (lookDirection.sqrMagnitude > 0.0001f)
            {
                direction = lookDirection.normalized;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Cho phép các hành vi đặc biệt (như cú lăn né PlayerDodge) áp dụng vận tốc di chuyển ngang
    /// nhưng vẫn giữ nguyên xử lý trọng lực và bám sàn chuẩn của PlayerMovement.
    /// </summary>
    public void MoveWithExternalVelocity(Vector3 horizontalVelocity)
    {
        ApplyGravity();
        Vector3 motion = (horizontalVelocity + Vector3.up * verticalVelocity) * Time.deltaTime;
        characterController.Move(motion);
        UpdateAnimator(0f); // Giữ Speed = 0 để không bị chồng chéo chuyển động chạy trong Animator
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
