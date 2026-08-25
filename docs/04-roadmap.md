# Roadmap triển khai

Chia theo giai đoạn tăng dần độ phức tạp — mỗi giai đoạn build được, chạy được, verify được
trước khi qua giai đoạn tiếp theo. Thứ tự được sắp để giai đoạn sau luôn dựa trên hạ tầng/service
đã có ở giai đoạn trước (không cần quay lại sửa nền tảng).

Tham chiếu: [01-architecture-overview.md](01-architecture-overview.md) (bounded context, event
catalog, OAuth2), [02-database-schema.md](02-database-schema.md) (schema), 
[03-tech-stack.md](03-tech-stack.md) (chi tiết package/code mẫu).

---

## Giai đoạn 0 — Hạ tầng nền (AppHost, packages, spike rủi ro)

**Mục tiêu học**: làm quen .NET Aspire orchestration, chốt version package trước khi có nhiều
service dùng chung.

**Việc cần làm**
- Tạo `Directory.Packages.props` ở root, pin 4 dòng version đã xác minh (net10 libs 10.8.0,
  Aspire hosting 13.5.2, WolverineFx 6.30.0, Yarp.ReverseProxy 2.3.0, OpenIddict 7.6.0).
- Sửa `src/AppHost/AppHost.cs`: thêm `AddPostgres("postgres")`, `AddRabbitMQ("rabbitmq")`.
- **Spike Angular qua Aspire** (rủi ro đã ghi chú ở [03-tech-stack.md](03-tech-stack.md)): tạo
  1 Angular app rỗng (`ng new web --standalone`), thêm `Aspire.Hosting.JavaScript`, wire bằng
  `AddViteApp("web", "../Web", runScriptName: "dev")` vào AppHost. Nếu API này không đúng như
  tài liệu đã research, đây là lúc phát hiện sớm, chưa tốn công viết business logic.
- Cấu trúc solution: tạo các solution folder trong `DigitalBanking.slnx` khớp
  `src/Services/*` như mô tả ở [01-architecture-overview.md §5](01-architecture-overview.md).

**Hoàn thành khi**: `dotnet run` trên AppHost → Aspire dashboard hiện Postgres, RabbitMQ, và
Angular app (qua Vite) đều ở trạng thái chạy được; `dotnet build DigitalBanking.slnx` sạch.

---

## Giai đoạn 1 — Identity service (OAuth2/OIDC Authorization Server)

**Mục tiêu học**: OpenIddict, OAuth2 Authorization Code + PKCE, EF Core migration cho user
store + OpenIddict store.

**Việc cần làm**
- Scaffold `Identity.Domain/Infrastructure/Api/Contracts`.
- `IdentityDbContext`: ASP.NET Core Identity (`AspNetUsers` + cột `customer_id`) +
  `UseOpenIddict()`.
- Migration đầu tiên (`dotnet ef migrations add InitialIdentity`) — tạo cả bảng Identity lẫn
  bảng OpenIddict.
