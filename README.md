# Death Relapse

> **Khó, nhưng thua là do mình gà.**

Game hành động 2D góc nhìn từ trên xuống (Unity, C#), làm trong đồ án chuyên ngành CMP436 (HUTECH, HK1 2026–2027).
Game là công cụ để kiểm chứng một câu hỏi nghiên cứu về **độ khó công bằng**.

- Sinh viên: Nguyễn Quang Duy · MSSV 2380600359 · Lớp 23DTHC7 · Chuyên ngành Công nghệ phần mềm
- GVHD: TS. Nguyễn Quang Trung

## Tên đề tài

**Phát triển Death Relapse: game hành động 2D có độ khó công bằng dựa trên mô hình báo đòn – phản ứng**

*Developing Death Relapse: A Fair-Difficulty 2D Action Game Based on a Telegraph–Response Model*

## Câu định vị

Đồ án giải quyết việc người chơi game hành động khó thường thua boss mà không biết do mình phản ứng sai hay do game báo đòn không rõ, cho người chơi 18–30 tuổi, đo bằng tỉ lệ nhận đúng loại đòn và tỉ lệ lần thua người chơi cho là do lỗi của mình.

## Câu hỏi nghiên cứu và giả thuyết

- **CH1.** Báo đòn đa kênh (màu + hình + âm), với thời lượng tính từ ngân sách phản ứng, có làm tăng tỉ lệ nhận đúng loại đòn so với báo đòn một kênh không?
- **CH2.** Khi nhận đúng đòn nhiều hơn, người chơi có quy lần thua cho bản thân nhiều hơn không?
- **H1.** Tỉ lệ nhận đúng loại đòn ở nhóm B cao hơn nhóm A ít nhất 20 điểm %.
- **H2.** Tỉ lệ lần thua "do lỗi của mình" ở nhóm B cao hơn nhóm A ít nhất 20 điểm %.

## Bảng trước–sau

| Chỉ số | Cách đo | Trước | Sau | Ngày đo | Bằng chứng |
|---|---|---|---|---|---|
| 1. Tỉ lệ nhận đúng loại đòn (%) | Log tự động từng đòn: số lần phản ứng đúng ÷ số đòn đã thấy | Nhóm A (đối chứng), dự kiến đo 13/11–20/11/2026 | *(để trống)* | *(để trống)* | *(để trống)* |
| 2. Tỉ lệ lần thua do lỗi của mình (%) | Sau mỗi lần thua hỏi 1 câu: (a) mình phản ứng/chọn sai, hay (b) game khó hiểu/không công bằng? Tỉ lệ = số (a) ÷ số lần thua | Nhóm A (đối chứng), dự kiến đo 13/11–20/11/2026 | *(để trống)* | *(để trống)* | *(để trống)* |

Mục tiêu: nhóm B cao hơn nhóm A ít nhất 20 điểm % ở mỗi chỉ số.

## Thiết kế thí nghiệm

- **Biến thay đổi:** cách báo đòn. Nhóm A chỉ báo bằng màu; nhóm B báo bằng màu + hình + âm, thời lượng tính theo ngân sách thời gian phản ứng.
- **Giữ cố định:** cùng boss, cùng hitbox, cùng hướng dẫn, cùng câu hỏi; Khát máu LV4 và các tính năng tự chọn tắt khi đo. Hai nhóm dùng cùng một bản build, đổi chế độ bằng cờ cấu hình.
- **Người tham gia:** ít nhất 20 người (nhóm A ≥ 8, nhóm B ≥ 12), trong đó ít nhất 10 người ngoài nhóm bạn thân.
- **Pilot:** nhóm chơi game Zalo (~10 người), đại diện Văn Đức Đạt, đã đồng ý thử.
- **Lưu ý:** mẫu nhỏ nên chỉ mô tả xu hướng, không tuyên bố ý nghĩa thống kê.

## Phạm vi sản phẩm (MVP)

| Mức | Nội dung | Tuần |
|---|---|---|
| Must #1 | Lõi chiến đấu C# chạy theo bước thời gian cố định: di chuyển, tấn công, **Né hoàn hảo**, 1 kẻ địch; telemetry ≥ 3 sự kiện; test tự động cho cửa sổ Né | W4 |
| Must #2 | **Parry** (shotgun bắn quạt) + thanh **Tư thế**; boss ≤ 4 loại đòn; hai chế độ báo đòn A/B; phát hành itch.io, gửi dữ liệu lên Google Form/Sheets | W5 |
| Must #3 | **Khát máu** LV1–3; bộ đo đầy đủ (log từng đòn + phiếu hỏi sau mỗi lần thua) | W6 |
| Không làm | Multiplayer, AI sinh nội dung, backend riêng, bản mobile | — |

## Kế thừa từ Đồ án cơ sở

Không dùng lại mã nguồn hay asset. Đồ án cơ sở là game thẻ bài theo lượt (1 vs 3, dungeon crawler, có skill, item, sự kiện); Death Relapse là game hành động thời gian thực, khác hoàn toàn về thể loại, cơ chế và nghệ thuật. Phần kế thừa là kỹ năng Unity/C# và quy trình Git.

## Công nghệ

Unity · C# (lõi luật chiến đấu tách thành thư viện riêng, có test tự động) · Git/GitHub · GitHub Actions + GameCI (Docker) · itch.io · Google Form/Sheets

## Cấu trúc repo (dự kiến)

```
/Core          lõi luật chiến đấu (C# thuần)
/Tests         test tự động
/UnityProject  dự án Unity
/docs          ADR, đặc tả đo
/evidence      ảnh, log, minh chứng
README.md
CREDITS.md
```

## Tiến độ

- [x] W1: vấn đề, câu hỏi nghiên cứu, người chủ pilot, repo
- [ ] W2: 5 phỏng vấn người chơi, 8 user story, phạm vi MVP
- [ ] W3: C4 mức 1–2, 3 ADR, repo "Hello World"
- [ ] W4–W6: Must #1, #2, #3
- [ ] W7: bảo mật, Docker, CI, đóng băng v0.9
- [ ] W8: pilot ≥ 20 người, bảng trước–sau, chi phí
- [ ] W9–W10: báo cáo, slide, video demo, v1.0

## Cách chạy

*(Cập nhật khi có bản chạy được ở W3–W4.)*

## Asset và bản quyền

Toàn bộ asset bên thứ ba và license được ghi trong `CREDITS.md`.

## Khai báo sử dụng AI

Có dùng AI hỗ trợ soạn thảo, học và viết code; chi tiết trong Nhật ký AI. Commit do AI sinh phần lớn có tiền tố `[AI]`.
