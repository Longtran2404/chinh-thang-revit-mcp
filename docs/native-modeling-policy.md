# Chính Thắng — quy tắc dựng BIM và vật liệu

## Dùng đối tượng Revit có thể chỉnh sửa

- Tường: dùng Wall/WallType bản địa, cấu tạo lớp và Material đúng chức năng; chỉnh chiều cao, chiều dày, host, join bằng Revit.
- Cửa đi/cửa sổ: dùng loadable Family đúng category Doors/Windows và template host tương ứng. Opening/void phải cắt host thật; kiểm tra hướng mở, flip và cao độ.
- Nội thất: mỗi loại đồ là loadable Family `.rfa`, có thể chọn **Edit Family**, không chỉ một khối mesh nhập vào `.rfa`. Dựng hình bằng extrusion, blend, sweep, revolve, void hoặc nested family có thể sửa.
- Dùng reference planes, constraints và tham số chiều rộng/sâu/cao, độ dày, vật liệu và visibility phù hợp. Phân biệt rõ type parameters với instance parameters. Chỉnh bằng Edit Type hoặc Properties phải làm hình học thay đổi thực sự.
- Chi tiết: dùng Detail Components, profile, model geometry, dimensions và tags bản địa tùy mục đích; chi tiết cần thống kê hoặc xuất BIM phải có category/parameter phù hợp.
- Không thay các đối tượng trên bằng DirectShape, ImportInstance, mesh hoặc ảnh giả 3D. Nếu thiếu Family, tạo hoặc tìm Family chỉnh sửa được và kiểm tra trước khi dùng. Nếu chưa thực hiện được, báo thiếu và không tuyên bố hoàn thành.

## Vật liệu và texture

1. Kiểm tra và tái sử dụng Material trong dự án trước, tránh tạo bản trùng tên.
2. Thiếu texture thì tìm nguồn có giấy phép phù hợp hoặc tạo ảnh bằng công cụ image generation. Việc này do AI client thực hiện qua công cụ của client; MCP server không tự gọi một dịch vụ tạo ảnh.
3. Texture từ web phải ghi nguồn, giấy phép và ngày tải. Không đóng gói lại texture bên thứ ba vào kho public nếu giấy phép không cho phép.
4. Texture dùng làm bề mặt nên liền mạch, không phối cảnh, không ánh sáng/bóng đổ mạnh; đặt kích thước vật lý và hướng vân đúng đơn vị.
5. Lưu texture vào thư mục ổn định trong bộ bàn giao. Tạo hoặc sửa Material thật trong Revit; thiết lập Graphics và Appearance Asset, đường dẫn bitmap và real-world scale. Chỉ gán màu RGB chưa phải là vật liệu có texture.
6. Gán Material qua tham số Family/Type hoặc cấu tạo lớp của system family; kiểm tra Realistic/render và liên kết ảnh sau khi mở lại.
7. Ảnh tạo không thay thế mô hình Family; không suy diễn thông số kỹ thuật, chứng chỉ hoặc hiệu năng vật liệu từ ảnh.

## Tiêu chí nghiệm thu

- Kiểm tra category, Family, Type, host và Level đúng; không có khối proxy thay đối tượng yêu cầu.
- Mở Edit Family, đổi ít nhất hai kích thước, flex Family rồi xác nhận hình học cập nhật và không phát sinh lỗi constraint.
- Kiểm tra tối thiểu hai Type nếu Family có biến thể, và thay Material parameter để xác nhận gán vật liệu hoạt động.
- Lưu `.rfa` và mô hình thử riêng; nạp lại để kiểm tra. Không sửa bản gốc hoặc công bố đã nghiệm thu khi chỉ kiểm tra ảnh preview.
- Bàn giao `.rvt`/`.rfa`, texture được phép bàn giao và bảng nguồn texture; ghi rõ những gì chưa kiểm tra.

## Khả năng của phiên bản hiện tại

Các tool có sẵn hỗ trợ đọc/nạp/đổi tên/xuất Family, đặt FamilyInstance và sửa tham số. `create_material` hiện tạo Material với thuộc tính graphics; không tự tải hoặc gán texture Appearance.

Bản riêng đã mở rộng đọc/chỉnh Appearance Asset và texture theo schema qua `get_material_properties` / `set_material_appearance`, gồm dry-run và rollback. Xem `material-appearance.md` để phân biệt Graphics với render/PBR và phạm vi từng kênh.

Source upstream chưa có bộ tool chuyên dụng bao phủ toàn bộ Family Editor hay dịch vụ tìm/tạo texture. Với trường hợp thiếu typed tool, có thể triển khai C# qua `revit_send_code_to_revit` sử dụng Revit API hoặc thao tác trực tiếp trong Revit; phải kiểm tra đúng version, transaction, document context và kết quả thực. Việc có quy tắc này không có nghĩa các khả năng còn thiếu đã được lập trình hoặc nghiệm thu.
