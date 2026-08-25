# Database schema

Nguyên tắc: **1 database Postgres riêng cho mỗi service** (database-per-service) — không có
foreign key vật lý xuyên database; liên kết giữa các service (vd. `customer_id` trong
`accountdb` trỏ tới `customerdb`) chỉ là liên kết logic, toàn vẹn được đảm bảo qua event, không
qua FK constraint.

Quy ước chung cho mọi bảng (trừ khi ghi chú khác):
- Khoá chính `id uuid`.
- `created_at timestamptz`, `updated_at timestamptz`.
- Bảng `outbox_messages` ở mỗi service **do Wolverine tự tạo/quản lý** lúc khởi động
  (`UseResourceSetupOnStartup()`), không đi qua EF Core migration — không liệt kê chi tiết cột
  ở đây vì đó là bảng nội bộ của Wolverine, không phải domain schema.

---

## identitydb

Dùng ASP.NET Core Identity mặc định (user store) + **OpenIddict.EntityFrameworkCore** (OAuth2/
OIDC Authorization Server store). Cả hai bộ bảng đều là entity EF Core bình thường, tạo qua
`dotnet ef migrations` như domain schema khác — **khác** với bảng outbox của Wolverine (tự
provision lúc khởi động, không qua migration).

### ASP.NET Core Identity (user store)

| Bảng | Cột chính | Ghi chú |
|---|---|---|
| `AspNetUsers` | id, user_name, email, password_hash, phone_number, `customer_id` (uuid, nullable) | Chuẩn ASP.NET Identity + cột mở rộng `customer_id` liên kết logic tới `customerdb.customers` |
| `AspNetRoles` | id, name | Chuẩn Identity (vd. `Customer`, `Teller`, `Admin`) — dùng cho phân quyền thô theo vai trò, bổ sung cho scope OAuth2 (xem [01-architecture-overview.md §4](01-architecture-overview.md)) |
| `AspNetUserRoles` | user_id, role_id | Bảng nối chuẩn Identity |

### OpenIddict (Authorization Server store)

| Bảng | Cột chính | Ghi chú |
|---|---|---|
| `OpenIddictApplications` | id, client_id, client_type (`public`/`confidential`), display_name, redirect_uris, permissions, requirements | Đăng ký client — vd `angular-spa` (public, PKCE bắt buộc), các client confidential dùng Client Credentials |
| `OpenIddictScopes` | id, name, resources | Định nghĩa scope (vd `accounts.write`) và `resources` (audience) nó gắn vào — xem bảng scope theo service ở [01-architecture-overview.md §4](01-architecture-overview.md) |
| `OpenIddictAuthorizations` | id, application_id (FK), subject, status, type, scopes | Bản ghi 1 lần cấp quyền của user cho 1 client (dùng để cấp lại refresh token, hỗ trợ thu hồi) |
| `OpenIddictTokens` | id, application_id (FK), authorization_id (FK), subject, type, status, payload, expiration_date | Access/refresh/authorization-code token đã phát hành (payload có thể mã hoá tuỳ cấu hình) |

> `OpenIddictApplications`/`Scopes` thường được **seed lúc startup** bằng code (idempotent
> create-if-missing qua `IOpenIddictApplicationManager`/`IOpenIddictScopeManager`), không phải
> seed qua migration — migration chỉ tạo cấu trúc bảng.

---

## customerdb

| Bảng | Cột | Kiểu dữ liệu | Ghi chú |
|---|---|---|---|
| `customers` | id | uuid PK | |
| | first_name, last_name | varchar(100) | |
| | date_of_birth | date | |
| | national_id | varchar(20), unique | Số CCCD/CMND |
| | email | varchar(255), unique | |
| | phone | varchar(20) | |
| | address | varchar(500) | |
| | kyc_status | varchar(20) | `Pending` \| `Verified` \| `Rejected` |
| | created_at, updated_at | timestamptz | |
| `kyc_documents` | id | uuid PK | |
| | customer_id | uuid, FK → customers.id | |
| | document_type | varchar(30) | `IdCard` \| `Passport` \| `ProofOfAddress` |
| | document_number | varchar(50) | |
| | file_reference | varchar(500) | Đường dẫn/URL file (mock, không lưu file thật) |
| | status | varchar(20) | `Pending` \| `Verified` \| `Rejected` |
| | verified_at | timestamptz, nullable | |
| | created_at | timestamptz | |

