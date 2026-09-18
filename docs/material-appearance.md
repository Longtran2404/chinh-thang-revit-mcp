# Vật liệu render và PBR trong Chính Thắng Revit MCP

## Hai lớp vật liệu khác nhau

Graphics (`Color`, `Shininess`, `Smoothness`, transparency, patterns) điều khiển một phần hiển thị Revit. **Appearance Asset** điều khiển bề mặt render. Không dùng giá trị Shininess để tuyên bố đã gán roughness, normal hoặc các map PBR.

Bản riêng mở rộng hai tool hiện có:

- `revit_get_material_properties(includeAssets=true)`: trả schema, tên property thực, kiểu dữ liệu, giá trị và connected assets. Tối đa 512 properties, 5 tầng; có cờ nếu bị cắt ngắn. `read_only_snapshot` mô tả bản đọc, không có nghĩa property chắc chắn không sửa được trong EditScope.
- `revit_set_material_appearance`: nhận `appearanceEditsJson`, `sourceAppearanceAssetId`, `duplicateAppearanceAsset` và `dryRun`. Mọi chỉnh sửa nằm trong cùng transaction; một thuộc tính sai làm rollback toàn bộ.

Mặc định sao chép Appearance Asset để không thay đổi vật liệu khác đang dùng chung. Nếu tắt sao chép mà asset đang được chia sẻ, tool từ chối. Muốn chỉnh tiếp một asset riêng có thể dùng `duplicateAppearanceAsset=false`.

## Phạm vi kênh phải xem xét

| Thành phần | Yêu cầu |
|---|---|
| Base color / albedo / tint | Màu tuyến tính hoặc sRGB theo schema và nguồn texture; không tự suy đoán color space |
| Roughness / glossiness | Scalar hoặc map đúng schema; hai đại lượng không luôn tương đương vật lý |
| Highlights / specular / reflectivity | Kiểm tra trực tiếp, góc xiên/Fresnel và màu phản xạ; Generic có Highlights dạng boolean |
| Metalness | Chọn schema metal phù hợp; không coi boolean Generic Highlights là metalness map liên tục |
| Normal / bump | Phân biệt tangent-space normal với height map, trục/handedness, cường độ và kích thước |
| Displacement / height | Chỉ gán khi schema/renderer có hỗ trợ; không đổi sang bump mà giấu khác biệt |
| Opacity / cutout | Phân biệt cắt thủng với độ trong suốt và đúng chiều map |
| Transmission / translucency / IOR | Chọn asset phù hợp kính, vật liệu mờ hoặc trong; dùng giới hạn do Revit kiểm tra |
| Emission | Màu, texture, luminance, nhiệt độ màu theo schema; không coi ảnh sáng là đèn |
| Anisotropy / rotation / clearcoat | Đọc thuộc tính của Advanced asset, không giả định Generic có đầy đủ |
| AO và map đóng gói | Giữ nguồn; chỉ tách/chuyển khi biết rõ quy ước kênh. Không mặc định nhân AO vào albedo |
| UV / physical scale | Kích thước thật, offset, rotation, repeat; map cùng bộ phải khớp nhau |

**Không phải mọi schema Revit hỗ trợ mọi kênh.** Tool chỉnh theo property thực tế, hỗ trợ bool, integer/enum, float/double/distance, string, vector3 và color4, bitmap UnifiedBitmap cùng transforms. Loại property khác bị từ chối rõ ràng. Đây là trình chỉnh Appearance Asset có kiểm tra, không phải bộ chuyển đổi mọi loại PBR tự động.

## Quy trình

1. Chọn vật liệu hoặc asset mẫu đúng họ: gỗ, sơn, vải, đá, kim loại, kính… Đọc asset trước.
2. Lập bảng map đã có, map thiếu, scalar thay thế, nguồn/giấy phép và color space. Không bịa map còn thiếu. Ảnh tạo chỉ là nguồn tham khảo hình ảnh; vật liệu đo đạc cần nguồn kỹ thuật.
3. Sao chép/chọn asset phù hợp. Dùng đúng `path` trong kết quả đọc, không dịch tên property từ UI.
4. Gửi edit với `dryRun=true`: chạy xác thực Revit và rollback; nếu đạt mới áp dụng.
5. Đọc lại giá trị và kiểm tra Realistic/render; xác nhận bitmap tồn tại, scale đúng và vật liệu khác không bị thay đổi.
6. Lưu/reopen file thử, kiểm tra đường dẫn texture và bàn giao cùng tài nguyên có giấy phép.

Ví dụ cho **Generic**, chỉ sau khi kết quả đọc xác nhận property có tồn tại:

```json
[
  {"path":["generic_glossiness"],"value":0.6},
  {"path":["generic_is_metal"],"value":false},
  {"path":["generic_diffuse"],"bitmap":{"file":"C:/Textures/wood/albedo.png","scaleXmm":1200,"scaleYmm":1200,"rotationDegrees":0,"repeatU":true,"repeatV":true}},
  {"path":["generic_bump_map"],"bitmap":{"file":"C:/Textures/wood/height.png","scaleXmm":1200,"scaleYmm":1200}}
]
```

Giá trị distance chỉnh trực tiếp qua `value` dùng `unit_type_id` đã đọc từ schema; các tham số bitmap `scaleXmm/scaleYmm/offsetXmm/offsetYmm` luôn là mm và được chuyển sang đơn vị của property. Angle bitmap dùng độ. Không có chuyển roughness ↔ glossiness âm thầm.

Nguồn đối chiếu: RevitAPI.xml của Revit 2024 đã cài, [Autodesk AppearanceAssetEditScope](https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/743c74ba-12de-4d77-a677-325229525955.htm) và [Autodesk Generic schema](https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/dd16eb59-16ec-f121-289b-a69d26d7c789.htm). Tài liệu web là phiên bản mới hơn; build đích dùng API 2024.
