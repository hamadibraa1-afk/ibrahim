# Digital Suggestions System — Work Report with Evidence

Sharjah Charity International · .NET 8 + Angular 17

Every claim below is backed by a screenshot taken from the running application, an automated
test, or a measurement script. The screenshots and scripts are in this folder.

| Evidence | Result |
|---|---|
| Backend integration tests (`cd api && dotnet test`) | **20 / 20 pass** |
| Angular production build (`npx ng build`) | **succeeds** |
| Real-time browser test after the redesign | badge, queue and open page update without reload |
| Revoked-session browser test | open tab signed out **153 ms** after the admin suspended the account |
| Text contrast, 14 pages, 942 text elements (WCAG AA) | **382 failures before → 0 after**, lowest ratio **4.83 : 1** |

---

## Part 1 — Continued changes

### 1.1 Revoked sessions end immediately in open tabs

**Before:** suspending an account, changing its role or resetting its password deleted the
sessions on the server, but a tab that was already open kept showing data until its next request.

**Now:** the server pushes `sessionRevoked` over SignalR to that user's open tabs. Each tab
re-checks its own session; a revoked one gets 401 and lands on the login page at once, while a
session that was deliberately kept (for example, the tab that changed the password) carries on.

Measured in a real browser: an employee had *My proposals* open; the admin suspended the account;
**the employee's tab moved to the login page by itself 153 ms later** with the "session expired" notice:

![Signed out automatically after suspension](screenshots/after-16-revoked-signed-out.png)

Code: `Security/SessionService.cs` (`RevokeAllAsync`), `web/.../realtime.service.ts` (`sessionRevoked`).
Test: `Suspending_a_user_ends_their_open_sessions_immediately`.

### 1.2 The SLA monitor runs on one server at a time

**Before:** with several API instances behind a load balancer, each one would run the 15-minute
SLA pass and send duplicate escalations.

**Now:** each pass first takes a database lease (`JobLeases` table). One conditional `UPDATE`
decides the holder, so two instances can never both win; if the holder crashes, its lease expires
and another instance takes over. No Redis or other infrastructure is needed.

Live evidence: the monitor ran with a 1-minute interval during this session, took the lease, and
escalated an overdue proposal through both levels on its own — log line:
`SLA pass: SlaRunResult { Level1Escalations = 1, Level2Escalations = 1 }`. The result in the UI:

![Automatic escalation, level 2, rerouted to the line manager](screenshots/after-05-proposal-escalated.png)

Test: `Only_one_instance_holds_the_sla_monitor_lease_until_it_expires` (renewal, refusal while
held, hand-over after expiry).

### 1.3 Login rate limit is configurable

The per-IP login throttle (10 attempts / 5 minutes) is now `Security:LoginRateLimit` in
`appsettings.json`, because offices behind one shared IP address share that budget.

---

## Part 2 — Glassmorphism redesign

### What was applied

| Requirement | How it is met |
|---|---|
| **Frosted glass** | Panels use `rgba(255,255,255,0.62)` with `backdrop-filter: blur(18px) saturate(165%)`; dialogs and menus `0.82` with `blur(26px)`; the header `blur(20px)`; the sidebar is *dark* glass `rgba(16,58,41,0.86)` with `blur(24px)`. |
| **1px light borders** | `1px solid rgba(255,255,255,0.7)` on every panel, plus an inner top highlight (`inset 0 1px 0`). |
| **Soft glowing shadows** | Layered green-tinted shadows; clickable cards and primary buttons glow on hover. |
| **Vibrant background** | Fixed multi-layer gradient built from the logo colours (green `#3B7E49` family and orange `#DB9357` family), so the glass has something to frost. Kept light behind page titles, which sit directly on it. |
| **Images preserved** | The app has no `<img>` tags; its visual assets are inline SVGs — the logo (sidebar, login, mobile login), all icons, and the dot-pattern background on the login identity panel. **None were changed or removed**; the login identity panel keeps its own gradient and pattern untouched. |
| **Functionality intact** | No class name, ID, binding or TypeScript logic was removed. Glass is applied through the existing classes (`.card`, `.btn`, `.field-input`, `.badge`) plus six added helper classes (`glass`, `glass-pad`, `glass-strong`, `glass-bar`, `glass-dark`, `glass-overlay`) placed *next to* the existing ones. All browser tests pass on the new design. |
| **Readable text** | Measured, not estimated — see below. Fallbacks: `prefers-reduced-transparency` and browsers without `backdrop-filter` get near-opaque panels; `prefers-reduced-motion` disables hover motion; print output has no effects. |

Files: `web/src/styles.scss` (the design system), `web/tailwind.config.js` (text tokens),
6 templates (added classes only), `my-proposals` (one class binding fixed — see below).

### Accessibility: measured contrast

