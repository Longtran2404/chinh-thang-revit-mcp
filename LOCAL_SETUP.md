# Cài Chính Thắng Revit MCP trên Windows

## Build từ source

Cần Windows, Revit 2024 và .NET 8 SDK. Mở PowerShell tại thư mục source:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/build-local.ps1
```

Script chạy test, build add-in và tạo server độc lập trong `artifacts/ChinhThangRevitMcp-1.0.0-win-x64/`, kèm ZIP và SHA-256. Máy sử dụng bộ ZIP không cần .NET SDK. Không phân phối Revit API DLL, mô hình hay khóa truy cập.

## Cài trên máy

Đóng Revit trước khi cài. Tại thư mục source:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/install-local.ps1 -Client codex -WhatIf
powershell -ExecutionPolicy Bypass -File scripts/install-local.ps1 -Client codex
```

Nếu dùng ZIP: giải nén rồi chạy `install-local.ps1` ngay trong thư mục giải nén với các tham số tương tự.

- Server và add-in: `%LOCALAPPDATA%\ChinhThangRevitMcp\app\`.
- Khai báo Revit: `%APPDATA%\Autodesk\Revit\Addins\2024\ChinhThangRevitMcp.addin`.
- Codex: thêm duy nhất `[mcp_servers.chinh-thang-revit-mcp]` vào `%USERPROFILE%\.codex\config.toml`.
- Cấu hình cũ được sao lưu với hậu tố `.ctmcp-backup-<timestamp>`; chương trình cũ có bản `.backup-<timestamp>`.
- Dùng `-Client none` nếu muốn tự cấu hình MCP client. Server dùng stdio, tham số `--target 2024 --toolsets all`.
- Dữ liệu runtime giữ định dạng upstream tại `%LOCALAPPDATA%\RvtMcp`. Không cài đồng thời add-in upstream trong cùng phiên Revit.

## Kiểm tra sử dụng thật

1. Mở Revit 2024, mở một mô hình thử nghiệm.
2. Trong tab **Add-Ins**, kiểm tra panel **Chinh Thang MCP**, trạng thái **MCP: ON**.
3. Mở lại Codex để tải cấu hình MCP mới.
4. Yêu cầu: “Dùng chinh-thang-revit-mcp đọc thông tin view đang mở, chưa sửa mô hình.”

Có thể kiểm tra trực tiếp bằng Python 3:

```powershell
python scripts/smoke-test.py "$env:LOCALAPPDATA\ChinhThangRevitMcp\app\server\RvtMcp.Server.exe"
python scripts/smoke-test.py "$env:LOCALAPPDATA\ChinhThangRevitMcp\app\server\RvtMcp.Server.exe" --live
```

Lệnh đầu kiểm tra handshake và 229 tools. Lệnh `--live` phải trả về view thực trong Revit mới chứng minh kết nối đầu cuối hoạt động. Việc build/test thành công không thay thế bước này.

MCP này chạy cục bộ, không yêu cầu khóa API của nhà cung cấp AI. AI client bạn chọn vẫn sử dụng cơ chế tài khoản riêng của client. Adaptive bake mặc định tắt; muốn tự động rút gọn code cần tự cung cấp implementation `ICodeCondenser`.

## Gỡ và khôi phục

Đóng Revit và ngắt MCP trước. Xóa riêng `ChinhThangRevitMcp.addin`, xóa mục `[mcp_servers.chinh-thang-revit-mcp]` trong Codex, sau đó xóa thư mục `app` của bản này. Không xóa toàn bộ thư mục Addins hoặc cấu hình Codex. Các bản sao lưu có thể dùng để khôi phục phiên bản trước.

Tham khảo cấu hình MCP chính thức: https://developers.openai.com/codex/mcp
