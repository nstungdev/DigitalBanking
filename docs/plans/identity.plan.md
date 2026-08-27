# Plan: Identity

> Trạng thái: Draft. Cập nhật lần cuối: 2026-08-26.

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
- [ ] `Data/IdentityDbContext.cs`, `Data/ApplicationUser.cs` (+ `CustomerId`), gọi `builder.UseOpenIddict()` trong `OnModelCreating`.
- [ ] `dotnet ef migrations add InitialIdentity` (chạy trong `src/Services/Identity`).
- [ ] `Data/Seed/OpenIddictSeeder.cs` — seed client `angular-spa` + toàn bộ scope, chạy sau `app.Build()`.

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
