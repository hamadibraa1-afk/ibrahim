# Patient app conventions (web-patient)

Next.js 15 App Router, React 19, next-intl 4, Tailwind 3. Every page lives under `src/app/[locale]/`.

## Routes

Public (no session): `/` P01 · `/how-it-works` P12 · `/legal` P13 · `/crisis-resources` P14 · `/start/eligibility` P15 ·
`/start/signup` P02 · `/start/verify` P16 · `/start/recovery-codes` P17 · `/login` P18 · `/login/mfa` P19 · `/recover` P20 ·
`/offline`, `/session-expired`, `/maintenance` P35 (404 is `[locale]/not-found.tsx`).

Signed in, under the `(app)` route group, whose layout already requires a session: `/start/consents` P03 · `/start/safety-info` P21 ·
`/start/path` P22 · `/start/safety-check` P23 · `/intake` P04 · `/intake/summary` P05 · `/crisis` P06 · `/waiting` P36 ·
`/matches` P07 · `/waitlist` P56 · `/therapists` P24 · `/therapists/[id]` P25 · `/therapists/[id]/book` P08 · `/appointments` P27 ·
`/appointments/[id]` P28 · `/appointments/[id]/confirmed` P26 · `/home` P09 · `/homework` P29 · `/homework/[id]` P10 ·
`/check-in` P30 · `/safety-plan` P37 · `/settings` P31 · `/settings/security` P32 · `/settings/notifications` P33 ·
`/settings/safety` P34 · `/privacy` P11.

Put a page at `src/app/[locale]/(app)/<route>/page.tsx` (signed in) or `src/app/[locale]/(public)/<route>/page.tsx`.
Route groups don't appear in the URL.

## Data

- Contract: `src/lib/api/types.ts` and `docs/api-v1.md`. Do not invent endpoints or fields. If something is missing, say so in your report.
- Server components: `serverGet<T>('home')` and `getMe()` from `@/lib/api/server` (they redirect on 401 or a crisis lock). `publicGet<T>('meta/config')` for public data.
- Client components: `api<T>('mood', { method: 'POST', body })` from `@/lib/api/client`. It goes through the BFF, refreshes once on 401, and sends the user to `/crisis` if the API says the chat is locked.
- Sign-in steps that return tokens use `authApi('signup-verify' | 'login' | 'login-mfa' | 'logout', body)`. Tokens never reach the browser.
- Errors are `ApiError` with `problem.title/detail` already in the user's language: show them, never a hard-coded English string. `fieldErrors()` maps validation errors to fields.
- If a gated response has `crisis: true` (GateOutcome), navigate to `/crisis` at once with `router.replace`.
- After onboarding steps, use `onboardingStep(me)` / `landingPath(me)` from `@/lib/onboarding` to choose the next screen.

## Language and direction

- Every visible string comes from `messages/{ar,en}/<namespace>.json`. One top-level key equals the file name. The namespaces are registered in `src/i18n/namespaces.ts` (already listed; don't edit that file). Arabic and English must have the same keys and placeholders (a unit test checks this).
- Arabic copy follows the design file's text exactly where the mockup has it. English is a natural translation, not a transliteration.
- Use logical CSS only (`ms-`, `me-`, `ps-`, `pe-`, `start-`, `end-`, `text-start`). Never use `left/right`, `ml/mr`, or `pl/pr`. Arrows get `rtl:rotate-180`.
- Phone numbers, codes, e-mail, times inside Arabic text: wrap in `<bdi>` or set `dir="ltr"` on the input.
- Dates and times: `formatDate`, `formatTime`, `formatDateTime`, `formatRelativeDays` from `@/lib/format` (Latin digits, Riyadh time by default; pass `me.timeZone` when you have it).
- The header (brand, language toggle, Help now) is in the layout. Do not add another.

## UI

- Primitives in `src/components/ui/`: Button/ButtonLink, Card, TextField/TextArea, Switch, Chip/Tag, OptionButton, Avatar, ProgressBar, Notice, PageHeading, BackLink, StepIndicator, BottomNav/BottomNavSpacer, Spinner, EmptyState. Don't modify them. Feature components go in `src/components/<feature>/`.
- Tokens: `bg`, `surface`, `ink`, `muted`, `primary`, `primary-strong`, `accent`, `tint-teal`, `tint-sand`, `help`, `line`. Dark mode works automatically.
- Mobile first (390 px design, max width 768 px content). Touch targets ≥ 44 px. Every input has a label. Every icon-only button has `aria-label`. Errors use `role="alert"`.
- The mockups are in the scratchpad (`mockups/project/<id>-*.dc.html`, screenshots in `shots/`). Match layout, order and copy.

## Tests

- Unit tests: `tests/unit/<feature>/*.test.tsx` with Vitest + Testing Library. Render with `renderWithIntl(ui, 'ar' | 'en')` from `tests/unit/intl.tsx`. Mock `@/lib/api/client` and `@/i18n/navigation`.
- Test both languages for at least one screen per feature, including direction-sensitive bits.
- Run only your own tests while other features are being built: `npx vitest run tests/unit/<feature>`. Also run `npx tsc --noEmit` and `npx next lint`, and fix errors in your files.
