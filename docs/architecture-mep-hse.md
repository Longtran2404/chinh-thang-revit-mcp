# Kiến trúc, MEP và HSE

Các công cụ dưới đây thuộc `workflows`, được bật trong bộ cài `--toolsets all`. Chúng bổ sung kiểm tra và theo dõi, không thay thế thiết kế chuyên ngành hoặc nghiệm thu hiện trường.

## Kiến trúc

`revit_audit_architecture_readiness(element_ids?, limit=100)` đọc phòng chưa đặt/chưa kín hoặc trùng, family in-place/không mở Edit Family được, cửa không có host, vật liệu thiếu Appearance Asset. Có ID phần tử để chọn và sửa bằng các công cụ Revit hiện có. Family mở được chưa chứng minh hình học có tham số: `family_flex_verified` luôn false cho đến khi kiểm tra flex riêng. Việc có Appearance Asset chưa chứng minh đủ texture/PBR.

Không truyền ID: quét category tường, sàn, trần, mái, cửa, cửa sổ, nội thất, phòng. Import/DirectShape trong category khác cần truyền ID cụ thể. Kiểm tra chỉ đọc, không tự sửa hay xóa.

## MEP

`revit_audit_mep_readiness(element_ids?, limit=100)` trả về đầu connector vật lý, kích thước thực, đầu hở, hệ thống của tuyến và độ dốc hình học tuyệt đối theo phần trăm. Tuyến đứng có `vertical=true`, độ dốc phần trăm null; tuyến cong không bị coi là tuyến thẳng. Độ dốc không xác định chiều chảy. Đầu hở có thể là đầu chờ hợp lệ, không tự kết luận lỗi.

Dùng kết quả cùng các tool tạo duct/pipe/conduit/cable tray, fitting, kết nối và kiểm tra va chạm đã có. Chưa tính tải lạnh, tổn thất áp, chọn bơm/quạt, chọn dây hoặc phối hợp bảo vệ.

## HSE

`revit_audit_hse_readiness(element_ids?, limit=100)` lấy các vòng biên mặt trên sàn: biên ngoài và lỗ mở đều là ứng viên cần kiểm tra. Trả về tọa độ, chiều dài, loại đường cong và phase của phần tử. Không tự suy ra chiều cao rơi, thiếu lan can hay trạng thái an toàn từ một vòng biên. Chưa bao phủ scaffold, phương án cẩu, PPE, sức khỏe nghề nghiệp hoặc môi trường.

`revit_hse_issue_register(action="list", start=0, limit=100)` đọc sổ HSE lưu trong chính RVT. Danh sách có phân trang, không trả toàn bộ lịch sử dài trong một lần gọi.

Ví dụ ghi vấn đề bằng `action="upsert"`, `issue_json`:

```json
{"issue_key":"S1-OPENING-01","title":"Kiểm tra bảo vệ lỗ mở sàn S1","hazard":"FloorOpening","status":"Open","element_ids":[],"owner":"","notes":"Gắn ID sàn thực tế trước khi đối chiếu mô hình."}
```

- Hazard: `FallEdge`, `FloorOpening`, `Access`, `Lifting`, `Electrical`, `Fire`, `Other`.
- Trạng thái: `Open`, `InProgress`, `Resolved`. `InProgress` cần owner; `Resolved` cần owner, reviewer và evidence. Công cụ lưu thông tin người dùng cung cấp, không tự xác nhận bằng chứng.
- `element_ids` phải có các ID instance tồn tại; `[]` dành cho vấn đề hiện trường chưa gắn mô hình. Lưu thêm UniqueId để truy vết.
- `due_date` nếu có dùng `yyyy-MM-dd`; không gửi thông báo ra ngoài.
- Cùng issue_key cập nhật bản ghi, giữ tối đa 20 lần sửa; khi đạt giới hạn công cụ dừng để tránh âm thầm bỏ lịch sử. Tối đa 500 vấn đề/dự án. Undo bằng Revit.

Các audit giới hạn 1–500 phần tử/lần, trả `truncated` khi còn phần tử chưa đọc; HSE giới hạn 500 đoạn biên/sàn và báo riêng khi cắt ngắn. Linked models không nằm trong phạm vi, phase phải được người dùng đối chiếu với giai đoạn thi công. Lỗi từng phần tử được trả là `NOT_INSPECTED`, không tính là đạt.
