# AGENTS.md — BM-ThuDiem

Chương trình riêng, bản **1.3.2**. Chỉ nghe COM4. Không nghe Kingbird. Không mở COM5.

Đích chung của dự án nằm ở whitepaper repo cha: mẫu củ mì ra một điểm bột khớp đèn máy và sổ cũ, không gõ điểm trên bàn.

## Luật đang chạy

- Khung `NNNNNN=` là gram viết ngược. `075000=` = 570 g.
- Khô: mẫu gần 5000 g nhất trong 4200–5800 g.
- Ướt: trung vị cao nguyên 150–1200 g. Thường 300–700 g. Phụ lục đúng 5 kg là 280 g = 10% đến 670 g = 30%.
- Điểm = tra Quyết định 228 sau khi quy `5000 × ướt / khô`. 522 g = 22,4%.
- Không tính khô trừ ướt. Không hủy phiếu chỉ vì nước rung.
- Cân dưới 100 g một giây thì đóng phiên. Có cả khô và ướt thì ghi.
- Sổ `thudiem.db`, trần 500 MB. Nút Clear chỉ xóa nét biểu đồ.
- Điểm dưới 15 không vào bình quân và không giao AutoCan. Ô Lọc dưới 15 đang bật thì ẩn các phiếu đó. Admin chọn phiếu rồi bấm Xóa phiếu, nhập mã. App không tự POST mill và không mở `cancomp.db`.

Đừng sửa AutoCan khi chỉ đổi cách chấm.