- Đăng ký `AddOpenIddict().AddCore().AddServer()` theo mẫu ở
  [03-tech-stack.md](03-tech-stack.md#oauth2--openid-connect-openiddict): Authorization Code +
  PKCE, Refresh Token, scope `openid/profile/email/offline_access` + scope nghiệp vụ
  (`customers.*`, `accounts.*`...).
- Seed (startup, không phải migration): 1 client `angular-spa` (public, PKCE bắt buộc), 1-2 user
  test qua ASP.NET Identity (`UserManager`).
- Đăng ký `identity-api` vào AppHost với `.WithExternalHttpEndpoints()` — **quan trọng**: trình
  duyệt (Angular) cần redirect thẳng tới `/connect/authorize` của Identity, không qua service
  discovery nội bộ (service discovery chỉ dùng cho gọi service-to-service).

**Hoàn thành khi**: dùng Postman/`curl` hoặc một OIDC debugger đi hết luồng Authorization Code
+ PKCE thủ công (goto `/connect/authorize` → login → nhận `code` → đổi `code` lấy access +
refresh token ở `/connect/token`), decode token thấy đúng scope đã cấp.

---

## Giai đoạn 2 — Gateway rỗng + Angular shell (login thật)

**Mục tiêu học**: YARP reverse-proxy, Angular standalone + signal, tích hợp OIDC client trong
SPA thật (không phải Postman).

**Việc cần làm**
- Scaffold `Gateway`: cài `Yarp.ReverseProxy`, cấu hình `ReverseProxy` section trong
  `appsettings.json` (cluster trỏ service discovery, vd `http://identity-api`,
  `http://customer-api`...). Route API nghiệp vụ qua Gateway; riêng endpoint OIDC
  (`/connect/authorize`, `/connect/token`) Angular gọi **thẳng** tới Identity's external
  endpoint, không qua Gateway.
- Angular: cài `angular-auth-oidc-client`, cấu hình `provideAuth()` theo mẫu ở
  [03-tech-stack.md](03-tech-stack.md), tạo route `/auth-callback`, nút Login/Logout, trang
  hiển thị claims sau khi đăng nhập.
- `HttpInterceptorFn` đính access token vào mọi request gọi Gateway.

**Hoàn thành khi**: bấm Login trên Angular → redirect sang Identity → đăng nhập bằng user test
→ quay lại Angular thấy đã authenticated, hiển thị được claims từ token. Đây cũng là lúc xác
nhận luôn caveat "zoneless + angular-auth-oidc-client" có vấn đề gì không.

---

## Giai đoạn 3 — Customer service

**Mục tiêu học**: CQRS nhẹ với Wolverine.Http + EF Core, publish integration event qua outbox,
bảo vệ endpoint bằng OAuth2 scope.

**Việc cần làm**
- Scaffold `Customer.Domain` (aggregate `Customer`, value object `KycStatus`) /
  `Infrastructure` (EF Core, migration `customerdb`) / `Api` / `Contracts`
  (`CustomerRegistered`, `CustomerKycVerified`, `CustomerKycRejected`).
- Endpoint: `RegisterCustomer`, `GetCustomer`, `SubmitKycDocument`, `VerifyKyc` (giả lập admin
  duyệt tay, chưa cần UI admin riêng).
- Cấu hình outbox (`PersistMessagesWithPostgresql`, `AddDbContextWithWolverineIntegration`) +
  publish event lên RabbitMQ.
- Bảo vệ endpoint bằng `[Authorize(Policy = "customers.write")]` / `"customers.read"`.
- Gateway: thêm route `/api/customers/**`.
- Angular: form đăng ký hồ sơ khách hàng, trang xem thông tin cá nhân.

**Hoàn thành khi**: đăng ký customer qua Angular → thấy row trong `customerdb.customers` →
thấy message `CustomerRegistered` xuất hiện trên RabbitMQ management UI (hoặc log Wolverine).

---

## Giai đoạn 4 — Account service

**Mục tiêu học**: invariant nghiệp vụ trong aggregate (không cho debit quá số dư), consume
event từ service khác để ra quyết định nghiệp vụ.

**Việc cần làm**
- Aggregate `Account` với method `Open`, `Close`, `Freeze`, `Debit(amount)`, `Credit(amount)` —
  invariant nằm trong method, không có setter public.
- Endpoint: `OpenAccount`, `GetAccount`, `ListAccountsByCustomer`, `CloseAccount`.
- Subscribe `CustomerKycVerified` — business rule: chỉ cho mở account khi KYC đã verified
  (ví dụ điều kiện kiểm tra ngay trong handler `OpenAccount`, hoặc lưu trạng thái KYC cache cục
  bộ nhận qua event — chọn 1 trong 2 cách và ghi lại lý do chọn).
- Publish `AccountOpened`, `AccountDebited`, `AccountCredited`, `AccountDebitRejected`,
  `AccountFrozen`, `AccountClosed`.
- Gateway: route `/api/accounts/**`. Angular: danh sách account, mở account mới, xem số dư.

**Hoàn thành khi**: luồng end-to-end đầu tiên hoàn chỉnh — đăng ký customer → verify KYC (thủ
công) → mở account → thấy account + số dư trên Angular. Đây là mốc khớp "Giai đoạn Nền tảng"
trong bản roadmap tóm tắt ở [01-architecture-overview.md §6](01-architecture-overview.md).

---

## Giai đoạn 5 — Transaction service + TransferSaga

**Mục tiêu học**: trọng tâm CQRS + Saga của cả hệ thống — điều phối nhiều bước, bù trừ khi thất
bại, build read model từ event.

**Việc cần làm**
- `TransferSaga` (Wolverine `Saga`) theo mẫu ở
  [03-tech-stack.md](03-tech-stack.md): `TransferRequested` → command debit Account → chờ
  `AccountDebited`/`AccountDebitRejected` → command credit Account đích → chờ kết quả →
  `TransferCompleted`, hoặc bù trừ (credit ngược lại nguồn) → `TransferFailed` nếu bước sau
  thất bại.
- Read model `transaction_history`: subscribe `AccountDebited`/`AccountCredited`, ghi append-only.
- Endpoint query lịch sử giao dịch (đọc thẳng từ `transaction_history`, không đọc qua Account).
- Gateway: route `/api/transactions/**`. Angular: form chuyển khoản, trang lịch sử giao dịch.

**Hoàn thành khi**: chuyển khoản thành công giữa 2 account cùng khách hàng, thấy log saga
chuyển trạng thái từng bước; thử case số dư không đủ để thấy `TransferFailed` + không có tiền
"biến mất" (verify tổng số dư 2 account trước/sau bằng nhau khi thất bại).

---

## Giai đoạn 6 — Card service

**Mục tiêu học**: consume event để tự động phản ứng (thẻ tự khoá khi account bị đóng băng),
lặp lại khuôn mẫu CQRS đã quen từ giai đoạn 3-4 nhưng nhanh hơn.

**Việc cần làm**
- Aggregate `Card` (issue, activate, block), `CardAuthorization` (mock uỷ quyền giao dịch).
- Subscribe `AccountFrozen`/`AccountClosed` → tự động `BlockCard` cho thẻ liên kết.
- Gateway route `/api/cards/**`. Angular: trang quản lý thẻ (danh sách, khoá/mở thẻ).

**Hoàn thành khi**: đóng băng 1 account có thẻ liên kết → thẻ tự chuyển trạng thái `Blocked`
mà không cần gọi tay từ Angular (chứng minh event-driven reaction hoạt động).

---

## Giai đoạn 7 — Loan service

**Mục tiêu học**: quy trình phê duyệt nhiều trạng thái (workflow dài hơn saga 2 bước), gọi
command sang Account để giải ngân/thu nợ.

**Việc cần làm**
- Aggregate `LoanApplication`, `Loan`, `RepaymentSchedule`.
- Luồng: `SubmitApplication` → (duyệt thủ công/giả lập rule) → `Approve`/`Reject` →
  `LoanDisbursed` (gửi command credit vào `disbursed_account_id`) → sinh `repayment_schedule`.
- Xử lý `RepaymentReceived`: gọi debit tài khoản nguồn, cập nhật `repayment_schedule`.
- Gateway route `/api/loans/**`. Angular: form đăng ký vay, xem lịch trả nợ, trả nợ.

**Hoàn thành khi**: đăng ký vay → duyệt → thấy tiền được cộng vào account (qua
`AccountCredited`) → trả 1 kỳ nợ → `repayment_schedule` cập nhật đúng trạng thái.

---

## Giai đoạn 8 — Payment service + PaymentSaga + thử Client Credentials

**Mục tiêu học**: saga thứ hai (củng cố pattern đã học ở giai đoạn 5) + thực hành thêm grant
type OAuth2 còn lại (Client Credentials, machine-to-machine).

**Việc cần làm**
- `PaymentSaga` tương tự `TransferSaga`: `PaymentRequested` → hold/debit tài khoản nguồn → gọi
  "external provider" (mock, chỉ trả về thành công/thất bại ngẫu nhiên hoặc theo rule test) →
  `PaymentCompleted`/`PaymentFailed` (kèm hoàn tiền nếu thất bại).
- Đăng ký thêm 1 client OpenIddict loại **confidential**, cấp quyền `ClientCredentials` — dùng
  cho 1 kịch bản m2m cụ thể (vd. một job nội bộ gọi thẳng API đọc số dư `account-api` để đối
  soát, không gắn với user nào).
- Gateway route `/api/payments/**`. Angular: trang thanh toán hoá đơn.

**Hoàn thành khi**: thanh toán hoá đơn thành công/thất bại đúng kịch bản; gọi được 1 API bằng
token lấy từ Client Credentials flow (không qua đăng nhập user) và bị từ chối nếu thiếu scope.

---

## Giai đoạn 9 — Notification & Audit (event-driven consumer thuần)

**Mục tiêu học**: subscriber thuần không có write model nghiệp vụ, dùng RabbitMQ topic exchange
wildcard để bắt toàn bộ event hệ thống.

**Việc cần làm**
- Notification: subscribe phần lớn event trong bảng ở
  [01-architecture-overview.md §3](01-architecture-overview.md), ghi bảng `notifications`
  (mock gửi — chỉ log ra console/lưu DB, không tích hợp email/SMS thật).
- Audit: bind wildcard (`#`) trên exchange RabbitMQ, ghi mọi event vào `audit_logs`.
- Angular (tuỳ chọn, không bắt buộc): trang thông báo cho user, trang audit log cho admin.

**Hoàn thành khi**: thực hiện bất kỳ hành động nghiệp vụ nào (mở account, chuyển khoản...) →
thấy bản ghi tương ứng xuất hiện ở cả `notifications` và `audit_logs` mà không cần sửa gì ở
service phát sinh event gốc (chứng minh tính tách rời của kiến trúc event-driven).

---

## Giai đoạn 10 — Hoàn thiện & nâng cao (tuỳ chọn)

Không bắt buộc để hệ thống "chạy được", nhưng đáng làm nếu muốn đi sâu hơn:

- So sánh `UseIntrospection()` vs validate offline (encryption key chia sẻ) cho OpenIddict —
  đo thử độ trễ khác nhau.
- Viết unit test cho domain logic (aggregate/invariant) và integration test cho Wolverine
  handler.
- Theo dõi OpenTelemetry trace trên Aspire dashboard xuyên suốt 1 request đi qua nhiều service
  (Gateway → Account → RabbitMQ → Transaction).
- Thử tắt đột ngột 1 service khi đang có message pending — quan sát outbox/retry của Wolverine
  hoạt động ra sao khi service sống lại.
- Đóng gói production-ready (Docker, secrets thật thay development certificate của OpenIddict).