---

## accountdb

| Bảng | Cột | Kiểu dữ liệu | Ghi chú |
|---|---|---|---|
| `accounts` | id | uuid PK | |
| | customer_id | uuid | Liên kết logic tới `customerdb.customers` |
| | account_number | varchar(20), unique | |
| | account_type | varchar(20) | `Checking` \| `Savings` |
| | currency | varchar(3) | ISO 4217, vd `VND`, `USD` |
| | balance | decimal(18,2) | Số dư hiện tại — nguồn sự thật (source of truth) |
| | available_balance | decimal(18,2) | `balance` trừ đi các khoản đang hold |
| | status | varchar(20) | `Active` \| `Frozen` \| `Closed` |
| | opened_at | timestamptz | |
| | closed_at | timestamptz, nullable | |
| `account_holds` | id | uuid PK | |
| | account_id | uuid, FK → accounts.id | |
| | amount | decimal(18,2) | |
| | reason | varchar(200) | Vd: "Pending card authorization" |
| | created_at | timestamptz | |
| | released_at | timestamptz, nullable | |

---

## transactiondb

| Bảng | Cột | Kiểu dữ liệu | Ghi chú |
|---|---|---|---|
| `transfer_saga_state` | id | uuid PK | Saga instance id (Wolverine `Saga.Id`) |
| | from_account_id | uuid | |
| | to_account_id | uuid | |
| | amount | decimal(18,2) | |
| | status | varchar(20) | `Started` \| `SourceDebited` \| `Completed` \| `Compensating` \| `Failed` |
| | correlation_id | uuid | Dùng để trace xuyên các event liên quan |
| | created_at | timestamptz | |
| | completed_at | timestamptz, nullable | |
| `transaction_history` | id | uuid PK | Append-only, là **read model** build từ `AccountDebited`/`AccountCredited` |
| | account_id | uuid | |
| | direction | varchar(10) | `Debit` \| `Credit` |
| | amount | decimal(18,2) | |
| | balance_after | decimal(18,2) | Snapshot số dư sau giao dịch (phục vụ hiển thị sao kê) |
| | related_transfer_id | uuid, nullable | FK logic → transfer_saga_state.id |
| | occurred_at | timestamptz | |

---

## carddb

| Bảng | Cột | Kiểu dữ liệu | Ghi chú |
|---|---|---|---|
| `cards` | id | uuid PK | |
| | account_id | uuid | Liên kết logic tới `accountdb.accounts` |
| | customer_id | uuid | Liên kết logic tới `customerdb.customers` |
| | card_number_masked | varchar(20) | Vd `**** **** **** 1234` — không lưu số thẻ đầy đủ |
| | card_type | varchar(20) | `Debit` \| `Credit` |
| | expiry_date | date | |
| | status | varchar(20) | `Active` \| `Blocked` \| `Expired` |
| | daily_limit | decimal(18,2) | |
| | created_at | timestamptz | |
| `card_authorizations` | id | uuid PK | |
| | card_id | uuid, FK → cards.id | |
| | amount | decimal(18,2) | |
| | merchant | varchar(200) | Mock tên đơn vị chấp nhận thanh toán |
| | status | varchar(20) | `Authorized` \| `Declined` \| `Settled` |
| | created_at | timestamptz | |

---

## loandb

