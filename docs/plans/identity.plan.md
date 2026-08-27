# Plan: Identity

> Trạng thái: Draft. Cập nhật lần cuối: 2026-08-27.

## Bối cảnh
- Vai trò trong kiến trúc: OAuth2/OIDC Authorization Server (OpenIddict) — xem [01-architecture-overview.md §1, §4](../01-architecture-overview.md).
- Giai đoạn roadmap: [Giai đoạn 1](../04-roadmap.md#giai-đoạn-1--identity-service-oauth2oidc-authorization-server) trong 04-roadmap.md.
- Trạng thái hiện tại: `src/Services/Identity/Identity.csproj` đã tồn tại (scaffold `dotnet new webapi` trơn, net10.0, chưa có EF Core/Wolverine/OpenIddict/ServiceDefaults). Đã đăng ký vào `DigitalBanking.slnx` (folder `/services/`) và vào `AppHost.cs` (hiện reference nguyên `postgres` server, chưa tách `identitydb`; `WithHttpEndpoint(port: 5000)`; Nx app `portal` đã `WaitFor(identityApi)`). Còn `Controllers/WeatherForecastController.cs` + `WeatherForecast.cs` là rác template cần xoá. **`src/Gateway` chưa tồn tại.**
- **3 quyết định khác so với docs mặc định (chốt trong lượt này)**:
  1. Toàn bộ Domain/Infrastructure/Api gộp vào **1 project** `Identity.csproj` (folder nội bộ, không tách 4 csproj như mô tả gốc ở 01 §5).
  2. Trang đăng nhập tương tác của OpenIddict được làm ở **FE (Angular)** thay vì Razor Pages/MVC server-rendered.
  3. `/account/login` và `/account/register` gọi **qua Gateway** (không gọi thẳng Identity) — vì Gateway chưa tồn tại, plan này bao gồm luôn 1 phần **tối thiểu** để scaffold Gateway (chỉ đủ route cho 2 endpoint này). Gateway đầy đủ (route cho toàn bộ service khác) vẫn thuộc [Giai đoạn 2](../04-roadmap.md) và nên có `docs/plans/gateway.plan.md` riêng sau.
  4. **DDD dạng mỏng cho `RegisterUser`, không áp dụng cho `Login`**: Identity là "generic subdomain" (khái niệm DDD của Eric Evans) — phần phức tạp thật sự (hash password, cấp token OAuth2) đã do ASP.NET Identity/OpenIddict xử lý, nên không mô hình hoá toàn bộ. Nhưng để vẫn có chỗ thực hành DDD (mục tiêu ban đầu của cả dự án), `RegisterUser` có 1 lớp **Domain** nhỏ (`User` aggregate, value object `Email`, domain event `UserRegistered`) tách biệt khỏi `ApplicationUser : IdentityUser` (kiểu của framework) — đúng hướng Domain không phụ thuộc Infrastructure. `Login` thì không có domain layer riêng vì không có invariant nghiệp vụ nào để bảo vệ ngoài "khớp mật khẩu hay không" (thuần kiểm tra, không phải quyết định nghiệp vụ). DDD "đậm" nhất trong hệ thống vẫn dồn cho Account/Transaction/Loan sau này.

## Phạm vi lần này

**Trong phạm vi:**
- `IdentityDbContext : IdentityDbContext<ApplicationUser>` (+ `CustomerId` nullable) và `UseOpenIddict()`; migration `InitialIdentity`.
- OpenIddict Server: Authorization Code + PKCE, Refresh Token (chưa bật Client Credentials — để Giai đoạn 8 theo roadmap).
- Seed lúc startup: client `angular-spa` (public, PKCE bắt buộc) + toàn bộ scope theo bảng ở 01 §4.
- Endpoint `RegisterUser` (Wolverine.Http) — tự đăng ký, thay cho việc seed tay user test. Orchestrate qua **Domain layer mỏng**: `Domain/Email.cs` (Value Object, validate format), `Domain/User.cs` (factory `User.Register(Email)` → trả về `User` + domain event), `Domain/UserRegistered.cs` (domain event) — handler gọi Domain trước để validate/tạo event, rồi mới gọi `UserManager.CreateAsync` (Infrastructure) để hash password + lưu.
- Endpoint `Login` (Wolverine.Http, JSON) — xác thực + set auth cookie cho luồng `/connect/authorize`. **Không có domain layer riêng** — thuần kiểm tra credential qua `SignInManager`, không có invariant nghiệp vụ nào để mô hình hoá.
- Sửa `AppHost.cs`: tách `identitydb = postgres.AddDatabase("identitydb")`, Identity reference `identitydb` thay vì cả server; thêm `.WithExternalHttpEndpoints()` cho `identity-api` (bắt buộc để trình duyệt redirect thẳng tới `/connect/*`).
- `Identity.csproj` reference `ServiceDefaults`.
- **Gateway (tối thiểu)**: scaffold `src/Gateway` (YARP), 1 route `/account/**` → `identity-api`; đăng ký vào `.slnx` + `AppHost.cs`; CORS cho origin Angular đặt ở đây (không phải ở Identity).
- FE: route `/login` (form) trong app `portal`, gọi `/account/login`, `/account/register` **qua Gateway**; cấu hình `provideAuth()` (`angular-auth-oidc-client`) + route `/auth-callback` — 2 route OIDC protocol này (`/connect/authorize`, `/connect/token`) vẫn gọi **thẳng Identity**, không qua Gateway (theo quy ước đã ghi ở Giai đoạn 2 của roadmap).
- Xoá `WeatherForecastController.cs`, `WeatherForecast.cs`.

**Ngoài phạm vi / để sau:**
- Gateway đầy đủ (route cho Customer/Account/... service khác) — để `docs/plans/gateway.plan.md` + Giai đoạn 2.
- Test (unit/integration) — để Giai đoạn 10.
- Client Credentials grant, `UseIntrospection()` — Giai đoạn 8/10.
- Cert production thật (đang dùng `AddDevelopmentEncryptionCertificate()`/`...Signing...()`).
- Liên kết `CustomerId` thật (chờ Customer service — Giai đoạn 3), tạm để nullable.
- Trang consent (xin đồng ý scope) — tạm auto-approve.
- Publish integration event (`UserRegistered`) qua RabbitMQ — Identity chưa wiring outbox/RabbitMQ ở lần này.

## Interface / Contract

**Backend (Wolverine.Http, trong `Identity.csproj`):**

| Method | Path | Request | Response | Ghi chú |
|---|---|---|---|---|
| POST | `/account/register` | email, password | 201 + userId | Public, **gọi qua Gateway**; handler: `Domain.User.Register(Email)` (validate + raise `UserRegistered`) → `UserManager.CreateAsync` (hash + lưu) → cascade publish `UserRegistered` qua Wolverine (local message, chưa ra RabbitMQ) |
| POST | `/account/login` | email, password, returnUrl | 200 (kèm returnUrl để redirect tiếp) / 401 | Public, `SignInManager.CheckPasswordSignInAsync` + `HttpContext.SignInAsync` (cookie), **gọi qua Gateway** |
| GET/POST | `/connect/authorize` | chuẩn OIDC | redirect | OpenIddict xử lý; chưa authenticated → redirect 302 sang `{AngularOrigin}/login?returnUrl=...`; **gọi thẳng Identity** (top-level navigation, không qua Gateway) |
| POST | `/connect/token` | chuẩn OIDC | access/id/refresh token | OpenIddict passthrough; `angular-auth-oidc-client` gọi **thẳng Identity** |
| GET | `/connect/userinfo` | Bearer token | claims | OpenIddict passthrough; **gọi thẳng Identity** |

Client/scope: `angular-spa` (public, PKCE), redirect URI `http://localhost:4200/auth-callback` (khớp cổng Angular thật trong AppHost hiện tại). Scope: `openid/profile/email/offline_access` + toàn bộ scope nghiệp vụ đã liệt kê ở 01 §4 (định nghĩa trước dù service tương ứng chưa tồn tại).

DB `identitydb`: theo [02-database-schema.md](../02-database-schema.md) (AspNetUsers + CustomerId, AspNetRoles, OpenIddictApplications/Scopes/Authorizations/Tokens).

**Frontend (app `portal`):**

| Route | Component | Gọi API |
|---|---|---|
| `/login` | `LoginPage` (form email/password) | `POST {gateway}/account/login`, `POST {gateway}/account/register` — **qua Gateway** |
| `/auth-callback` | xử lý bởi `angular-auth-oidc-client` | `POST {identity}/connect/token` (thư viện tự gọi, **thẳng Identity**) |

## Các bước implement

**Hạ tầng**
- [ ] `AppHost.cs`: `identitydb = postgres.AddDatabase("identitydb")`, đổi `WithReference(postgres)` → `WithReference(identitydb)`, thêm `.WithExternalHttpEndpoints()` cho `identity-api`.
- [ ] `Identity.csproj`: thêm `ProjectReference` → `ServiceDefaults`; thêm package `Npgsql.EntityFrameworkCore.PostgreSQL`, `Microsoft.EntityFrameworkCore.Design`, `Microsoft.AspNetCore.Identity.EntityFrameworkCore`, `OpenIddict.AspNetCore`, `OpenIddict.EntityFrameworkCore` (7.6.0), `WolverineFx`, `WolverineFx.Http` (6.30.0).
- [ ] Xoá `Controllers/WeatherForecastController.cs`, `WeatherForecast.cs`.
- [ ] `Program.cs`: `builder.AddServiceDefaults()`, `app.MapDefaultEndpoints()`.

**Gateway (tối thiểu — chỉ đủ cho Identity)**
- [ ] `dotnet new webapi` (hoặc rỗng) tại `src/Gateway`, cài `Yarp.ReverseProxy` (2.3.0), reference `ServiceDefaults`.
- [ ] `appsettings.json` — `ReverseProxy` section: route `/account/**` → cluster trỏ `http://identity-api`.
- [ ] `Program.cs`: `AddReverseProxy().LoadFromConfig(...)`, `MapReverseProxy()`; `AddCors` cho phép origin Angular (`http://localhost:4200`) + `AllowCredentials()` — **CORS đặt ở Gateway, không phải Identity**, vì trình duyệt gọi thẳng Gateway cho 2 endpoint này.
- [ ] Đăng ký `src/Gateway/Gateway.csproj` vào `.slnx` (folder `/gateway/` hoặc tương tự) và vào `AppHost.cs` (`AddProject<Projects.Gateway>("gateway").WithReference(identityApi)`), có `WithExternalHttpEndpoints()` (Angular gọi thẳng Gateway).
- [ ] Verify YARP forward `Set-Cookie` từ response của Identity về nguyên vẹn cho browser (mặc định YARP có forward, nhưng cần kiểm tra thực tế — xem rủi ro bên dưới).

**Data**

> 2 điểm khác với sketch gốc ở [03-tech-stack.md](../03-tech-stack.md) (chủ đích, không phải lỗi):
> (1) dùng `IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>` thay vì
> `IdentityDbContext<ApplicationUser>` (key `string` mặc định) — để khớp quy ước `id uuid` cho
> mọi bảng trong [02-database-schema.md](../02-database-schema.md); (2) migration chạy inline
> trong `Program.cs` (`Database.MigrateAsync()`) thay vì qua 1 migration service riêng theo
> khuyến nghị hiện tại của Aspire — vì đã chốt trước đó không muốn thêm service chuyên migrate.

Bố cục:
```
src/Services/Identity/Data/
  ApplicationUser.cs          (sửa lại — hiện là stub sai)
  IdentityDbContext.cs        (sửa lại — hiện là stub rỗng)
  Seed/
    OpenIddictSeeder.cs        (file mới)
```
Không cần `ApplicationRole.cs` riêng — dùng thẳng `IdentityRole<Guid>` của framework.

- [ ] `Data/ApplicationUser.cs`:
  ```csharp
  using Microsoft.AspNetCore.Identity;

  namespace Identity.Data;

  public class ApplicationUser : IdentityUser<Guid>
  {
      public Guid? CustomerId { get; set; }
  }
  ```
  Kế thừa `IdentityUser<Guid>` (stub hiện tại không kế thừa gì); `CustomerId` là `Guid?` (khớp
  cột `customer_id uuid, nullable` — liên kết logic tới `customerdb.customers`, không FK vật lý).

- [ ] `Data/IdentityDbContext.cs`:
  ```csharp
  using Microsoft.AspNetCore.Identity;
  using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
  using Microsoft.EntityFrameworkCore;

  namespace Identity.Data;

  public class IdentityDbContext(DbContextOptions<IdentityDbContext> options)
      : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
  {
      protected override void OnModelCreating(ModelBuilder builder)
      {
          base.OnModelCreating(builder);
          builder.UseOpenIddict();
      }
  }
  ```
  Giữ style primary-constructor đã có sẵn trong stub, chỉ đổi base class từ `DbContext` sang
  `IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>`. Tên class `IdentityDbContext`
  trùng tên base class (`Microsoft.AspNetCore.Identity.EntityFrameworkCore.IdentityDbContext<T1,T2,T3>`)
  nhưng **hợp lệ** — C# phân biệt theo `(tên, arity)`, arity 0 vs arity 3 không đụng nhau (giống
  `Queue`/`Queue<T>` trong BCL). OpenIddict tự thêm 4 bảng
  (`OpenIddictApplications/Authorizations/Scopes/Tokens`) qua `UseOpenIddict()`, dùng khoá
  `string` mặc định, độc lập với `Guid` của `AspNetUsers` (không FK thật, chỉ cột `Subject`
  string) — không cần `UseOpenIddict<Guid>()`.

