# Vận dụng stack

Repo hiện tại target **net10.0**, dùng **Aspire.AppHost.Sdk 13.5.2** (xác nhận từ
`src/AppHost/AppHost.csproj`). Các thư viện liên quan nằm ở **4 dòng version độc lập, không đi
cùng nhau** — cần lưu ý khi thêm package:

| Nhóm package | Version đã xác minh | Ghi chú |
|---|---|---|
| net10 runtime libs (`Microsoft.Extensions.*`, `OpenTelemetry.*`) | `10.8.0` / `1.15.x` | Đã dùng trong `ServiceDefaults.csproj` |
| Aspire hosting (`Aspire.Hosting.PostgreSQL`, `Aspire.Hosting.RabbitMQ`, `Aspire.Hosting.JavaScript`) | `13.5.2` | Lockstep với `Aspire.AppHost.Sdk` đã có sẵn |
| WolverineFx (`WolverineFx`, `WolverineFx.Http`, `WolverineFx.RabbitMQ`, `WolverineFx.EntityFrameworkCore`, `WolverineFx.Postgresql`) | `6.30.0` | Lockstep version riêng |
| `Yarp.ReverseProxy` | `2.3.0` | Độc lập |
| OpenIddict (`OpenIddict.AspNetCore`, `OpenIddict.EntityFrameworkCore`) | `7.6.0` (bản ổn định; có nhánh `8.0.0-preview.*` — không dùng cho học tập lúc này) | Lockstep version riêng |
| `angular-auth-oidc-client` (npm) | Theo version Angular chính (vd `22.x` đi với Angular 22) | Version tracking theo Angular major, không phải version độc lập |

Khuyến nghị: thêm `Directory.Packages.props` ở root repo để pin tập trung, tránh lệch version
giữa các service khi thêm project mới.

---

## Wolverine

Dùng làm **command/query mediator nội bộ** (thay MediatR) **và** **message bus liên service**
(thay MassTransit) trong cùng một framework.

### In-process mediator (CQRS)
```csharp
builder.Host.UseWolverine(opts =>
{
    // handler discovery, middleware, transports...
});
```
Handler theo convention `Handle(TCommand cmd, ...)` — không cần interface, không cần đăng ký
thủ công, Wolverine tự quét assembly.

### Wolverine.Http — expose handler thành HTTP endpoint
```csharp
builder.Services.AddWolverineHttp();
var app = builder.Build();
app.MapWolverineEndpoints();
```
```csharp
public static class OpenAccountEndpoint
{
    [WolverinePost("/accounts")]
    public static (AccountResponse, AccountOpened) Post(OpenAccount command, AccountDbContext db)
        => (response, accountOpenedEvent); // item thứ 2 được Wolverine tự "cascade" publish
}
```
`[WolverineGet]`/`[WolverinePost]`/... thay thế Controller/Minimal API thủ công, giảm
boilerplate publish event sau khi xử lý command.

