# Death Relapse
Game boss-rush 3D (Unity 6) phục vụ nghiên cứu ảnh hưởng của cách báo đòn lên người chơi.
## Chạy
1. Cài Unity <ghi đúng phiên bản trong ProjectSettings/ProjectVersion.txt>.
2. Mở project, mở scene `Assets/Scenes/SampleScene.unity`, bấm Play.
## Điều khiển
- `WASD`: Di chuyển quanh đấu trường (đồng bộ theo góc nhìn trên xuống kiểu Hades)
- `Chuột trái`: Đánh combo 2 nhát kiếm (`Slash1` / `Slash2`)
- `Space`: Lăn né có khung bất tử (I-frame)
- `H`: Bật / Tắt bảng hướng dẫn điều khiển lần đầu (tự khóa tạm chiến đấu khi mở)
- `F9`: Bật / Tắt công cụ đo NFR (FPS trung bình, 1% Low FPS trong 60s, thời gian nạp đấu trường `SceneLoadMs`)
## Trạng thái (Đóng băng tính năng `v0.9` – Tuần 7)
- **Gameplay & Arena:** Đấu trường Gothic 3D (`SampleScene.unity`), Knight (300 HP, combo 2 nhát, lăn né I-frame, tiếng bước chân khớp Animation Event), Boss Stelae Knight (3000 HP, 2 đòn `Red Attack` hình quạt + `Strong Red Attack` chữ nhật `BoxLine` kèm sóng xung kích nứt đất, bám địa hình).
- **Thí nghiệm A/B & UX:** Chế độ Build A (`GroupA_NoTelegraph`) / Build B (`GroupB_WithTelegraph`), bảng hướng dẫn lần đầu (`FirstTimeControlsOverlay`), màn hình `THẤT BẠI` + khảo sát mở khóa Replay, 8 hiệu ứng âm thanh `.mp3` (Pixabay, đã ghi trong `CREDITS.md`).
- **Bảo mật, Telemetry & DevSecOps:** Lõi bảo mật C# thuần (`TelemetrySecurityCore.cs`), chống sửa log cục bộ bằng `HMAC-SHA256`/`SHA-256`, chặn CSV/Formula Injection (OWASP A03), vá TC-12 gửi bù hàng đợi ngoại tuyến khi bấm Replay, Telemetry Security Proxy (FastAPI + Docker) và GitHub Actions CI (C# NUnit + Python pytest + Docker smoke test).

> **Khó, nhưng thua là do mình gà.**

Game hành động 3D góc nhìn từ trên xuống (Unity, C#), làm trong đồ án chuyên ngành CMP436 (HUTECH, HK1 2026–2027).
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
- [x] W7: bảo mật, Docker, CI, đóng băng v0.9
- [ ] W8: pilot ≥ 20 người, bảng trước–sau, chi phí
- [ ] W9–W10: báo cáo, slide, video demo, v1.0

## Hướng dẫn chạy DevSecOps, Docker & Kiểm thử (Dành cho GVHD / Chấm điểm Tuần 7 – `v0.9`)

Toàn bộ khóa bí mật (`HMAC_SECRET`) và URL đích (`APPS_SCRIPT_URL`) **tuyệt đối không hardcode trong mã nguồn Unity/Python hay bản build** mà chỉ nạp qua biến môi trường hoặc tệp cấu hình cục bộ (đã nằm trong `.gitignore`).

### 1. Cấu hình biến môi trường cục bộ
Sao chép tệp mẫu thành `.env` (dành cho Docker Proxy) và tùy chọn tạo `telemetry_runtime_config.json` (dành cho client Unity khi gửi sang proxy cục bộ):

```bash
# Linux / macOS / Git Bash
cp .env.example .env
cp telemetry_runtime_config.example.json telemetry_runtime_config.json

# Windows PowerShell
Copy-Item .env.example .env
Copy-Item telemetry_runtime_config.example.json telemetry_runtime_config.json
```

> Lưu ý: Đặt cùng giá trị bí mật cho `HMAC_SECRET` trong `.env` và `hmacSecretKey` trong `telemetry_runtime_config.json` (hoặc biến môi trường `DR_TELEMETRY_HMAC_KEY`). Mặc định `DRY_RUN=true` để xác thực và ghi log kiểm toán cục bộ mà không cần URL Google Sheets thật.

### 2. Khởi chạy Telemetry Security Proxy bằng Docker
**Cách 1 — Dùng Docker Compose (khuyến nghị):**
```bash
docker compose up --build -d
docker compose ps
```

**Cách 2 — Dùng `docker build` và `docker run` thuần:**
```bash
docker build -t death-relapse-telemetry:v0.9 .
docker run -d --name death-relapse-telemetry-proxy \
  --env-file .env \
  -p 8080:8080 \
  death-relapse-telemetry:v0.9
```

Dừng container sau khi kiểm tra:
```bash
docker compose down
# hoặc: docker rm -f death-relapse-telemetry-proxy
```

### 3. Kiểm tra `/health`, Demo chặn tấn công & Đo thực tế NFR-05
```bash
# Kiểm tra sức khỏe dịch vụ
curl -i http://127.0.0.1:8080/health

# Chạy kịch bản minh chứng bảo mật (200 / 401 / 400 / 429) và đo độ trễ p50/p95 NFR-05
python telemetry-service/scripts/attack_demo_and_p95.py --url http://127.0.0.1:8080 --secret change-me-local-secret
```

### 4. Chạy bộ kiểm thử tự động cục bộ (giống GitHub Actions CI)
```bash
# 1) Kiểm thử C# thuần (.NET 8 NUnit link trực tiếp Assets/Scripts/Core/TelemetrySecurityCore.cs)
dotnet test ci/DeathRelapse.CoreTests/DeathRelapse.CoreTests.csproj --verbosity normal

# 2) Kiểm thử bảo mật Python FastAPI (pytest)
pip install -r telemetry-service/requirements.txt
pytest -v telemetry-service/tests/
```

### 5. Danh sách tính năng đóng băng `v0.9`
- **Không thêm gameplay mới** trong giai đoạn đóng băng: giữ nguyên 1 đấu trường, 1 Boss (2 đòn đánh), 1 Knight (combo 2 nhát + lăn né I-frame).
- **Bảo mật 2 lớp:** Telemetry Security Proxy (HMAC-SHA256, chống Replay 300s, allow-list schema, chặn Formula Injection, bất biến `T == H + D + M`, Rate Limit 10 req/phút) + chống sửa file log cục bộ (`recordHash` + vá **TC-12** gửi bù khi Replay).
- **3 Cải tiến UX playtest:** Onboarding Overlay (phím `H`), đánh bóng màn hình `THẤT BẠI` + dòng nhắc mở khóa Replay, thay toàn bộ SFX bằng 8 tệp `.mp3` sạch bản quyền + bước chân Knight đồng bộ Animation Event.
- **Đo NFR & CI/CD:** `NfrBenchmarkRecorder` (`F9`), 14/14 Unity EditMode Tests + 10/10 .NET CI Core Tests + 13/13 Python Security Tests + Docker Smoke Test trên GitHub Actions.

## Asset và bản quyền

Toàn bộ asset bên thứ ba và license được ghi trong `CREDITS.md`.

## Khai báo sử dụng AI

Có dùng AI hỗ trợ soạn thảo, học và viết code; chi tiết trong Nhật ký AI. Commit do AI sinh phần lớn có tiền tố `[AI]`.
