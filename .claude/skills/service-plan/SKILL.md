---
name: service-plan
description: >
  Generates docs/plans/<service-name>.plan.md — a concrete implementation guide for building
  one backend microservice or one Angular GUI/app in this repo. Use this skill whenever:
  - The user asks for a plan/implementation guide for a specific service or GUI ("viết plan
    cho Account service", "tạo file plan implement portal", "generate plan.md cho Loan",
    "hướng dẫn cách implement Transaction service")
  - The user mentions docs/plans/ or a "<name>.plan.md" file
  Always interviews the user with the AskUserQuestion tool for anything not already decided in
  docs/01-04 before writing the file — never silently guess scope, endpoints, or screens that
  aren't already documented. Ask everything unresolved in one batched call, not one at a time.
  Reply in the same language the user is chatting in — Vietnamese in, Vietnamese out;
  otherwise English.
---

# Service Plan

Writes `docs/plans/<service-name>.plan.md` — a single-file implementation guide detailed
enough to actually start coding from, for one backend microservice (e.g. Account, Loan) or one
Angular app/feature under `src/Web` (e.g. `portal`). Unlike a quick architecture doc, this file
is scoped to **one buildable unit** and doubles as both blueprint and progress checklist, since
this repo has no separate task-tracking skill.

This is not the same thing as Claude Code's built-in Plan Mode (which plans the *current turn's*
work and disappears after). `docs/plans/*.plan.md` is a persisted, committed artifact meant to
outlive the session and guide implementation across many future turns.

## Step 0 — Identify the target and check current state

