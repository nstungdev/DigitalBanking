# DigitalBanking — Tài liệu thiết kế

Dự án học tập thực hành **DDD + CQRS** kết hợp **Wolverine** (JasperFx), **.NET Aspire**,
**EF Core/PostgreSQL** và **Angular**, với chủ đề nghiệp vụ ngân hàng số.

## Mục lục

1. [01-architecture-overview.md](01-architecture-overview.md) — Tổng quan hệ thống: danh sách
   microservices, bounded context, event catalog, saga, cấu trúc solution, lộ trình.
2. [02-database-schema.md](02-database-schema.md) — Schema chi tiết từng database (bảng, cột,
   kiểu dữ liệu, khoá).
3. [03-tech-stack.md](03-tech-stack.md) — Cách vận dụng Wolverine, .NET Aspire, EF Core,
   OpenIddict (OAuth2/OIDC), Angular; tên package và version cụ thể đã xác minh cho repo này.
4. [04-roadmap.md](04-roadmap.md) — Roadmap triển khai theo từng giai đoạn: mục tiêu học, việc
   cần làm, tiêu chí hoàn thành cho mỗi giai đoạn.

## Trạng thái

Đây là tài liệu **thiết kế** — repo hiện chưa có code service/domain/DB/frontend nào (chỉ có
`src/AppHost` và `src/ServiceDefaults` từ template Aspire). Tài liệu dùng làm nền cho các bước
scaffold code sau này.
