# NguCanh.md – Ngữ cảnh và nhật ký làm việc với AI

File này do AI cập nhật sau mỗi lần làm việc (xem quy tắc ở `GEMINI.md`). Dùng để viết báo cáo sử dụng AI và để nắm lại tình hình khi bắt đầu phiên mới.

---

## Tóm tắt hiện tại

> AI ghi đè khối này mỗi lần cập nhật, tối đa 25 dòng.

- **Đề tài:** Death Relapse, đồ án chuyên ngành. Nghiên cứu ảnh hưởng của cách báo đòn (telegraph) của boss lên khả năng nhận đúng loại đòn và cảm giác công bằng của người chơi, thí nghiệm A/B.
- **Công nghệ:** Unity 6 LTS, URP, C#, Input System Package (bản mới).
- **Thể loại:** boss-rush 3D, camera cố định nghiêng từ trên xuống, 1 arena, 1 boss.
- **Mốc:** bản build có chế độ A/B và telemetry xong trước 6/11/2026. Pilot từ 7/11 trên itch.io (bản Windows).
- **Trạng thái code:** Đã tạo project Unity, repo Git `main`. Đã hoàn thành `PlayerMovement.cs`, `CameraFollow.cs`, `CREDITS.md`. Đã hoàn thành hệ thống đòn đánh báo trước của Boss (`BossAttackData.cs`, `BossAttackController.cs`) với mesh quạt đỏ điền dần bằng `Time.deltaTime` và kiểm tra va chạm thuần túy theo hình học XZ dùng chung thông số đã chốt.
- **Asset đã có:** model hiệp sĩ (đã rig Mixamo), animation Mixamo. Model boss Stelae knight (đã import và gán vật liệu). Đã ghi nhận bản quyền trong `CREDITS.md`.
- **Việc đang dở:** Gắn component `BossAttackController` vào Boss trong Editor, tạo các file ScriptableObject đòn đánh (0.6s, 1s, 2s) và test phím 1. Triển khai cơ chế Né (Dash / Perfect Dodge) cho Player.
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

## [2026-10-19 16:32] Tạo file CREDITS.md ghi nhận bản quyền 3D model và animation
- **Công cụ/model:** Gemini Flash
- **Yêu cầu của tôi (tóm tắt):** Tạo `CREDITS.md` cho model hiệp sĩ và model boss từ Sketchfab; ghi nhận việc tách xương để auto-rig bằng Mixamo và tách vũ khí model boss.
- **AI đã làm (file tạo/sửa):** Tạo `CREDITS.md`, cập nhật `NguCanh.md`.
- **Quyết định kỹ thuật và lý do:** Trích xuất thông tin tác giả (ValeriyUA, Muru), concept art (Shuohan), link và giấy phép CC BY 4.0; ghi rõ các thay đổi chỉnh sửa (modifications) theo đúng điều khoản Creative Commons và quy định tại `GEMINI.md`. Gộp toàn bộ animation Mixamo vào mục cấp phép tương ứng.
- **Chưa kiểm chứng / rủi ro:** Model boss đang trong quá trình chỉnh sửa bên ngoài (Blender/Maya), chưa import vào Unity.
- **Cách kiểm tra đề xuất:** Mở file `CREDITS.md` kiểm tra đường link, tên tác giả và các mô tả thay đổi.
- **Tôi đã kiểm tra / chỉnh sửa:** 
- **Việc tiếp theo:** Nhập model boss sau khi chuẩn bị xong, triển khai cơ chế Né (Dash) cho Player.
- **Commit:** Chưa commit (chờ người dùng yêu cầu/xác nhận)

## [2026-10-20 19:35] Triển khai hệ thống báo đòn (telegraph) hình quạt và hitbox toán học cho Boss
- **Công cụ/model:** Gemini Flash
- **Yêu cầu của tôi (tóm tắt):** Tạo `BossAttackData.cs` (ScriptableObject) và `BossAttackController.cs`; 1 đòn đỏ cận quét quạt điền dần bằng `Time.deltaTime`, hết giờ kiểm tra trúng/hụt bằng khoảng cách XZ và góc quét từ dữ liệu chốt duy nhất, không dùng collider; hỗ trợ scale tốc độ Animator; phím thử 1; vẽ Gizmos cung quạt; cập nhật NguCanh.md và commit; hướng dẫn thiết lập trong Editor và 3 câu hỏi tự kiểm tra.
- **AI đã làm (file tạo/sửa):** Tạo `Assets/Scripts/Boss/BossAttackData.cs`, tạo `Assets/Scripts/Boss/BossAttackController.cs`, cập nhật `NguCanh.md`.
- **Quyết định kỹ thuật và lý do:** Dùng chung một nguồn dữ liệu hình học (center, forward, radius, arcAngle) được chốt MỘT LẦN khi bắt đầu đòn cho cả việc dựng Mesh báo đòn và tính toán va chạm để đảm bảo 100% tính công bằng cho nghiên cứu A/B (không lệch pha khi boss xoay/di chuyển sau khi đã báo); dựng Mesh Triangle Fan bán kính 1 đơn vị và điều khiển độ phình bằng localScale XZ để tối ưu hiệu năng (chỉ cần 1 mesh dùng chung cho cả Fill và Outline); dùng `Time.deltaTime` trong Coroutine để kiểm soát thời gian thực tế chính xác tuyệt đối ở mọi mức khung hình; kiểm tra va chạm bằng công thức khoảng cách 2D XZ kết hợp `Vector3.Angle` tránh phụ thuộc vào Physics/Collider; nâng nhẹ Y (+0.015m và +0.002m) triệt tiêu hiện tượng Z-fighting trên sàn.
- **Chưa kiểm chứng / rủi ro:** Chưa kiểm tra thực tế trong Unity Editor (chưa tạo GameObject quạt đỏ với Material URP Unlit Transparent, chưa gán tham chiếu vào BossAttackController và chưa chạy thử phím 1 để đo thời gian phản hồi thực tế).
- **Cách kiểm tra đề xuất:** Thiết lập 2 GameObject rỗng (Fill, Outline) gắn MeshFilter/MeshRenderer với Material URP đỏ trong suốt; gắn BossAttackController lên Boss, gán Player Transform và Animator; tạo các asset BossAttackData với thời lượng 0.6s, 1s, 2s; nhấn Play, ấn phím 1 và quan sát Console log, vùng quạt điền dần và kết quả TRÚNG/HỤT khi Player đứng trong/ngoài vùng quét.
- **Tôi đã kiểm tra / chỉnh sửa:** 
- **Việc tiếp theo:** Gắn component trong Unity Editor, tạo các file ScriptableObject cấu hình đòn đánh, thử nghiệm phím 1 và tinh chỉnh góc quay của model Boss nếu cần.
- **Commit:** [AI] feat: đòn đánh quạt đỏ của boss với telegraph điền dần và hitbox toán học