- If the user already named the service/GUI, use it. If ambiguous (e.g. "viết plan cho service
  tiếp theo"), ask which one — don't guess from the roadmap order alone.
- Resolve the filename slug: match the bounded context names already used in
  `docs/01-architecture-overview.md` for backend (`identity`, `customer`, `account`,
  `transaction`, `card`, `loan`, `payment`, `notification`, `audit`, `gateway`), or the app
  folder name under `src/Web/apps/` for frontend (e.g. `portal`).
- Check whether `docs/plans/<slug>.plan.md` already exists:
  - **Doesn't exist** → Mode: create (Steps 1-4 below).
  - **Exists** → read it first, then ask the user whether they want to (a) revise it because
    scope/design changed, (b) just sync its checklist against what's actually been built so
    far, or (c) regenerate from scratch. Don't silently overwrite a plan someone may have hand-
    edited.
- Check the actual repo for existing code at `src/Services/<Name>/` (backend) or
  `src/Web/apps/<name>/` (frontend) — read what's there. A plan for a service that's half-built
  already must reflect that, not describe it as if starting from zero.

## Step 1 — Pull everything already decided (don't re-ask what's documented)

Read before asking anything:
- **`docs/01-architecture-overview.md`** — the service's row (responsibility, DB), every event
  catalog row where it's publisher or consumer, its OAuth2 scopes/audience if backend.
- **`docs/02-database-schema.md`** — its DB schema section (backend only).
- **`docs/03-tech-stack.md`** — the relevant tech patterns (Wolverine.Http/outbox/saga + EF
  Core + OpenIddict validation for backend; standalone/signals/zoneless + OIDC client for
  frontend).
- **`docs/04-roadmap.md`** — the specific "Giai đoạn" entry for this service/GUI — its stated
  tasks and "Hoàn thành khi" criteria are the baseline scope and Definition of Done unless the
  user says otherwise.

Everything found here is a **fact to restate in the plan, not a question to ask**.

## Step 2 — Interview only the genuine gaps

Compare what Step 1 gave you against what's actually needed to start writing code. Ask about
gaps only — batch every open question into a single `AskUserQuestion` call. Typical gaps by
target type (skip any already answered by the docs or by the user's original request):

**Backend service:**
- Phạm vi lần này: toàn bộ use case của service, hay 1 tập con trước (nếu vậy, liệt kê use case
  cụ thể để người dùng chọn)?
- Có implement luôn phần consume integration event từ service khác trong lượt này không, hay
  chỉ command/query trước, event sau?
- Có viết test (unit cho domain, integration cho handler) trong phạm vi plan này không?
- Enforce OAuth2 scope ngay từ đầu hay tạm bỏ qua lúc mới scaffold (docs đã định nghĩa scope,
  nhưng có thể muốn hoãn lúc khung sườn chưa chạy được)?

**Frontend/GUI (Angular):**
- Màn hình/luồng cụ thể nào trong phạm vi lần này, nếu roadmap chưa liệt kê chi tiết?
- Dùng lại component/service từ lib dùng chung (nếu đã có), hay tạo mới trong phạm vi app này?
- Có cần state phức tạp hơn signal đơn giản không (đã ghi trong docs là mặc định dùng signal).

**Chung cho cả 2:**
- Có ràng buộc thời gian/độ ưu tiên nào khác so với thứ tự mặc định trong roadmap không?

If Step 1 already answers a question outright (e.g. roadmap Giai đoạn 4 already lists the exact
endpoints for Account), don't ask it again — at most confirm briefly as part of the same batch
if truly ambiguous.

## Step 3 — Write the plan

Fill this structure, saved to `docs/plans/<slug>.plan.md` (create the `docs/plans/` folder if
it doesn't exist yet):

```markdown
# Plan: <Service/GUI name>

> Trạng thái: Draft. Cập nhật lần cuối: <YYYY-MM-DD>.

## Bối cảnh
- Vai trò trong kiến trúc — 1-2 câu, link [01-architecture-overview.md](../01-architecture-overview.md).
- Giai đoạn roadmap tương ứng — link [04-roadmap.md](../04-roadmap.md#giai-đoạn-n--...).
- Trạng thái hiện tại của code (chưa có gì / đã có scaffold ở đâu, còn thiếu gì).

## Phạm vi lần này
- Trong phạm vi: {chốt qua Step 2}
- Ngoài phạm vi / để sau: {chốt qua Step 2}

## Interface / Contract
- Backend: bảng endpoint (method, path, request, response, scope yêu cầu) — trích/khớp với
  event catalog và OAuth2 scope đã có trong 01; bảng schema liên quan trích từ 02.
- Frontend: bảng route/component chính, service/API nào nó gọi qua Gateway.

## Các bước implement
- [ ] Checklist theo thứ tự, đủ chi tiết để bắt tay code ngay (project/folder cần tạo, file cụ
      thể, migration, endpoint, component...). Đây vừa là blueprint vừa là progress tracker —
      tick trực tiếp khi làm xong, khác quy ước "frozen" của lean-plan vì repo này không có
      skill task riêng.

## Kiểm thử & Verification
- Cách build/run/test cụ thể; Definition of Done — kế thừa "Hoàn thành khi" của giai đoạn
  roadmap tương ứng, cụ thể hoá thêm cho đúng phạm vi đã chốt ở trên.

## Câu hỏi còn mở / rủi ro
- Bất kỳ điều gì chưa chắc chắn, cần quyết định thêm khi implement.
```

Keep the "Các bước implement" checklist concrete — each item should be specific enough to
execute without re-deriving the approach (name the actual file/endpoint/component, not "thêm
logic cần thiết").

## Step 4 — Confirm and save

Show the full draft to the user before saving. Ask if anything needs adjusting. Once confirmed,
write it to `docs/plans/<slug>.plan.md`.

## Principles

- **Extract from docs first, interview second** — `docs/01-04` already answer most structural
  questions; only ask what they genuinely don't cover.
- **One buildable unit per file** — a plan covers exactly one service or one GUI/app, never
  "the whole system" (that's what `04-roadmap.md` is for).
- **Blueprint + tracker in one file** — checkboxes are expected to be ticked as work progresses,
  unlike a frozen spec document.
- **Reflect reality, not a blank slate** — if code already exists for this target, the plan
  describes what's left, not what to build from zero.
- **Batch the interview** — all open questions go into a single `AskUserQuestion` call per
  round, not a back-and-forth of single questions.