`docs/tools/contrast.mjs` takes each visible text element's real colour and compares it with the
pixels actually rendered behind it (a second screenshot with text hidden), so blur and
transparency are accounted for. Threshold: WCAG AA (4.5 : 1, or 3 : 1 for large text).

| Page | Before: failures (lowest) | After: failures (lowest) |
|---|---|---|
| Login | 8 (2.38) | 0 (5.38) |
| Dashboard | 23 (2.38) | 0 (4.89) |
| Impact & ROI | 32 (2.38) | 0 (4.89) |
| Proposal with impact | 21 (2.38) | 0 (4.89) |
| Escalated proposal | 18 (2.34) | 0 (4.89) |
| User management | 31 (**1.07**) | 0 (4.84) |
| Form settings | 33 (2.38) | 0 (4.89) |
| Audit log | 22 (2.38) | 0 (4.84) |
| Screening queue | 32 (2.38) | 0 (4.89) |
| Screening detail (blind) | 12 (2.34) | 0 (4.89) |
| Notifications | 13 (2.33) | 0 (4.89) |
| Manager — rerouted | 21 (2.38) | 0 (4.89) |
| Employee — my proposals | 105 (2.07) | 0 (4.83) |
| Submission form | 11 (2.38) | 0 (4.89) |
| **Total** | **382** | **0** |

Raw outputs: `contrast-before.txt`, `contrast-after.txt`.

### Defects found and fixed during the redesign

The contrast measurement exposed three bugs that already existed in the original frontend:

1. **Invisible buttons on User management** (1.07 : 1). The page used `bg-teal` / `text-navy`,
   colours that were never defined in the Tailwind config, so "+ Add user" and the dialog's
   "Add user" button rendered white-on-white. The colours are now defined (class names unchanged).
2. **Missing ✓ in the progress tracker** (2.07 : 1). An `[ngClass]` object with several keys
   sharing `text-white` made Angular remove that class again, so the tick was dark grey on dark
   green. Replaced by a method returning one class string.
3. **Light secondary text and orange badges** (2.3–2.6 : 1). Grey `#9CA3AF` text and white text
   on the logo orange `#DB9357` were below AA even on plain white. Darker shades of the same
   colours are now used (same class names).

### Before / after

**Login** — the identity panel (logo, dot pattern, gradient) is untouched; the form is now a glass card over the vibrant background.
![Login](screenshots/compare-login.png)

**Dashboard**
![Dashboard](screenshots/compare-dashboard.png)

**Proposal with the post-implementation impact panel (ROI 371.4 %)**
![Proposal impact](screenshots/compare-proposal-impact.png)

**User dialog** — the "Add user" button is visible now; the page behind is frosted.
![User dialog](screenshots/compare-user-modal.png)

**Blind screening queue** — the proposer still shows as 🔒 masked.
![Screening](screenshots/compare-screening-blind.png)

**Employee progress** — the ✓ marks are visible now.
![Employee progress](screenshots/compare-employee-progress.png)

### Full gallery (after)

| | |
|---|---|
| ![](screenshots/after-01-login.png) Login | ![](screenshots/after-02-dashboard.png) Dashboard |
| ![](screenshots/after-03-impact-roi.png) Impact & ROI report | ![](screenshots/after-04-proposal-impact.png) Proposal impact panel |
| ![](screenshots/after-05-proposal-escalated.png) Auto-escalated proposal | ![](screenshots/after-06-users.png) User management |
| ![](screenshots/after-07-user-modal.png) Add-user dialog with manager field | ![](screenshots/after-08-form-settings.png) Form settings incl. impact section |
| ![](screenshots/after-09-audit.png) Audit log | ![](screenshots/after-10-screening-blind.png) Blind screening queue |
| ![](screenshots/after-11-screening-detail-blind.png) Blind screening detail | ![](screenshots/after-12-notifications.png) Real-time notifications |
| ![](screenshots/after-13-manager-rerouted.png) Manager: rerouted proposal | ![](screenshots/after-14-employee-progress.png) Employee progress |
| ![](screenshots/after-15-submit-form.png) Submission form | ![](screenshots/after-16-revoked-signed-out.png) Signed out after suspension |

---

## Reproducing the evidence

```bash
# backend + frontend
cd api/ProposalSystem.Api && dotnet run          # http://localhost:5199
cd web && npm install && npx ng serve            # http://localhost:4200

# scripts (need: npm i playwright pngjs)
node docs/tools/shots.mjs <output-folder>         # the screenshots
node docs/tools/contrast.mjs                      # the contrast table
node docs/tools/revoke.mjs <output-folder>        # instant sign-out after suspension
# BASE_URL and CHROMIUM environment variables override the defaults.

cd api && dotnet test                             # 20 integration tests
```

The screenshot data (8 proposals at every stage, one verified ROI measurement, one overdue
proposal escalated by the live monitor) was created through the API only, apart from moving one
deadline into the past so the live monitor would escalate it.