- [ ] `dotnet ef migrations add InitialIdentity` (chạy trong `src/Services/Identity`).

- [ ] `Data/Seed/OpenIddictSeeder.cs` — seed client `angular-spa` + toàn bộ scope, chạy sau
  `app.Build()`:
  ```csharp
  using OpenIddict.Abstractions;
  using static OpenIddict.Abstractions.OpenIddictConstants;

  namespace Identity.Data.Seed;

  public static class OpenIddictSeeder
  {
      // Khớp bảng scope ở 01-architecture-overview.md §4
      private static readonly (string Name, string? Resource)[] Scopes =
      [
          ("openid", null), ("profile", null), ("email", null), ("offline_access", null),
          ("customers.read", "customer-api"),    ("customers.write", "customer-api"),
          ("accounts.read", "account-api"),      ("accounts.write", "account-api"),
          ("transactions.read", "transaction-api"), ("transactions.write", "transaction-api"),
          ("cards.read", "card-api"),            ("cards.write", "card-api"),
          ("loans.read", "loan-api"),            ("loans.write", "loan-api"),
          ("payments.read", "payment-api"),      ("payments.write", "payment-api"),
      ];

      public static async Task SeedAsync(IServiceProvider services)
      {
          await using var scope = services.CreateAsyncScope();
          var scopeManager = scope.ServiceProvider.GetRequiredService<IOpenIddictScopeManager>();
          var appManager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

          foreach (var (name, resource) in Scopes)
          {
              if (await scopeManager.FindByNameAsync(name) is not null)
                  continue;

              var descriptor = new OpenIddictScopeDescriptor { Name = name };
              if (resource is not null)
                  descriptor.Resources.Add(resource);

              await scopeManager.CreateAsync(descriptor);
          }

          if (await appManager.FindByClientIdAsync("angular-spa") is not null)
              return;

          var app = new OpenIddictApplicationDescriptor
          {
              ClientId = "angular-spa",
              ClientType = ClientTypes.Public,
              DisplayName = "DigitalBanking Angular SPA",
              RedirectUris = { new Uri("http://localhost:4200/auth-callback") },
              PostLogoutRedirectUris = { new Uri("http://localhost:4200/") },
              Permissions =
              {
                  Permissions.Endpoints.Authorization,
                  Permissions.Endpoints.Token,
                  Permissions.GrantTypes.AuthorizationCode,
                  Permissions.GrantTypes.RefreshToken,
                  Permissions.ResponseTypes.Code,
              },
              Requirements = { Requirements.Features.ProofKeyForCodeExchange }
          };
          foreach (var (name, _) in Scopes)
              app.Permissions.Add(Permissions.Prefixes.Scope + name);

          await appManager.CreateAsync(app);
      }
  }
  ```
  Đã verify trực tiếp từ source OpenIddict 7.6.0: `IOpenIddictScopeManager`/
  `IOpenIddictApplicationManager` với `CreateAsync(descriptor)`, `FindByNameAsync(name)`,
  `FindByClientIdAsync(id)` là đúng API hiện hành. Idempotent (check tồn tại trước khi tạo) —
  khớp mẫu chính thức OpenIddict dùng trong sample "Zirku" (SPA + PKCE, kiến trúc gần giống ở đây).