| Bảng | Cột | Kiểu dữ liệu | Ghi chú |
|---|---|---|---|
| `loan_applications` | id | uuid PK | |
| | customer_id | uuid | |
| | requested_amount | decimal(18,2) | |
| | term_months | int | |
| | interest_rate | decimal(5,2) | %/năm |
| | status | varchar(20) | `Pending` \| `Approved` \| `Rejected` |
| | submitted_at | timestamptz | |
| | decided_at | timestamptz, nullable | |
| `loans` | id | uuid PK | |
| | application_id | uuid, FK → loan_applications.id | |
| | principal | decimal(18,2) | |
| | interest_rate | decimal(5,2) | |
| | term_months | int | |
| | status | varchar(20) | `Active` \| `Closed` \| `Defaulted` |
| | disbursed_account_id | uuid | Tài khoản nhận giải ngân (liên kết logic) |
| | start_date, end_date | date | |
| `repayment_schedule` | id | uuid PK | |
| | loan_id | uuid, FK → loans.id | |
| | due_date | date | |
| | amount_due | decimal(18,2) | |
| | amount_paid | decimal(18,2), default 0 | |
| | status | varchar(20) | `Pending` \| `Paid` \| `Overdue` |
| `loan_payments` | id | uuid PK | |
| | loan_id | uuid, FK → loans.id | |
| | schedule_id | uuid, FK → repayment_schedule.id | |
| | amount | decimal(18,2) | |
| | paid_at | timestamptz | |
| | source_account_id | uuid | |

---

## paymentdb

| Bảng | Cột | Kiểu dữ liệu | Ghi chú |
|---|---|---|---|
| `payment_requests` | id | uuid PK | |
| | customer_id | uuid | |
| | source_account_id | uuid | |
| | payee_type | varchar(30) | `BillProvider` \| `ExternalBank` |
| | payee_reference | varchar(200) | Mã hoá đơn/số tài khoản đích (mock) |
| | amount | decimal(18,2) | |
| | currency | varchar(3) | |
| | status | varchar(20) | `Pending` \| `Processing` \| `Completed` \| `Failed` |
| | created_at | timestamptz | |
| `payment_saga_state` | id | uuid PK | Saga instance id |
| | payment_request_id | uuid, FK → payment_requests.id | |
| | status | varchar(20) | `Started` \| `FundsHeld` \| `ProviderCalled` \| `Completed` \| `Compensating` \| `Failed` |
| | correlation_id | uuid | |
| | created_at | timestamptz | |
| | completed_at | timestamptz, nullable | |

---

## notificationdb

| Bảng | Cột | Kiểu dữ liệu | Ghi chú |
|---|---|---|---|
| `notifications` | id | uuid PK | |
| | customer_id | uuid | |
| | channel | varchar(20) | `Email` \| `Sms` \| `Push` |
| | template | varchar(100) | Vd `AccountOpened`, `TransferCompleted` |
| | payload | jsonb | Dữ liệu render template (mock gửi, không tích hợp email/SMS thật) |
| | status | varchar(20) | `Sent` \| `Failed` |
| | sent_at | timestamptz | |
| `notification_preferences` | id | uuid PK | |
| | customer_id | uuid | |
| | channel | varchar(20) | |
| | enabled | boolean | |

> Service này thuần là consumer sự kiện — không cần bảng outbox trừ khi tự phát sinh event
> riêng (hiện tại chưa cần).

---

## auditdb

| Bảng | Cột | Kiểu dữ liệu | Ghi chú |
|---|---|---|---|
| `audit_logs` | id | uuid PK | |
| | event_type | varchar(100) | Vd `AccountDebited`, `LoanApproved` |
| | aggregate_type | varchar(50) | Vd `Account`, `Loan` |
| | aggregate_id | uuid | |
| | payload_json | jsonb | Toàn bộ nội dung event gốc |
| | occurred_at | timestamptz | |
| | correlation_id | uuid, nullable | Trace xuyên nhiều service/saga |
| | causation_id | uuid, nullable | Event/command nào đã sinh ra event này |
| | source_service | varchar(50) | Service đã publish event |

Bảng append-only, có index trên `(aggregate_id)` và `(occurred_at)` để phục vụ tra cứu
compliance/audit trail.
