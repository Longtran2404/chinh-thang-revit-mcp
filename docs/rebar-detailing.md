# Bố trí cốt thép và liên kết thép

Ba công cụ mới thuộc toolset `structural`:

- `revit_get_detailing_catalog`: đọc đường kính, đường kính uốn, lớp bảo vệ và loại liên kết thực tế trong dự án; kiểm tra phần tử có thể làm host.
- `revit_create_rebar_path`: tạo **Autodesk Revit Rebar thật** theo các đỉnh đường tim tính bằng mm. Dùng cho neo L/U/Z, đầu sàn bẻ vào dầm, thép dầm/cầu thang gấp khúc, thép mũ, chân kê và chân chó.
- `revit_create_steel_connection`: tạo native detailed connection từ loại đã nạp; phần tử đầu tiên là phần tử chính. Không thay bằng ký hiệu liên kết generic. Cần dịch vụ liên kết của Revit. Việc tạo handler không chứng nhận hình học bản mã/bu lông đã sinh đầy đủ hoặc khả năng chịu lực.

## Đường thép và bố trí

Trước tiên đọc host, cao độ, lớp bảo vệ, mặt trên/dưới, tiết diện dầm và đường kính thực tế. Tọa độ đầu vào là **đường tim thanh**, không phải mặt bê tông. Tính vị trí từ lớp bảo vệ cộng nửa đường kính; đai và các lớp thép khác cần được xét riêng. Công cụ không tự lấy chiều dài neo/nối theo một hệ số mặc định.

`points_json` chứa chuỗi các đỉnh trước khi bo uốn. Revit áp dụng đường kính uốn của bar type. Các nhánh quá ngắn so với bán kính uốn bị từ chối. Không đưa hai đoạn thẳng hàng liên tiếp, đoạn quay ngược hoặc đoạn ngắn dưới 1 mm.

`ShapeDriven` dùng đường thép đồng phẳng. `normal_x/y/z` là pháp tuyến mặt phẳng và hướng dương rải thép. Có thể chỉnh shape/thông số/ràng buộc trực tiếp trong Revit. Không dùng DirectShape hay ModelLine thay cốt thép.

`FreeForm` dùng đường thép không gian, ví dụ chân chó có chân xoay lệch mặt phẳng. Các thanh được đặt theo hướng normal đã chỉ định. **Revit 2024 tạo loại này không có ràng buộc mặt host và không thể bổ sung ràng buộc đó sau này**. Vẫn là Rebar có host, bar type và thống kê; chỉnh đường bằng `RebarFreeFormAccessor.SetCurves` hoặc các thao tác FreeForm được Revit hỗ trợ. Không hứa có các tay nắm kích thước như ShapeDriven.

- `Single`: quantity=1, distribution_length_mm=0, spacing_mm=0.
- `FixedNumber`: quantity>=2 và chiều dài vùng rải rõ ràng. Khoảng cách = chiều dài/(quantity-1); spacing_mm=0.
- `MaximumSpacing`: nhập chiều dài vùng rải và khoảng cách tối đa; để quantity=1. Hai đầu đều có thanh, bước thực tế có thể nhỏ hơn yêu cầu.
- `dry_run=true`: tạo và kiểm tra hình học rồi rollback; không trả ID giả của phần tử đã hủy.
- Một lần tối đa 1.000 vị trí; tối đa 128 đỉnh/thanh. Giao dịch lỗi/cảnh báo bị rollback, không để bộ thép dở dang.

## Ví dụ hình học (không phải chi tiết thiết kế)

Thay host/type và toàn bộ tọa độ bằng dữ liệu dự án. Các kích thước dưới đây chỉ để thử chức năng:

```json
{
  "host_id": 123,
  "bar_type_id": 456,
  "points_json": "[[0,0,300],[0,0,0],[3000,0,0],[3000,0,300]]",
  "normal_x": 0, "normal_y": 1, "normal_z": 0,
  "mode": "ShapeDriven",
  "layout_rule": "FixedNumber",
  "quantity": 6, "distribution_length_mm": 1000,
  "dry_run": true
}
```

- Cầu thang có chiếu nghỉ: `[[0,0,0],[500,0,0],[2000,0,1500],[2500,0,1500]]`, normal `[0,1,0]`.
- Chân kê phẳng: `[[0,0,0],[200,0,0],[200,0,200],[600,0,200],[600,0,0],[800,0,0]]`, normal `[0,1,0]`.
- Chân chó không gian: `[[0,0,0],[200,0,0],[200,0,200],[200,400,200],[200,400,0],[400,400,0]]`, mode `FreeForm`.

Thanh neo qua vùng sàn–dầm vẫn có **một host chính**; phải kiểm tra vùng giao và khả năng chỉnh host thực tế. Chưa có tự động dò mặt để phân phối thép toàn bộ tầng, thiết kế neo/nối theo tiêu chuẩn, chia vùng gối/nhịp, kiểm tra va chạm giữa mọi thanh, hoặc tự động triển khai bản vẽ gia công. Phải kiểm tra các bước này riêng trước khi phát hành.