**Bảo vệ endpoint bằng scope OAuth2**: endpoint Wolverine.Http vẫn là route ASP.NET Core bình
thường phía dưới, nên dùng thẳng authorization chuẩn — không cần API riêng của Wolverine:
```csharp
[WolverinePost("/accounts/{id}/debit")]
[Authorize(AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme,
           Policy = "accounts.write")]
public static (Response, AccountDebited) Post(DebitAccount command, AccountDbContext db) { ... }
```
Có thể bật bắt buộc `[Authorize]` cho toàn bộ endpoint (trừ chỗ đánh dấu `[AllowAnonymous]`)
bằng `app.MapWolverineEndpoints(opts => opts.RequireAuthorizeOnAll());`. Chi tiết cấu hình
policy theo scope xem phần [OAuth2 / OpenID Connect](#oauth2--openid-connect-openiddict) bên
dưới.

### Transactional Outbox/Inbox (EF Core + PostgreSQL)
```csharp
builder.Host.UseWolverine(opts =>
{
    opts.PersistMessagesWithPostgresql(connectionString);
    opts.UseEntityFrameworkCoreTransactions();
    opts.Policies.AutoApplyTransactions();
});
builder.Services.AddDbContextWithWolverineIntegration<AccountDbContext>(o =>
    o.UseNpgsql(connectionString));
builder.Host.UseResourceSetupOnStartup();
```
Khi handler gọi `SaveChangesAsync`, thay đổi domain và integration event được ghi cùng một
transaction DB (outbox), rồi Wolverine publish ra RabbitMQ ở background — tránh dual-write
problem (lưu DB thành công nhưng publish event thất bại, hoặc ngược lại).

> **Lưu ý quan trọng**: bảng outbox/inbox là do chính Wolverine tự tạo/quản lý lúc khởi động
> (`UseResourceSetupOnStartup()`), **không** đi qua EF Core migration — chỉ schema domain
> (aggregate, read model) mới cần migration EF Core như bình thường.

### RabbitMQ transport
```csharp
opts.UseRabbitMqUsingNamedConnection("rabbitmq").AutoProvision();
opts.PublishMessage<AccountDebited>().ToRabbitExchange("account-events");
opts.ListenToRabbitQueue("transaction-service-inbox");
```
Tên connection `"rabbitmq"` khớp với tên resource khai báo trong Aspire AppHost — Wolverine
đọc thẳng connection string Aspire tiêm vào qua `.WithReference(rabbitmq)`, không cần cấu hình
thủ công.

### Saga
```csharp
public class TransferSaga : Saga
{
    public Guid Id { get; set; }
    public TransferStatus Status { get; set; }

    public static (TransferSaga, DebitAccount) Start(TransferRequested requested)
        => (new TransferSaga { Id = requested.TransferId, Status = TransferStatus.Started },
            new DebitAccount(requested.FromAccountId, requested.Amount, requested.TransferId));

    public CreditAccount Handle(AccountDebited debited)
    {
        Status = TransferStatus.SourceDebited;
        return new CreditAccount(ToAccountId, Amount, Id);
    }

    public void Handle(AccountCredited credited) => MarkCompleted();

    public CreditAccount Handle(AccountDebitRejected rejected) // hoặc CreditRejected sau bước credit
    {
        Status = TransferStatus.Failed;
        // Bù trừ thủ công — Wolverine không có DSL compensating-transaction sẵn (khác MassTransit)
        return new CreditAccount(FromAccountId, Amount, Id); // hoàn tiền lại tài khoản nguồn
    }
}
```
State persist qua chính `DbContext` đã đăng ký `AddDbContextWithWolverineIntegration` — không
cần package/registration riêng cho saga.

---

## EF Core + PostgreSQL (CQRS nhẹ)

- Mỗi service có **1 database Postgres riêng** — không dùng schema chung, không có FK vật lý
  xuyên service (xem chi tiết bảng ở [02-database-schema.md](02-database-schema.md)).
- **Write model**: EF Core DbContext ánh xạ aggregate — entity có hành vi (method), invariant
  nghiệp vụ enforce bên trong method (vd `Account.Debit(amount)` tự kiểm tra đủ số dư), tránh
  setter công khai bừa bãi.
- **Read model**: khi cần hình dạng dữ liệu khác write model, build read model riêng ngay
  trong cùng DB (vd `transaction_history` ở Transaction service, build từ
  `AccountDebited`/`AccountCredited`) — không cần DB thứ hai, không dùng event sourcing/Marten
  để giữ đơn giản cho mục tiêu học tập hiện tại.

---

## .NET Aspire

`AppHost.cs` (đã có sẵn, hiện đang rỗng) sẽ khai báo toàn bộ resource:

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres");
var identityDb = postgres.AddDatabase("identitydb");
var customerDb = postgres.AddDatabase("customerdb");
var accountDb = postgres.AddDatabase("accountdb");
// ... 1 database/service, cùng 1 postgres container cho môi trường local

var rabbitmq = builder.AddRabbitMQ("rabbitmq");

var accountApi = builder.AddProject<Projects.Account_Api>("account-api")
    .WithReference(accountDb).WithReference(rabbitmq);
// ... mỗi microservice khai báo tương tự

var gateway = builder.AddProject<Projects.Gateway>("gateway")
    .WithReference(accountApi) /* + các service khác */;

var web = builder.AddViteApp("web", "../Web", runScriptName: "dev")
    .WithReference(gateway)
    .WithExternalHttpEndpoints();

builder.Build().Run();
```

- `ServiceDefaults` (đã có sẵn) được reference bởi **mọi** service để có sẵn OpenTelemetry,
  health checks (`/health`, `/alive`), service discovery, resilience — không cần sửa gì thêm
  ở project này.
- Aspire lo phần local orchestration + service discovery — mỗi service gọi nhau qua tên
  resource (vd `http://account-api`), không cần hardcode URL/port.
- **Gateway/YARP không có package Aspire hosting riêng** — chỉ là `AddProject<Projects.Gateway>()`
  bình thường, trong đó `Gateway` là ASP.NET Core project cài `Yarp.ReverseProxy` 2.3.0:
  ```csharp
  builder.Services.AddReverseProxy().LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));
  app.MapReverseProxy();
  ```
  cluster destination trong `appsettings.json` trỏ vào tên service discovery (`http://account-api`).
- **Angular qua Aspire — cần spike xác nhận trước khi code**: package đang active/lockstep
  13.5.2 là `Aspire.Hosting.JavaScript` (`AddViteApp`, `AddNodeApp`...), **không phải**
  `Aspire.Hosting.NodeJs` cũ (đang dừng ở 9.5.2, có vẻ legacy/không còn theo kịp Aspire 13.x).
  Angular 17+ dùng Vite nội bộ nên hướng dùng là `AddViteApp` như snippet trên. Độ tin cậy
  nguồn thông tin này ở mức trung bình (từ aspire.dev + mô tả NuGet, chưa đối chiếu trực tiếp
  learn.microsoft.com) — **việc đầu tiên khi bắt đầu code phần Angular+Aspire nên là spike nhỏ
  xác nhận API này chạy đúng**, trước khi build cả Angular app xung quanh nó.

---

## OAuth2 / OpenID Connect (OpenIddict)

Identity service tự implement Authorization Server chuẩn OAuth2/OIDC bằng **OpenIddict 7.6.0**
— xem tổng quan vai trò từng service trong mô hình OAuth2 ở
[01-architecture-overview.md §4](01-architecture-overview.md).

### Đăng ký OpenIddict server (Identity service)
```csharp
builder.Services.AddDbContext<IdentityDbContext>(options =>
{
    options.UseNpgsql(connectionString);
    options.UseOpenIddict(); // đăng ký entity Applications/Authorizations/Scopes/Tokens vào model EF Core
});

builder.Services.AddOpenIddict()
    .AddCore(options =>
    {
        options.UseEntityFrameworkCore().UseDbContext<IdentityDbContext>();
    })
    .AddServer(options =>
    {
        options.SetAuthorizationEndpointUris("connect/authorize")
               .SetTokenEndpointUris("connect/token")
               .SetUserinfoEndpointUris("connect/userinfo");

        options.AllowAuthorizationCodeFlow().RequireProofKeyForCodeExchange();
        options.AllowClientCredentialsFlow();
        options.AllowRefreshTokenFlow();

        options.RegisterScopes("accounts.read", "accounts.write", /* ... */ "offline_access");

        options.AddDevelopmentEncryptionCertificate()
               .AddDevelopmentSigningCertificate(); // dev only — thay bằng cert thật khi production

        options.UseAspNetCore()
               .EnableAuthorizationEndpointPassthrough()
               .EnableTokenEndpointPassthrough()
               .EnableUserinfoEndpointPassthrough();
    });
```

> **Bảng OpenIddict tạo qua EF Core migration bình thường** (`dotnet ef migrations add
> AddOpenIddict`) — khác với bảng outbox Wolverine (tự provision lúc start, xem phần
> Transactional Outbox ở trên). Đừng nhầm 2 cơ chế "tự quản lý schema" này với nhau.

### Định nghĩa scope gắn audience
```csharp
await scopeManager.CreateAsync(new OpenIddictScopeDescriptor
{
    Name = "accounts.write",
    Resources = { "account-api" } // token được cấp scope này sẽ có "account-api" trong claim aud
});
```

### Seed client Angular SPA (public, PKCE bắt buộc) — chạy lúc startup, không phải migration
```csharp
if (await appManager.FindByClientIdAsync("angular-spa") is null)
{
    await appManager.CreateAsync(new OpenIddictApplicationDescriptor
    {
        ClientId = "angular-spa",
        ClientType = OpenIddictConstants.ClientTypes.Public,
        RedirectUris = { new Uri("https://localhost:4200/auth-callback") },
        PostLogoutRedirectUris = { new Uri("https://localhost:4200/") },
        Permissions =
        {
            OpenIddictConstants.Permissions.Endpoints.Authorization,
            OpenIddictConstants.Permissions.Endpoints.Token,
            OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode,
            OpenIddictConstants.Permissions.GrantTypes.RefreshToken,
            OpenIddictConstants.Permissions.ResponseTypes.Code,
            OpenIddictConstants.Permissions.Prefixes.Scope + "accounts.read",
            OpenIddictConstants.Permissions.Prefixes.Scope + "accounts.write",
            OpenIddictConstants.Permissions.Prefixes.Scope + "offline_access",
        },
        Requirements = { OpenIddictConstants.Requirements.Features.ProofKeyForCodeExchange }
    });
}
```

### Resource server (mỗi microservice nghiệp vụ) validate token — điểm quan trọng nhất

**Không dùng `AddJwtBearer` thông thường.** OpenIddict mặc định **mã hoá** access token (JWE,
không phải JWT ký thường) — middleware `JwtBearer` chuẩn không giải mã được. Cách đúng: cài
`OpenIddict.Validation.AspNetCore` ở **mỗi service resource** (Account, Customer, Transaction,
Card, Loan, Payment):

```csharp
builder.Services.AddOpenIddict()
    .AddValidation(options =>
    {
        options.SetIssuer("https://identity-api");
        options.AddAudiences("account-api");
        options.AddEncryptionKey(sharedEncryptionKey); // chia sẻ giữa Identity và service này
        options.UseSystemNetHttp();
        options.UseAspNetCore();
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("accounts.write", p => p.RequireClaim("scope", "accounts.write"));
```
Endpoint dùng `[Authorize(AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme, Policy = "accounts.write")]`
(xem ví dụ áp lên Wolverine.Http endpoint ở phần Wolverine phía trên).

Có 2 cách validate, đánh đổi latency vs khả năng thu hồi token real-time:
- **Offline (mặc định, dùng encryption key chia sẻ)** — nhanh, không cần gọi mạng, nhưng nếu
  token bị thu hồi trước hạn thì resource server không biết ngay.
- **`UseIntrospection()`** — mỗi request gọi tới `/connect/introspect` của Identity để hỏi
  token còn hợp lệ không — chậm hơn nhưng thu hồi tức thời. Có thể dùng làm bài tập nâng cao để
  so sánh 2 cách.

> Có escape hatch `DisableAccessTokenEncryption()` để access token là JWT ký thường (đọc được
> trên jwt.io, dùng `AddJwtBearer` bình thường) — OpenIddict khuyến nghị chỉ dùng khi cấp token
> cho resource server bên thứ ba không tự kiểm soát được. Vì mọi resource server ở đây đều là
> service của chính mình, nên **giữ mặc định (mã hoá + `OpenIddict.Validation.AspNetCore`)** để
> học đúng mô hình bảo mật gốc của OpenIddict.

---

## Angular (frontend)

Version hiện tại (đã xác minh): **Angular 22** (`@angular/core@22.1.3`).

- **Standalone components + signals** là mặc định từ Angular 19 — không cần NgModule.
- **Zoneless change detection** là mặc định từ Angular 21 — nên thiết kế component theo
  signal-first (`signal()`, `computed()`, `effect()`) ngay từ đầu, tránh viết code dựa vào
  Zone.js sẽ phải sửa lại sau.
- Lazy-loaded feature routes ánh xạ theo bounded context:
  `auth/`, `accounts/`, `transfers/`, `cards/`, `loans/`, `payments/`, `notifications/`.
- Gọi API qua **1 base URL duy nhất** (Gateway/BFF) — Angular không bao giờ gọi thẳng từng
  microservice.
- **Auth**: đăng nhập qua OIDC **Authorization Code + PKCE** với Identity (OpenIddict), dùng thư
  viện `angular-auth-oidc-client` (version đi theo Angular major, vd `22.x`), API dạng
  standalone/functional (`provideAuth()`) khớp với convention standalone component:
  ```ts
  // app.config.ts
  provideAuth({
    config: {
      authority: 'https://identity-api',
      redirectUrl: window.location.origin + '/auth-callback',
      postLogoutRedirectUri: window.location.origin,
      clientId: 'angular-spa',
      scope: 'openid profile accounts.read accounts.write offline_access',
      responseType: 'code',   // Authorization Code — PKCE tự bật kèm theo, không cần cấu hình thêm
      useRefreshToken: true,
    },
  })
  ```
  `HttpInterceptorFn` dùng token do thư viện quản lý để tự đính vào mọi request tới Gateway.
  **Caveat chưa xác minh đầy đủ**: chưa có tài liệu chính thức xác nhận thư viện này tương
  thích hoàn toàn với zoneless change detection (mặc định từ Angular 21) — cơ chế silent-renew
  qua iframe/`postMessage` trước đây dựa vào Zone.js. Nên spike nhỏ (đăng nhập + refresh token)
  trước khi build toàn bộ luồng auth xung quanh thư viện này.
- **State**: bắt đầu bằng service + `signal()`/`computed()` đơn giản, không cần thư viện state
  management ngay; để ngỏ khả năng thêm NgRx/SignalStore nếu app phình to sau này.
