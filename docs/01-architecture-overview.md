# Tổng quan kiến trúc

## Quyết định kiến trúc đã chốt

1. **Phạm vi nghiệp vụ**: Core banking (Customer/KYC, Account, Transaction/Transfer) + Card +
   Loan + External Payment + Notification + Audit — để ngỏ khả năng mở rộng thêm sau.
2. **Kiến trúc triển khai**: Microservices thật ngay từ đầu — mỗi bounded context là một
   service độc lập, có database riêng, giao tiếp chủ yếu qua message broker.
3. **Data/CQRS**: EF Core + PostgreSQL, CQRS "nhẹ" (không event sourcing) — mỗi service có
   write model (EF Core) và có thể có read model chiếu (projection) riêng cho query.

## 1. Danh sách microservices (bounded contexts)

| # | Service | Trách nhiệm chính | Database |
|---|---------|--------------------|----------|
| 1 | **Identity** | OAuth2/OIDC Authorization Server (OpenIddict): đăng ký/đăng nhập, cấp access/id/refresh token theo scope, quản lý client & scope | `identitydb` |
| 2 | **Customer** | Hồ sơ khách hàng, KYC (giấy tờ, trạng thái xác minh) | `customerdb` |
| 3 | **Account** | Mở/đóng/đóng băng tài khoản, số dư, invariant Debit/Credit | `accountdb` |
| 4 | **Transaction** (Ledger/Transfer) | Điều phối chuyển khoản (saga), sổ cái lịch sử giao dịch (read model) | `transactiondb` |
| 5 | **Card** | Phát hành thẻ, trạng thái thẻ, hạn mức, uỷ quyền giao dịch thẻ | `carddb` |
| 6 | **Loan** | Hồ sơ vay, phê duyệt, lịch trả nợ, giải ngân | `loandb` |
| 7 | **Payment** | Thanh toán hoá đơn/chuyển khoản liên ngân hàng (mock đối tác ngoài), saga | `paymentdb` |
| 8 | **Notification** | Lắng nghe event toàn hệ thống → gửi email/SMS (mock) | `notificationdb` |
| 9 | **Audit** | Ghi log mọi integration event (compliance, append-only) | `auditdb` |
| — | **Gateway** (BFF) | YARP reverse-proxy, entrypoint cho Angular — chỉ forward `Authorization` header, không tự validate token | không có DB |

**Nguyên tắc biên (bounded context)**: mỗi service sở hữu dữ liệu và invariant của riêng
mình; không service nào được truy cập trực tiếp DB của service khác. Mọi giao tiếp
cross-service đi qua HTTP (qua Gateway, hoặc nội bộ khi thật sự cần đồng bộ) hoặc qua message
broker (bất đồng bộ — là lựa chọn mặc định).

**Vì sao tách Account và Transaction thành 2 service riêng** (thay vì gộp làm một
"Account/Ledger" service duy nhất): Account sở hữu invariant nghiệp vụ tức thời (số dư, có đủ
tiền để debit hay không), còn Transaction là nơi điều phối các quy trình nhiều bước (chuyển
khoản nội bộ = debit A + credit B, có thể cần bù trừ nếu bước sau thất bại) và xây dựng read
model lịch sử giao dịch từ event — đây là ví dụ CQRS + Saga rõ ràng nhất trong cả hệ thống,
phù hợp mục tiêu học tập.

## 2. Sơ đồ tổng quan (logic)

```
                         ┌───────────────────┐
                         │   Angular (Web)    │
                         └─────────┬──────────┘
                                   │ HTTPS (1 base URL)
                         ┌─────────▼──────────┐
                         │   Gateway (YARP)    │  ← chỉ forward, không validate
                         └─────────┬──────────┘
        ┌───────────┬──────────────┼──────────────┬───────────┬───────────┐
        ▼           ▼              ▼              ▼           ▼           ▼
   ┌────────┐  ┌──────────┐  ┌──────────┐   ┌─────────┐  ┌───────┐  ┌─────────┐
   │Identity│  │ Customer │  │ Account  │   │Transaction│ │ Card  │  │  Loan   │ ...
   │ (IdP)  │  │(resource)│  │(resource)│   │(resource) │ │(res.) │  │ (res.)  │
   └────────┘  └──────────┘  └────┬─────┘   └────┬────┘  └───┬───┘  └────┬────┘
        (mỗi service: 1 Postgres DB riêng) │           │          │
                                   └───────────┴──────────┴──────────┴──► RabbitMQ
                                          (integration events, Wolverine)
                                                   │
                                     ┌─────────────┴─────────────┐
                                     ▼                           ▼
                              ┌─────────────┐            ┌─────────────┐
                              │ Notification│            │    Audit    │
                              └─────────────┘            └─────────────┘
```