- [ ] Wiring `Data/` vào `Program.cs` (không lặp lại phần `AddOpenIddict().AddServer()` đã có
  ở checklist "OpenIddict Server" bên dưới):
  ```csharp
  builder.Services.AddDbContext<IdentityDbContext>(options =>
      options.UseNpgsql(builder.Configuration.GetConnectionString("identitydb")));

  // ... AddOpenIddict().AddCore(o => o.UseEntityFrameworkCore().UseDbContext<IdentityDbContext>())
  //     .AddServer(...) — xem "OpenIddict Server" bên dưới

  var app = builder.Build();

  await using (var scope = app.Services.CreateAsyncScope())
  {
      var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
      await db.Database.MigrateAsync();          // shortcut tạm thời — xem rủi ro
      await OpenIddictSeeder.SeedAsync(app.Services);
  }

  await app.RunAsync();
  ```
  Connection string `identitydb` sẽ tự có trong `builder.Configuration` sau khi `AppHost.cs`
  được sửa theo checklist "Hạ tầng" ở trên (`postgres.AddDatabase("identitydb")` +
  `.WithReference(identitydb)`).

**Domain (mỏng — chỉ cho RegisterUser)**
- [ ] `Domain/Email.cs` — Value Object, constructor validate format (không phụ thuộc `System.ComponentModel.DataAnnotations`/EF Core — Domain không được phụ thuộc Infrastructure).
- [ ] `Domain/User.cs` — factory tĩnh `User.Register(Email email)` trả về `(User user, UserRegistered @event)`; không chứa password/hash (đó là việc của `UserManager`, không phải domain concept).
- [ ] `Domain/UserRegistered.cs` — record domain event: `UserId`, `Email`, `RegisteredAt`.

