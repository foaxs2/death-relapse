# NguCanh.md – Ngữ cảnh và nhật ký làm việc với AI

File này do AI cập nhật sau mỗi lần làm việc (xem quy tắc ở `GEMINI.md`). Dùng để viết báo cáo sử dụng AI và để nắm lại tình hình khi bắt đầu phiên mới.

---

## Tóm tắt hiện tại

> AI ghi đè khối này mỗi lần cập nhật, tối đa 25 dòng.

- **Đề tài:** Death Relapse, đồ án chuyên ngành. Nghiên cứu ảnh hưởng của cách báo đòn (telegraph) của boss lên khả năng nhận đúng loại đòn và cảm giác công bằng của người chơi, thí nghiệm A/B.
- **Công nghệ:** Unity 6 LTS, URP, C#, Input System Package (bản mới).
- **Thể loại:** boss-rush 3D, camera cố định nghiêng từ trên xuống, 1 arena, 1 boss.
- **Mốc:** bản build có chế độ A/B và telemetry xong trước 6/11/2026. Pilot từ 7/11 trên itch.io (bản Windows).
- **Trạng thái code:** Đã tạo project Unity, repo Git `main`. Đã hoàn thành và kiểm tra di chuyển 8 hướng (`PlayerMovement.cs`) và camera bám mượt mà (`CameraFollow.cs`). Đã thực hiện commit đầu tiên cho toàn bộ project setup.
- **Asset đã có:** model hiệp sĩ, animation Mixamo (idle, run with sword, gun play, slash), Animator Controller `PlayerAnimator`. Chưa có model kiếm, súng, boss.
- **Việc đang dở:** Thiết kế bộ đòn của Boss và cơ chế Né hoàn hảo (Perfect Dodge / Dash) cho Player.
- **Vấn đề đã biết:** giáp váy của hiệp sĩ bị chân xuyên khi chạy (chấp nhận).

---

## Nhật ký

> Thêm mục mới ở cuối, không sửa hay xóa mục cũ.

<!-- Các mục mới sẽ được AI thêm bên dưới dòng này -->

## [2026-10-19 09:47] Khởi tạo Git, liên kết repo death-relapse, kéo README và thêm .gitignore
- **Công cụ/model:** Gemini Flash
- **Yêu cầu của tôi (tóm tắt):** Pull repo `https://github.com/foaxs2/death-relapse.git`, lấy README về làm repo chính của dự án, dùng `.gitignore` mẫu cho Unity.
- **AI đã làm (file tạo/sửa):** Tạo file `.gitignore`, kéo về `README.md` từ remote branch `main`, cập nhật `NguCanh.md`.
- **Quyết định kỹ thuật và lý do:** Khởi tạo Git trong thư mục project hiện tại, gắn remote `origin`, checkout tracking branch `main` từ `origin/main` để lấy file `README.md` mà không ảnh hưởng tới các file chưa track của Unity; tạo `.gitignore` chuẩn Unity theo template chính thức của GitHub để loại trừ `Library/`, `Temp/`, `Logs/`, `UserSettings/`, tránh phình repo.
- **Chưa kiểm chứng / rủi ro:** Chưa commit lần đầu cho project code (chờ người dùng xác nhận trước khi thực hiện `git commit` và `git push`).
- **Cách kiểm tra đề xuất:** Chạy `git status` kiểm tra các thư mục rác đã được bỏ qua, kiểm tra file `README.md` và `.gitignore` tại thư mục gốc.
- **Tôi đã kiểm tra / chỉnh sửa:** 
- **Việc tiếp theo:** Xác nhận commit ban đầu (nếu muốn) hoặc tiếp tục cấu hình Input System / import asset hiệp sĩ.
- **Commit:** Chưa commit (chờ người dùng yêu cầu/xác nhận)

