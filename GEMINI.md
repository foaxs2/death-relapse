# Quy tắc làm việc cho AI agent – Đồ án "Death Relapse"

Đọc hết file này trước khi làm bất cứ việc gì trong project.

## 1. Bối cảnh dự án

- Đồ án chuyên ngành làm một mình: game boss-rush 3D, camera cố định nghiêng từ trên xuống (kiểu Hades), 1 arena, 1 boss.
- Mục đích thật là một nghiên cứu: so sánh hai cách báo đòn (telegraph) của boss giữa nhóm A và nhóm B, đo khả năng nhận đúng loại đòn và cảm giác công bằng của người chơi. Game chỉ là công cụ thí nghiệm.
- Người dùng phải hiểu và bảo vệ được từng dòng code. Vai trò của bạn là hỗ trợ, không phải làm thay mà người dùng không hiểu.
- Công nghệ: Unity 6 LTS, URP, C#.

## 2. Ngôn ngữ

- Trả lời bằng tiếng Việt.
- Tên biến, hàm, class, file bằng tiếng Anh. Comment trong code bằng tiếng Việt, ngắn gọn, giải thích "vì sao" chứ không lặp lại "làm gì".

## 3. Input: chỉ dùng Input System Package (bản mới)

- Dùng `using UnityEngine.InputSystem;`, `InputAction`, `InputActionReference` hoặc `InputActionAsset`.
- **Cấm** dùng API cũ: `Input.GetAxis`, `Input.GetKey`, `Input.GetButton`, `Input.mousePosition`, `Input.GetMouseButton`. Vị trí chuột lấy bằng `Mouse.current.position.ReadValue()`.
- Bật action trong `OnEnable()`, tắt trong `OnDisable()`.
- Nếu nghi ngờ project đang để Active Input Handling là "Input Manager (Old)" hoặc "Both", hãy báo cho người dùng trước khi viết code input.

## 4. Git và commit

- **Chỉ commit khi người dùng yêu cầu hoặc xác nhận.** Không tự `git push`, không `git push --force`, không viết lại lịch sử (`rebase`, `reset --hard`, `commit --amend` trên commit đã có).
- Commit có phần code do AI viết hoặc sửa đáng kể thì **bắt buộc mở đầu bằng `[AI]`**. Commit hoàn toàn do người dùng tự làm thì không dùng tiền tố này.
- Định dạng:

```text
[AI] <loại>: <mô tả ngắn, tiếng Việt, dưới 72 ký tự>

Công cụ: Gemini (Antigravity)
Yêu cầu: <tóm tắt ngắn người dùng đã yêu cầu gì>
Ghi chú: <điều cần lưu ý, ví dụ chưa test trong Editor>
```

- `<loại>` là một trong: `feat`, `fix`, `refactor`, `docs`, `chore`.
- Mỗi commit chỉ làm một việc logic. Không gộp nhiều tính năng vào một commit.
- **Trước khi commit phải cập nhật `NguCanh.md`** (xem mục 5) rồi commit cùng lúc. Không cần ghi mã hash vào NguCanh.md, chỉ cần ghi lại tiêu đề commit.
- Không commit các thư mục sinh ra tự động: `Library/`, `Temp/`, `Logs/`, `UserSettings/`, `obj/`, `Builds/`. Nếu `.gitignore` thiếu thì báo người dùng.
- Không commit file lớn hơn 50 MB. Asset nặng (FBX, PNG, WAV) nên dùng Git LFS, hãy nhắc người dùng thay vì tự làm.

## 5. Nhật ký ngữ cảnh: `NguCanh.md`

File `NguCanh.md` nằm ở thư mục gốc project. Mục đích: giúp người dùng viết báo cáo "sử dụng AI" và giúp một phiên làm việc mới (hoặc một cuộc trò chuyện mới) nắm lại tình hình nhanh.

**Sau mỗi lần làm việc xong** (hoặc trước mỗi commit), bạn phải:

1. Cập nhật khối **"Tóm tắt hiện tại"** ở đầu file (ghi đè, luôn phản ánh trạng thái mới nhất, không quá 25 dòng).
2. Thêm **một mục mới vào cuối file** trong phần **"Nhật ký"** theo mẫu dưới đây. **Không sửa hay xóa các mục cũ.**

```markdown
## [YYYY-MM-DD HH:mm] <tiêu đề ngắn>
- **Công cụ/model:** 
- **Yêu cầu của tôi (tóm tắt):** 
- **AI đã làm (file tạo/sửa):** 
- **Quyết định kỹ thuật và lý do:** 
- **Chưa kiểm chứng / rủi ro:** 
- **Cách kiểm tra đề xuất:** 
- **Tôi đã kiểm tra / chỉnh sửa:** (để trống, người dùng tự điền)
- **Việc tiếp theo:** 
- **Commit:** <tiêu đề commit>
```

Quy tắc ghi:

- Ô **"Tôi đã kiểm tra / chỉnh sửa"** luôn để trống cho người dùng. **Tuyệt đối không tự điền** hay khẳng định người dùng đã kiểm tra.
- Chỉ ghi những gì đã thực sự xảy ra trong phiên làm việc. Không bịa, không tô hồng.
- Viết cô đọng, đủ để người đọc sau vài tuần vẫn hiểu.

## 6. Cách viết code

- Code ngắn gọn, dễ đọc, đúng với yêu cầu. **Không thêm tính năng ngoài yêu cầu.**
- Sau khi viết xong, giải thích ngắn: mỗi script làm gì, các hàm chính hoạt động ra sao, những dòng quan trọng nhất. Người dùng cần hiểu để bảo vệ đồ án.
- Mọi hằng số, tốc độ, thời lượng, sát thương phải là `[SerializeField]` hoặc nằm trong ScriptableObject. Không để số cứng trong logic.
- Không dùng `Find`, `FindObjectOfType`, `GetComponent` trong `Update`. Lưu tham chiếu trước (cache).
- Không tạo object (`Instantiate`) liên tục trong lúc chơi nếu có thể dùng pooling (đạn, hiệu ứng).
- Player và boss dùng state machine đơn giản (Idle, Move, Attack, Dodge, Hurt, Dead...). Không viết chuỗi `if` lồng nhau cho trạng thái.
- Dùng `Time.deltaTime` cho mọi thứ tính theo thời gian. Logic vật lý dùng `FixedUpdate` hoặc `CharacterController`.
- Mỗi class một file, tên file trùng tên class. Đặt script trong `Assets/Scripts/` theo thư mục con (`Player`, `Boss`, `Combat`, `UI`, `Telemetry`).

## 7. Thiết kế phục vụ thí nghiệm A/B

Các yêu cầu này quan trọng hơn cả sự gọn gàng của code:

- Mỗi đòn của boss là một ScriptableObject gồm: loại đòn, clip animation, hình dạng hitbox, hình dạng vùng cảnh báo, **thời lượng telegraph**, kiểu phản ứng đúng (né hay phản đòn).
- **Hitbox phải khớp chính xác vùng cảnh báo.** Cùng một dữ liệu hình học, không viết hai nơi riêng.
- Nhóm A và nhóm B phải có **cùng thời lượng telegraph** và **cùng animation**. Chỉ khác ở các kênh báo hiệu bật/tắt (màu, vùng sàn, âm thanh). Không để hai chế độ khác nhau về thời gian hay độ khó.
- Phân nhóm A/B **ngẫu nhiên bằng code**, lưu mã người chơi và nhóm vào log.
- Log telemetry theo cấu trúc cố định (CSV hoặc JSON): mã người chơi, nhóm, thời điểm, loại đòn, phản ứng của người chơi, đúng/sai, thời gian phản ứng, kết quả. Ghi ra file, không dùng backend.

## 8. Phạm vi (không làm nếu không được yêu cầu)

Không tự thêm: Bloodlust, Posture Bar, inventory, skill tree, nhiều màn chơi, sinh map ngẫu nhiên, hệ thống roguelike. Những thứ này để dành cho đồ án tốt nghiệp, không thuộc đồ án này. Khi người dùng yêu cầu thứ gì có vẻ vượt quá phạm vi, hãy hỏi lại.

## 9. Những việc phải hỏi trước khi làm

- Sửa trực tiếp file scene (`.unity`), prefab (`.prefab`), `.meta` hoặc các file trong `ProjectSettings/` và `Packages/`. Tốt hơn là hướng dẫn người dùng thao tác trong Unity Editor, vì các file này dễ hỏng khi sửa bằng tay.
- Cài thêm package, asset hoặc thư viện bên ngoài.
- Xóa, đổi tên hoặc di chuyển file.
- Thay đổi cấu trúc thư mục hoặc kiến trúc đã thống nhất.

## 10. Trung thực về kết quả

- Bạn không chạy được Unity Editor. **Không nói code "chạy được" hoặc "đã test" nếu chưa thật sự biên dịch hoặc chạy.**
- Sau mỗi thay đổi, liệt kê các điều người dùng cần kiểm tra trong Editor (gắn script vào object nào, gán tham chiếu nào, test thế nào).
- Khi không chắc về một API hoặc hành vi của Unity, nói rõ là chưa chắc, đừng đoán rồi trình bày như sự thật.
- Khi yêu cầu mơ hồ hoặc mâu thuẫn với các quy tắc trong file này, hỏi lại thay vì tự quyết.

## 11. Asset và giấy phép

- Mọi asset bên ngoài (model, animation, âm thanh, font, texture) phải được ghi vào `CREDITS.md` gồm: tên, tác giả, nguồn (link), loại giấy phép.
- Asset giấy phép CC BY bắt buộc ghi công tác giả. Không thêm asset không rõ giấy phép.
- Không đưa khóa API, mật khẩu hoặc link riêng tư vào repo.
