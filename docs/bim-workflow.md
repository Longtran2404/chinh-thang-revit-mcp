# Phạm vi và tiêu chí chất lượng BIM

Định hướng bản riêng bao phủ chuỗi công việc: khảo sát dữ liệu đầu vào → dựng mô hình bản địa → thư viện tham số → vật liệu → hồ sơ → thống kê → kiểm tra → bàn giao. Đây là yêu cầu chất lượng; không phải tuyên bố mọi chức năng sau đã có typed tool hay đã nghiệm thu trực tiếp.

| Nhóm | Dữ liệu và hành vi cần bảo toàn |
|---|---|
| Kiến trúc | Levels, grids, walls, floors, roofs, ceilings, compound layers, joins, openings, rooms, phases và design options |
| Cửa / mặt dựng | Family đúng category/host, kích thước, chiều mở, sill/head, opening cut, vật liệu khung/kính/phụ kiện, curtain panels và mullions |
| Nội thất | Loadable parametric Family, nested family khi cần, constraints, type/instance, visibility theo detail level, material parameters; sửa được trong Family Editor |
| Kết cấu / MEP | Category và hệ thống đúng, host/connectors và tham số kỹ thuật từ nguồn thật; không dùng hình học nhìn giống để thay mô hình chuyên ngành |
| Vật liệu | Identity + Graphics + Appearance/PBR + physical/thermal properties khi có dữ liệu; theo material-appearance.md |
| Hồ sơ | View/template, dimensions, tags, details, schedules, sheets và titleblocks; thống nhất tên, tỷ lệ, số hiệu |
| Khối lượng | Category, type, classification và đơn vị; kiểm tra material quantities, bảng thống kê và nguồn tính |
| Thư viện | .rfa, type catalog khi cần, thumbnail, version Revit, category/host, tham số, nguồn/license, đường dẫn texture tương đối trong bộ bàn giao |
| QA | Flex family, host/cut, constraints, warning, join, missing texture, duplicate assets, scale, kiểm tra reopen và đối chiếu hồ sơ |

Mỗi công việc phải có đầu vào, đầu ra chỉnh sửa được và tiêu chí kiểm tra. Không thay đối tượng BIM yêu cầu bằng proxy hoặc ảnh để đạt hình thức. Phân biệt rõ kết quả tạo được, kiểm tra được bằng API và kiểm tra trực quan trong Revit.

Các thiếu hụt quan trọng hiện tại: chưa có bộ authoring Family Editor tổng quát, chưa tích hợp tìm/tải/tạo bộ map PBR tự động, chưa có chứng nhận cho mọi phiên bản Revit/renderer. Dùng công cụ client hoặc Revit API có kiểm tra khi phù hợp; báo rõ nếu chưa thể hoàn thành.