- **Đường liền (Gateway → service)**: đồng bộ, HTTP, do Angular chủ động gọi. Gateway chỉ định
  tuyến (routing) và forward nguyên `Authorization: Bearer <token>` — **mỗi service tự validate
  token và tự enforce scope của riêng nó** (đúng vai trò OAuth2 Resource Server), Gateway không
  làm hộ việc này. Chi tiết ở mục 4 bên dưới.
- **Đường qua RabbitMQ**: bất đồng bộ, dùng cho mọi giao tiếp giữa các service nghiệp vụ và
  2 service "consumer thuần" (Notification, Audit). Đây là kênh giao tiếp mặc định giữa các
  bounded context — hạn chế tối đa gọi HTTP trực tiếp service-to-service.

## 3. Event catalog (giao tiếp bất đồng bộ qua RabbitMQ + Wolverine)

Quy ước đặt tên: `{BoundedContext}.{Event}` dạng quá khứ = **integration event** (đã xảy ra,
public contract, cần version hoá khi thay đổi). Domain event nội bộ (raised trong aggregate,
xử lý trong cùng transaction qua Wolverine local queue + outbox) không liệt kê hết ở đây.

| Publisher | Integration Event | Payload chính | Consumer(s) |
|---|---|---|---|
| Customer | `CustomerRegistered` | customerId, name, email | Identity, Notification, Audit |
| Customer | `CustomerKycVerified` / `CustomerKycRejected` | customerId, status | Account (điều kiện mở tài khoản), Notification, Audit |
| Account | `AccountOpened` | accountId, customerId, accountNumber, type, currency | Notification, Audit |
| Account | `AccountDebited` / `AccountCredited` | accountId, amount, balanceAfter, correlationId | Transaction (ledger), Audit |
| Account | `AccountDebitRejected` | accountId, reason, correlationId | Transaction/Payment (saga compensation) |
| Account | `AccountFrozen` / `AccountClosed` | accountId, reason | Card (khoá thẻ liên kết), Notification, Audit |
| Transaction | `TransferCompleted` / `TransferFailed` | transferId, fromAccountId, toAccountId, amount | Notification, Audit |
| Card | `CardIssued` / `CardBlocked` | cardId, accountId | Notification, Audit |
| Loan | `LoanApproved` / `LoanRejected` | loanId, customerId, amount | Notification, Audit |
| Loan | `LoanDisbursed` | loanId, accountId, amount | Account (nhận lệnh credit), Audit |
| Loan | `RepaymentReceived` | loanId, amount, scheduleId | Notification, Audit |
| Payment | `PaymentCompleted` / `PaymentFailed` | paymentId, accountId, amount, payee | Notification, Audit |

- **Notification** subscribe hầu hết các event trên (queue riêng, không quan tâm thứ tự xử lý).
- **Audit** subscribe **tất cả** integration event qua một binding wildcard trên RabbitMQ topic
  exchange (`#`) — append-only, dùng cho truy vết compliance.

### Saga tiêu biểu

- **`TransferSaga`** (trong Transaction service): `TransferRequested` → gửi lệnh debit tới
  Account → chờ `AccountDebited`/`AccountDebitRejected` → nếu debit OK, gửi lệnh credit tài
  khoản đích → chờ kết quả → `TransferCompleted`; nếu credit thất bại thì bù trừ bằng lệnh
  credit ngược lại vào tài khoản nguồn (compensation) → `TransferFailed`.
- **`PaymentSaga`** (trong Payment service): `PaymentRequested` → debit/hold tài khoản nguồn →
  gọi provider ngoài (mock) → thành công thì `PaymentCompleted`; thất bại thì hoàn tiền (credit
  ngược) → `PaymentFailed`.

> Lưu ý: Wolverine không có DSL khai báo compensating-transaction sẵn (khác MassTransit) — bù
> trừ phải tự viết bằng handler xử lý message thất bại/timeout, gọi lệnh bù trừ thủ công. Chi
> tiết ở [03-tech-stack.md](03-tech-stack.md).

## 4. OAuth2 / OpenID Connect (Identity = Authorization Server)

Identity service **tự xây dựng** thành một Authorization Server chuẩn OAuth2/OIDC bằng
**OpenIddict** (open-source, không phụ thuộc bên thứ ba) — mục tiêu là thực hành protocol thật
sự (client, scope, grant type, token) chứ không chỉ dùng JWT tự chế.

### Vai trò các service trong mô hình OAuth2
- **Identity** = **Authorization Server** (cấp token) kiêm luôn user store (ASP.NET Identity).
- **Angular** = **Public Client** (SPA, không giữ client secret).
- **Mỗi microservice nghiệp vụ** (Account, Customer, Transaction, Card, Loan, Payment) =
  **Resource Server** — tự validate token nhận được và tự quyết định có đủ scope để xử lý
  request hay không. Notification/Audit là consumer thuần qua event, không cần vai trò này.