## [2026-10-19 11:21] Viết script PlayerMovement và CameraFollow
- **Công cụ/model:** Gemini Flash
- **Yêu cầu của tôi (tóm tắt):** Viết `PlayerMovement.cs` (Input System, 8 hướng world axis, gộp Move 1 lần cùng trọng lực, xoay bằng RotateTowards hàm riêng, cache Animator float "Speed", khóa di chuyển) và `CameraFollow.cs` (LateUpdate, SmoothDamp bám Player, giữ góc xoay, không dùng Find). Chưa commit. Cập nhật NguCanh.md bằng đường dẫn tương đối. Hướng dẫn thiết lập Unity.
- **AI đã làm (file tạo/sửa):** Tạo `Assets/Scripts/Player/PlayerMovement.cs`, tạo `Assets/Scripts/Core/CameraFollow.cs`, cập nhật `NguCanh.md`.
- **Quyết định kỹ thuật và lý do:** Dùng `CharacterController` kết hợp `isGrounded` và lực ghì nhẹ `-2f` khi chạm đất để bám mặt sàn ổn định; gộp vận tốc ngang và thẳng đứng thành 1 lệnh `.Move()` mỗi frame; chuẩn hóa vector input nếu magnitude > 1 để đi chéo không vượt quá tốc độ thẳng; tách hàm `UpdateRotation` riêng để chuẩn bị cho cơ chế ngắm bắn; dùng `SmoothDamp` trong `LateUpdate` cho camera để triệt tiêu hiện tượng giật hình khi player di chuyển trong `Update`.
- **Chưa kiểm chứng / rủi ro:** Chưa chạy test trực tiếp trong Unity Editor (chưa gán tham chiếu `moveAction`, `target`, và kiểm tra điều kiện chuyển state trong Animator).
- **Cách kiểm tra đề xuất:** Gắn `CharacterController` và `PlayerMovement` vào `Knight`, kéo `InputSystem_Actions` (Player/Move) vào `Move Action`, gắn `CameraFollow` vào `Main Camera`, kéo `Knight` vào `Target`, bấm Play và dùng WASD kiểm tra di chuyển / chuyển động animation.
- **Tôi đã kiểm tra / chỉnh sửa:** 
- **Việc tiếp theo:** Gắn component trong Inspector, cấu hình thông số Animator Transitions, kiểm tra chuyển trạng thái Idle/Run.
- **Commit:** Chưa commit (theo yêu cầu người dùng)

## [2026-10-19 14:46] Tinh chỉnh Camera và xác nhận hoàn thiện hệ thống di chuyển cơ bản
- **Công cụ/model:** Gemini Flash
- **Yêu cầu của tôi (tóm tắt):** Tư vấn góc camera chuẩn cho boss-rush kiểu Hades; cập nhật NguCanh.md sau khi đã chỉnh camera và test di chuyển thành công; đề xuất các bước chuẩn bị tiếp theo.
- **AI đã làm (file tạo/sửa):** Cập nhật `NguCanh.md`.
- **Quyết định kỹ thuật và lý do:** Tư vấn nâng góc nghiêng camera lên 50°–55° và tăng khoảng cách (Y=10..12, Z=-8..-10) để bảo đảm nhìn rõ diện tích sàn đấu và hình dạng vùng báo đòn (telegraph AoE) phục vụ nghiên cứu; xác nhận camera và di chuyển nhân vật đạt yêu cầu.
- **Chưa kiểm chứng / rủi ro:** Chưa làm cơ chế né (Dash) với thời gian bất tử (I-frame) và cửa sổ né hoàn hảo (Perfect Dodge); chưa có model kiếm/vũ khí.
- **Cách kiểm tra đề xuất:** Kiểm tra lại góc nhìn camera khi nhân vật di chuyển ra các góc xa của sàn đấu; chuẩn bị asset hoặc collider kiểm tra va chạm.
- **Tôi đã kiểm tra / chỉnh sửa:** 
- **Việc tiếp theo:** Lựa chọn và triển khai cơ chế tiếp theo: Dash / Né hoàn hảo (Perfect Dodge) hoặc Tấn công (Slash / Shoot).
- **Commit:** Chưa commit (chờ người dùng yêu cầu/xác nhận)

## [2026-10-19 15:05] Thực hiện commit đầu tiên cho dự án
- **Công cụ/model:** Gemini Flash
- **Yêu cầu của tôi (tóm tắt):** Thực hiện commit đầu tiên lên Git; tư vấn về animation Idle của boss và thiết kế 5 loại đòn của boss.
- **AI đã làm (file tạo/sửa):** Cập nhật `NguCanh.md`, thực hiện `git add` và `git commit` cho project ban đầu (Assets, Packages, ProjectSettings, .gitignore, GEMINI.md, NguCanh.md).
- **Quyết định kỹ thuật và lý do:** Đóng gói toàn bộ cấu hình nền tảng, thiết lập Input System, 2 script lõi `PlayerMovement.cs` và `CameraFollow.cs` cùng tài liệu quy tắc vào commit đầu tiên theo đúng format `[AI] feat`.
- **Chưa kiểm chứng / rủi ro:** Chưa push lên remote repo GitHub (theo quy định tại GEMINI.md: không tự ý `git push`).
- **Cách kiểm tra đề xuất:** Chạy `git log -1` và `git status` để kiểm tra cây commit và trạng thái working tree sạch sẽ.
- **Tôi đã kiểm tra / chỉnh sửa:** 
- **Việc tiếp theo:** Thiết kế ScriptableObject cho hệ thống đòn đánh của boss và cơ chế Né (Dash) cho Player.
- **Commit:** [AI] feat: khởi tạo dự án, di chuyển 8 hướng và camera follow