**OpenIddict Server**
- [ ] `Program.cs`: `AddOpenIddict().AddCore(...).AddServer(...)` theo mẫu [03-tech-stack.md](../03-tech-stack.md#oauth2--openid-connect-openiddict) — `AllowAuthorizationCodeFlow().RequireProofKeyForCodeExchange()`, `AllowRefreshTokenFlow()`.
- [ ] Redirect chưa-authenticated ở `/connect/authorize` sang `{AngularOrigin}/login?returnUrl=...` (qua `IOpenIddictServerEvents` hoặc check `User.Identity.IsAuthenticated` đầu pipeline).
- [ ] CORS **chỉ cho `/connect/token`, `/connect/userinfo`** (do `angular-auth-oidc-client` gọi thẳng, cross-origin XHR) — origin Angular, không cần `AllowCredentials()` (dùng Bearer token, không dùng cookie ở các endpoint này).
- [ ] Cookie đăng nhập: `SameSite = None`, `SecurePolicy = Always` → Identity phải chạy HTTPS kể cả dev (xem rủi ro).

**Endpoint nghiệp vụ**
- [ ] `Api/RegisterUserEndpoint.cs` — `[WolverinePost("/account/register")]`: gọi `Domain.User.Register(...)` → nếu hợp lệ, gọi `UserManager.CreateAsync(...)` → trả về `(response, userRegisteredEvent)` để Wolverine cascade publish.
- [ ] `Api/LoginEndpoint.cs` — `[WolverinePost("/account/login")]` (không qua Domain, gọi thẳng `SignInManager`).

**Frontend**
- [ ] Route `/login` (standalone component) trong `apps/portal` — gọi `{gateway}/account/login`/`/account/register`; submit xong thì `window.location.href = returnUrl` (điều hướng nguyên trang, không qua Angular Router, để quay lại domain Identity hoàn tất `/connect/authorize`).
- [ ] `app.config.ts`: `provideAuth()` — `authority: 'https://localhost:5000'` (Identity trực tiếp), `clientId: 'angular-spa'`, `redirectUrl` khớp `/auth-callback`.
- [ ] Route `/auth-callback` theo `angular-auth-oidc-client`.
- [ ] Cấu hình base URL gọi Gateway riêng (env/`apiUrl`) cho `LoginPage` — khác với `authority` (Identity) dùng cho OIDC client.

## Kiểm thử & Verification
- `dotnet build DigitalBanking.slnx` sạch.
- `dotnet run` trên AppHost → Aspire dashboard: `postgres`, `identity-api`, `gateway`, `web`/`portal` đều healthy.
- Luồng tay: mở Angular → Login → redirect `/connect/authorize` (thẳng Identity) → chưa có cookie → redirect `/login` (Angular) → đăng nhập user đã tự tạo qua `POST {gateway}/account/register` (test trước bằng Postman qua Gateway) → submit `POST {gateway}/account/login` → xác nhận cookie được set đúng cho domain Identity (kiểm tra DevTools) → quay lại `/connect/authorize` → thành công → `/auth-callback` nhận được access/id/refresh token, hiển thị claims đúng scope đã cấp.

## Câu hỏi còn mở / rủi ro
- **Cookie Domain qua proxy — rủi ro tăng so với gọi thẳng**: `/account/login` giờ đi qua Gateway, response `Set-Cookie` (không set `Domain` tường minh) sẽ mặc định scope theo **host** trình duyệt thấy (Gateway), không theo port — vì cookie khớp theo host, bỏ qua port, nên ở dev (Gateway và Identity cùng chạy trên `localhost`, khác port) cookie vẫn vô tình hoạt động khi browser sau đó điều hướng thẳng tới Identity. **Ở production, nếu Gateway và Identity nằm trên hostname thật khác nhau, cách này sẽ hỏng** — cần set `Domain=.digitalbanking.<tld>` tường minh lúc `SignInAsync`, hoặc thiết kế lại để 2 service chia sẻ subdomain chung. Ghi rõ để không quên khi triển khai thật.
- **YARP có forward `Set-Cookie` mặc định** — cần verify thực tế lúc code, không giả định suông.
- **Consent screen**: tạm auto-approve, chưa có trang xin đồng ý scope thật.
- **`CustomerId`** để nullable, chưa liên kết thật tới Customer service (chưa tồn tại) — khi có, có thể thêm method `User.LinkToCustomer(CustomerId)` vào Domain layer thay vì set trực tiếp property.
- **`RegisterUser` chưa publish integration event** `UserRegistered` (Identity chưa wiring RabbitMQ/outbox lần này).
- **Gateway tối thiểu ở đây sẽ cần mở rộng lại** khi làm `docs/plans/gateway.plan.md` cho Giai đoạn 2 — tránh 2 lần cấu hình xung đột nhau.
- **Migration inline khác khuyến nghị hiện tại của Aspire**: tài liệu Aspire hiện đề xuất 1 worker/service riêng chạy migration (`BackgroundService` + `WaitForCompletion()` trong AppHost) thay vì gọi `MigrateAsync()` ngay trong `Program.cs` của service. Ở đây **cố tình** không theo, vì đã chốt trước đó không muốn thêm 1 service chuyên migrate — chấp nhận đánh đổi (mọi service tự migrate DB của mình lúc start; không phải vấn đề bây giờ vì mỗi service có DB riêng, nhưng có thể cần xem lại nếu sau này nhiều service cùng migrate 1 lúc gây tranh chấp).
- **`db.Database.MigrateAsync()` yêu cầu đã chạy `dotnet ef migrations add InitialIdentity` trước** — nếu chưa có migration nào, lệnh này sẽ không tạo được bảng.