- **Gateway** = thuần routing, không tham gia xác thực (xem sơ đồ mục 2).

### Scope theo từng service (audience)

| Service (resource/audience) | Scope |
|---|---|
| `customer-api` | `customers.read`, `customers.write` |
| `account-api` | `accounts.read`, `accounts.write` |
| `transaction-api` | `transactions.read`, `transactions.write` |
| `card-api` | `cards.read`, `cards.write` |
| `loan-api` | `loans.read`, `loans.write` |
| `payment-api` | `payments.read`, `payments.write` |
| chuẩn OIDC | `openid`, `profile`, `email`, `offline_access` (cấp refresh token) |

Mỗi scope gắn với `Resources` (audience) của đúng service đó — token chỉ mang `aud` của
những service mà scope được cấp, đây là cơ chế OAuth2 giới hạn phạm vi truy cập giữa các
bounded context, không phải quy ước tự đặt.

### Grant type dùng để practice

| Grant type | Dùng cho | Ghi chú |
|---|---|---|
| **Authorization Code + PKCE** | Angular SPA đăng nhập người dùng | Bắt buộc PKCE vì client public (không secret) |
| **Refresh Token** | Angular gia hạn phiên không cần đăng nhập lại | Cần scope `offline_access` |
| **Client Credentials** | Gọi API service-to-service khi cần đồng bộ (m2m, không gắn user) | Client confidential (có secret), dùng cho tình huống ngoài luồng saga bất đồng bộ chính |

### Vai trò kép Authentication vs Authorization
- **Authentication** (đăng nhập, xác định danh tính) chỉ xảy ra **một lần** ở Identity, qua
  Authorization Code flow.
- **Authorization** (đủ quyền hay không) được **từng resource server** tự thực hiện lại trên
  mỗi request, bằng cách kiểm tra scope trong access token — kết hợp thêm role (`AspNetRoles`)
  nếu cần phân quyền thô hơn theo vai trò (`Customer`, `Teller`, `Admin`).

Chi tiết cách implement (package, code mẫu OpenIddict, cách resource server validate token,
cách bảo vệ Wolverine.Http endpoint theo scope, cấu hình Angular OIDC client) nằm ở
[03-tech-stack.md](03-tech-stack.md#oauth2--openid-connect-openiddict).

## 5. Cấu trúc solution (đề xuất, ánh xạ vào `DigitalBanking.slnx`)

```
Directory.Packages.props   (pin tập trung version package cho toàn solution)
src/
  AppHost/                 (đã có — sẽ thêm resource Postgres/RabbitMQ/project/Angular)
  ServiceDefaults/         (đã có — không đổi, mọi service reference)
  Gateway/                 (YARP + JWT validation)
  Services/
    Identity/
      Identity.Api/  Identity.Domain/  Identity.Infrastructure/  Identity.Contracts/
    Customer/        (Domain / Api / Infrastructure / Contracts — cùng khuôn mẫu)
    Account/
    Transaction/
    Card/
    Loan/
    Payment/
    Notification/
    Audit/
  Web/                     (Angular app)
```

Mỗi service theo khuôn Clean Architecture nhẹ:
- **Domain** — aggregate, value object, domain event, invariant nghiệp vụ.
- **Infrastructure** — EF Core DbContext, migrations, outbox.
- **Api** — Wolverine.Http endpoints, `Program.cs`, reference `ServiceDefaults`.
- **Contracts** — DTO/integration event public, được các service khác reference để biết
  "hình dạng" event, **không** chứa domain model.

## 6. Lộ trình gợi ý

Tóm tắt 4 cụm chính, theo thứ tự phụ thuộc:

1. **Nền tảng**: hạ tầng Aspire + Identity (OAuth2/OIDC) + Customer + Account + Angular shell
   (login thật qua Authorization Code + PKCE) — chạy được end-to-end với auth thật.
2. **Core banking**: Transaction service + `TransferSaga` + Angular transfer UI.
3. **Mở rộng nghiệp vụ**: Card, Loan, Payment + thử thêm grant Client Credentials cho 1 kịch
   bản service-to-service.
4. **Cross-cutting**: Notification, Audit (thuần consumer, minh hoạ tính event-driven).

Chi tiết từng giai đoạn nhỏ hơn (việc cần làm cụ thể, tiêu chí hoàn thành) ở
[04-roadmap.md](04-roadmap.md). Xem schema ở [02-database-schema.md](02-database-schema.md) và
cách vận dụng từng công nghệ ở [03-tech-stack.md](03-tech-stack.md).
