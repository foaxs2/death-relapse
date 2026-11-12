# CREDITS.md – Danh sách ghi nhận tác giả và bản quyền Asset (Third-Party Assets)

Tài liệu này ghi nhận toàn bộ các tài nguyên bên ngoài (3D model, animation, âm thanh...) được sử dụng trong dự án game **Death Relapse** nhằm đảm bảo tính minh bạch học thuật và tuân thủ các điều khoản cấp phép bản quyền phần mềm / nội dung số theo quy định tại `GEMINI.md`.

---

## 1. 3D Models

### 1.1. Model Hiệp sĩ (Player Character)
- **Tên tác phẩm gốc:** Medival armored stylyzed Knight
- **Tác giả:** ValeriyUA ([@surferdude](https://sketchfab.com/surferdude))
- **Nguồn (Source URL):** [Sketchfab - Medival armored stylyzed Knight](https://sketchfab.com/3d-models/medival-armored-stylyzed-knight-208e38632aee4008854785ce2c81e1f8)
- **Loại giấy phép:** [Creative Commons Attribution 4.0 International (CC BY 4.0)](http://creativecommons.org/licenses/by/4.0/)
- **Các chỉnh sửa đã thực hiện:** 
  - Gỡ bỏ bộ khung xương gốc (rig cũ).
  - Thực hiện auto-rigging lại hệ thống xương thông qua Adobe Mixamo để chuẩn hóa Humanoid Avatar và đồng bộ với bộ animation chuyển động.

### 1.2. Model Boss (Stelae Knight)
- **Tên tác phẩm gốc:** Stelae knight
- **Tác giả 3D:** Muru ([@muru](https://sketchfab.com/muru))
- **Ý tưởng thiết kế (Concept Art):** Shuohan ([ArtStation - Concept Art](https://www.artstation.com/artwork/KE3Yr))
- **Nguồn (Source URL):** [Sketchfab - Stelae knight](https://sketchfab.com/3d-models/stelae-knight-0a795ae613f2496e955974ca37bf1e94)
- **Loại giấy phép:** [Creative Commons Attribution 4.0 International (CC BY 4.0)](http://creativecommons.org/licenses/by/4.0/)
- **Các chỉnh sửa đã thực hiện:**
  - Gỡ bỏ bộ khung xương gốc để chuẩn bị auto-rig lại bằng Adobe Mixamo.
  - Chỉnh sửa hình học: tách riêng vũ khí (thanh kiếm) ra khỏi phần thân gốc nhằm thuận tiện cho việc gắn socket vũ khí lên xương bàn tay và thiết lập hitbox đòn đánh độc lập.

---

## 2. Animations

### Animation chuyển động và chiến đấu (Player & Boss)
- **Tác giả / Nhà cung cấp:** Adobe Systems Inc. (Adobe Mixamo)
- **Nguồn (Source URL):** [Mixamo](https://www.mixamo.com/)
- **Các clip đang sử dụng:** 
  - Hiệp sĩ: `idle.fbx`, `Run with sword.fbx`, `SlashNgangNgoaiVao.fbx`, `Slash2.fbx`, `Gunplay.fbx`
  - Các animation bổ sung trong tương lai cho Boss và thao tác lướt né (Dash/Roll).
- **Loại giấy phép:** Adobe Software Licensing Agreement (Royalty-free – được phép sử dụng miễn phí cho các ứng dụng và game 2D/3D phi thương mại lẫn thương mại).
- **Các chỉnh sửa đã thực hiện:** Thiết lập chuẩn Humanoid Avatar trong Unity, cấu hình Animator Controller và điều khiển chuyển động qua code CharacterController (không dùng Root Motion).

## 3. SWORDS

**Moonbrand (Early 14th C Arming Sword)** by [The Oakeshott Institute](https://sketchfab.com/oakeshott)
  - Source: [Sketchfab](https://sketchfab.com/3d-models/moonbrand-early-14th-c-arming-sword-80fa91fc8c974fd0acd2a55cc513edec)
  - License: [Creative Commons Attribution 3.0 Unported (CC BY 3.0)](http://creativecommons.org/licenses/by/3.0/)