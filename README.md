<!-- Modified for Chinh Thang Revit MCP, 2026-09-18. See FORK_CHANGES.md. -->
# Chính Thắng Revit MCP

![MCP tools](https://img.shields.io/badge/MCP-232%20tools-blue)
![License](https://img.shields.io/badge/license-Apache%202.0-green)

MCP chạy cục bộ để kết nối AI client với Autodesk Revit. Bản riêng của **Minh Long Tran / Chính Thắng**, phát triển từ [bimwright/rvt-mcp](https://github.com/bimwright/rvt-mcp).

## Bản này có gì?

- Server C# .NET 8 độc lập và add-in Revit; bộ build/cài riêng cho **Revit 2024, Windows x64**.
- Kết nối Codex hoặc MCP client hỗ trợ stdio, không yêu cầu khóa API của nhà cung cấp AI trong server.
- Panel **Chinh Thang MCP** trên tab Add-Ins của Revit.
- Đọc mô hình, tạo/sửa phần tử, kết cấu, MEP, bản vẽ, bảng thống kê, xuất dữ liệu và chạy C# qua Revit API.
- Giữ cơ chế Revit transaction/undo, token xác thực và giao tiếp localhost của upstream.
- Source còn các target Revit 2022–2027 từ upstream; bản này chỉ đóng gói và ưu tiên kiểm tra 2024.

## Bắt đầu

Xem [hướng dẫn build và cài trên máy](LOCAL_SETUP.md).

```powershell
powershell -ExecutionPolicy Bypass -File scripts/build-local.ps1
powershell -ExecutionPolicy Bypass -File scripts/install-local.ps1 -Client codex -WhatIf
powershell -ExecutionPolicy Bypass -File scripts/install-local.ps1 -Client codex
```

Đóng Revit trước khi cài. Sau đó mở Revit với mô hình thử, mở lại Codex và gọi `revit_get_current_view_info` để xác nhận kết nối thực.

## Quy tắc nội thất và vật liệu

Tường và cửa dùng đối tượng Revit bản địa; đồ nội thất là Family có tham số, chỉnh trực tiếp bằng Edit Family/Edit Type. Texture có thể lấy từ ảnh tạo hoặc thư viện có giấy phép và gán vào Material thực. Đọc [quy tắc dựng BIM](docs/native-modeling-policy.md), cũng được server cung cấp qua `revit://guidance/native-modeling`.

Xem thêm [material/PBR](docs/material-appearance.md) và [toàn bộ quy trình BIM](docs/bim-workflow.md). Bản riêng bổ sung đọc/chỉnh Appearance Asset thực, texture và kích thước ảnh, có dry-run và rollback.

## Công cụ

| Chế độ | Số tools | Ghi chú |
|---|---:|---|
| Mặc định | **40** | query, create, view, meta |
| `--toolsets all` | **232** | Bộ cài bản riêng dùng chế độ này |
| `all` + adaptive bake | **235** | Thêm quản lý gợi ý; mặc định tắt |

Dùng `--read-only` nếu chỉ muốn đọc. Đơn vị chiều dài tại biên MCP là mm. AI client vẫn có thể gửi nội dung tool cho dịch vụ AI mà bạn đang sử dụng.

Adaptive bake không có nhà cung cấp rút gọn code được cấu hình sẵn. Có thể dùng preset/macro cục bộ; tính năng rút gọn code cần implementation `ICodeCondenser` do bạn chủ động cung cấp.

## Cấu trúc

- `src/server`: server MCP, độc lập Revit.
- `src/plugin-r24`: add-in Revit 2024.
- `src/shared`: handlers và hạ tầng Revit dùng chung.
- `tests/RvtMcp.Tests`: bộ kiểm thử không cần chạy Revit.
- `scripts/build-local.ps1`: build, test, đóng gói.
- `scripts/install-local.ps1`: cài bản riêng và thêm cấu hình Codex tùy chọn.
- `scripts/smoke-test.py`: kiểm tra giao thức thật; thêm `--live` để kiểm tra Revit.

## Kiểm tra

Xem [kết quả kiểm chứng](docs/verification.md): 509 unit tests, build Revit 2024, 232 tools qua MCP, đọc view thật và kiểm tra Appearance Asset/PBR trên file thử riêng.

## Giấy phép và nguồn gốc

[Apache-2.0](LICENSE). Giữ bản quyền upstream của Khoa Le, xem [NOTICE](NOTICE) và [các thay đổi của bản riêng](FORK_CHANGES.md).

Đây là bản phái sinh độc lập, không phải sản phẩm chính thức của Autodesk hay bimwright. Không đưa Revit DLL, dữ liệu công trình, token hoặc cấu hình cá nhân lên GitHub.

## Bố trí thép chi tiết

Bản 1.1 bổ sung Rebar nhiều đoạn cho neo, cầu thang gấp khúc, sàn bẻ đầu và chân kê/chân chó; danh mục detailing và tạo native detailed steel connection. Xem [cách dùng và giới hạn](docs/rebar-detailing.md).
