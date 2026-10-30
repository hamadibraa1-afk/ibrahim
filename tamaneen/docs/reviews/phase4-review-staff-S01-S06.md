# R4 — Staff portal conformance: S01, S02, S03, S04, S06

Scope: spec `g3.json` (S01–S04, S06), mockups `S0x-*.dc.html`, guide §6.2/§7.5–7.7. Code: `web-staff/src/app`, `backend/src/Tamaneen.Api/Endpoints/StaffEndpoints.cs`, `backend/src/Tamaneen.Crisis.Worker/Endpoints/ConsoleEndpoints.cs`. All paths below are relative to `/home/user/ibrahim/tamaneen`.

**Route note.** The staff portal has no route-rename document. `docs/frontend-patient.md` covers the patient app only, and neither `README.md` nor `docs/api-v1.md` maps staff routes. The portal has four routes in total (`web-staff/src/app/app.routes.ts:10-23`): `/login`, `/dashboard`, `/crisis` and `/home`. Spec routes such as `/staff/login`, `/t/dashboard` and `/s/crisis/:eventId` are therefore undocumented renames. The rename is listed once per screen. It is minor where nothing links to the spec path, and a blocker where something does: the paging link `/crisis/{id}` has no matching route (S04).

---

## S01 — Therapist dashboard (`/t/dashboard` → built as `/dashboard`)

✅ Matches
- The page exists. The therapist guard is at `app.routes.ts:20`, and `homeRoute()` sends therapists here after sign-in (`core/session/staff-role.ts:58-60`).
- Greeting by part of day with the display name, and today's date (`dashboard.component.html:6-9`).
- Crisis alert banner: live upsert from `/hubs/crisis` (`dashboard.component.ts:62-65,140-142`), reload on reconnect, and `إقرار بالتنبيه` → `POST /v1/crisis/events/{id}/acknowledge` (`:91-93`). A 409 shows the server text "تولّى شخص آخر هذه الحالة من قبل." in the alert.
- Four stat cards: today's sessions, files waiting for approval, check-ins and capacity used/max (`html:43-62`).
- "مرضى جدد ينتظرون اعتماد الملف" table: pseudonym, risk tag, PHQ-9, GAD-7, first session. The first row's button is filled (`html:94-99`). Sorting is by risk (server). There is an empty state (`html:107`).
- Today's sessions list: time, pseudonym, session number, risk tag, and `تسجيل النتيجة` (menu: attended / no-show / cancelled) → `POST /v1/staff/appointments/{id}/outcome` (`html:111-149`). There is an empty state.
- The risk badge shows the number plus a word ("3 · عالٍ"), not colour alone (`shared/risk-tag.component.ts:12`).
- Only assigned patients are shown: explicit `CareAssignments` filters plus RLS (`StaffEndpoints.cs:97`). An unassigned pseudonym returns 404 (`StaffEndpoints.cs:326-341`). Opening a file writes an audit row (`StaffEndpoints.cs:191`).
- Pseudonyms only; no real names.

| severity | what's missing | design ref | code evidence | suggested fix |
|---|---|---|---|---|
| **blocker** | If the first dashboard load fails, crisis alerts are never shown, including live hub pushes. The whole alert section sits inside `@if (data(); as d)`, and `applyAlert` does `d && …`, so pushes are dropped while `data` is null. | S01 states: «خطأ: رسالة إعادة المحاولة لكل قسم على حدة **دون إخفاء التنبيهات**»; rules: «تنبيهات الخطر لا تُخفى ولا تُكتم من هذه الصفحة»; «شريط التنبيه لا ينتظر بقية الصفحة» | `dashboard.component.html:14-16`; `dashboard.component.ts:140-142` | Keep alerts in their own signal, loaded from `GET /v1/crisis/events/open` independently of `/v1/staff/dashboard`, and render the banner outside the `data()` block. |
| **major** | `إحالة للمناوب` fails on an unacknowledged alert. The client posts `{kind:'handover'}`, and `CrisisEvent.HandOver` throws `CrisisNotAcknowledged` unless the status is Acknowledged. There is also no note dialog and no navigation to `/t/alerts`. | «يفتح حوارًا قصيرًا لإضافة ملاحظة، ثم يترك الحدث مع المناوب دون أخذ الملكية … POST …/actions (type=Note) ثم الانتقال إلى /t/alerts» (SB-11) | `dashboard.component.ts:95-97`; `backend/src/Tamaneen.Crisis/CrisisEvent.cs:94-99`; the unit test mocks success (`dashboard.component.spec.ts:102-105`) | Open the note dialog and send `kind:'note'` (or add a "refer" kind that is valid while Open). Show the button only where it is valid. |
| **major** | No "reconnecting" strip and no 5-second polling when SignalR drops. The dashboard reloads only on `reconnected`. The hub retries every 10 s, and the status is never shown here. | «انقطاع SignalR: شريط صغير «جارٍ إعادة الاتصال» مع استطلاع كل 5 ثوانٍ (EC-03)» | `dashboard.component.ts:62-65`; `core/realtime/crisis-hub.service.ts:31` (status is shown only in `crisis-console.component.html:18-20`) | Show the `hub.status()` chip and poll the open alerts every 5 s while it is not `connected`. |
| **major** | `مراجعة الملف` opens a modal (`IntakeDialogComponent`) instead of navigating to S02 `/t/patients/:id/intake`. The modal is a much smaller view (see S02). | «مراجعة الملف … /t/patients/:id/intake (S02)» | `dashboard.component.html:96-98`; `dashboard.component.ts:116-138` | Route to the S02 page once it exists. |
| **major** | `الملف` (today's sessions) opens the same intake modal, not the patient overview. | «الملف … يفتح نظرة عامة على المريض. /t/patients/:id (S14)» | `dashboard.component.html:140-142` | Link to `/t/patients/:id` when S14 exists. Until then, label it as the intake file. |
| minor | `تسجيل النتيجة` is enabled before the session starts. The server rejects it (`AppointmentNotStarted`, `Appointment.cs:114-117`), but the button should be disabled. The menu also adds a "cancelled" outcome that the design does not have. | «يظهر فقط بعد بدء موعد الجلسة»; rule «زر تسجيل النتيجة معطل قبل وقت بدء الجلسة»; options «تمت الجلسة أو لم يحضر» | `dashboard.component.html:131`; `dashboard.component.ts:22` | `[disabled]="now < s.startsAt"`. Drop or justify `cancelled`. |
| minor | The `التقرير` link (weekly report ready) is absent. Its target, S17, is not built. | «التقرير … يظهر عندما يكون التقرير الأسبوعي جاهزًا … /t/patients/:id/reports (S17)»; mockup «تقرير أسبوعي جاهز التقرير» | `dashboard.component.html:120-123` shows only the "متابعة جديدة" text | Add it when S17 exists. |
| minor | The banner shows only the server title and body ("خطر 4 · P-…", "أقرّ خلال n دقيقة…"). The triggering rule (CR-4), the source and the elapsed time are missing, although `sourceLabel` and `openedAtUtc` are in the payload. | «رقم المريض المستعار، القاعدة المُطلِقة (مثل CR-4)، المصدر (متابعة واجب)، الوقت المنقضي، وحالة إقرار المناوب»; mockup «أطلقت قاعدة CR-4 قبل 6 دقائق» | `dashboard.component.html:22-23` | Render `sourceLabel`, the rule IDs and the elapsed time. |
| minor | Acknowledged alerts stay on screen with a "تم الإقرار" tag instead of disappearing. The banner region is `aria-live=polite` instead of `role=alert`. | «role=alert … ويختفي بعد الإقرار أو الإغلاق» | `dashboard.component.html:15,31-33` | Remove the alert when it is acknowledged, or confirm this deviation with design. Use `role="alert"`. |
| minor | The 409 conflict message does not name the role. | «أُقِرّ مسبقًا من [الدور]» (EC-35) | `Tamaneen.Crisis.Worker/Resources/CrisisResource.ar.resx:24` | Include the role or name of whoever acknowledged. |
| minor | The stat label is "متابعات جديدة هذا الأسبوع", but the design says "تقارير أسبوعية جديدة". There are no initials in a circle. The date uses the platform time zone, not the therapist's (S32). | Elements «بطاقات الملخص», «التحية والتاريخ … مع الأحرف الأولى في دائرة» | `assets/i18n/ar.json` `dashboard.stats.newCheckins`; `StaffEndpoints.cs:90`; `layout/shell.component.html:5-7` | Copy fix. Add an initials avatar. Use the per-user time zone later. |
| minor | Undocumented route: `/dashboard` instead of `/t/dashboard`. | route «/t/dashboard» | `app.routes.ts:20` | Rename, or document the mapping as the patient app does. |
| minor | Loading uses a single progress bar, not skeletons per section. | «تحميل: هياكل رمادية للبطاقات والجداول» | `dashboard.component.html:1-3` | Add skeletons. |

---

## S02 — Clinical file and evidence inspector (`/t/patients/:id/intake`)

**What exists:**
- **UI:** no route and no page. The only S02-like UI is the modal `features/dashboard/intake-dialog.component.ts`, opened from S01. It shows:
  - risk tag, PHQ-9, GAD-7 and C-SSRS positive/negative/missing (`:35-49`);
  - the submission date;
  - five sections taken from the patient's own reviewed summary (concern, goals, supports, priorCare, extra), with patient addenda appended to "extra";
  - an audit notice;
  - an `اعتماد الملف` button that turns into a "الملف معتمد" tag once approved (`:67-76`).
- **API:** only two endpoints (`StaffEndpoints.cs:58-59`).
  - `GET /v1/staff/patients/{pseudonymId}/intake` returns `StaffIntakeView`. It is audited (`patient.file_opened`), returns 404 when the patient is not assigned, and raises the risk to the maximum crisis level.
  - `POST /v1/staff/patients/{pseudonymId}/intake/approve` returns 204. It approves the latest submitted intake, or reviews the triage case for direct-path patients. It is audited.
- **Absent:** none of the spec's S02 endpoints exist. A backend grep finds nothing for `evidence`, `dismiss-item`, `file-feedback`, `risk-flag`, `crisis/manual`, `If-Match` or `ETag`.

| severity | what's missing | design ref | code evidence | suggested fix |
|---|---|---|---|---|
| **blocker** | The S02 page is missing. There is no `/t/patients/:id/intake` route, no patient header, and no tabs (overview, clinical file, plan, homework, reports). | route «/t/patients/:id/intake»; elements «رأس المريض», «تبويبات ملف المريض» | `app.routes.ts:19-23` (no patient routes); `staff-role.ts:37` (`IMPLEMENTED_ROUTES` = dashboard, crisis) | Add a `t/patients/:id` shell with tabs and an `intake` child. |
| **blocker** | `إطلاق تنبيه طوارئ` (manual crisis) is missing in both UI and API. This is a safety feature. | SB-18 «حوار تأكيد مع اختيار المستوى (عالٍ/حرج) وملاحظة … POST /v1/crisis/manual» | No endpoint in `ConsoleEndpoints.cs:87-95` or `StaffEndpoints.cs:51-67` | Add a worker endpoint `POST /v1/crisis/manual` (level 3 or 4, note; opens the event, locks the chat, pages) and a confirm dialog. |
| **blocker** | `رفع مستوى الخطر` is missing in both UI and API. Only an internal `TriageCase.RaiseRisk` exists. | SB-17 «حوار لاختيار مستوى أعلى من الحالي وسبب إلزامي … POST /v1/patients/{id}/risk-flag» | `Tamaneen.Clinical/Intake/TriageCase.cs:78-84` (domain only) | Add `POST …/risk-flag` (higher level only, reason required, audited) and its dialog. |
| **major** | No AI intake summary with per-statement evidence. The view shows the patient's own sections, not the AI draft. The design's sections (chief complaint, goals, protective factors, hypotheses marked «ليست تشخيصًا») are not modelled. The `EditedByPatient` flag is stored but dropped, so there is no «صحّحه المريض» mark. | element «ملخص الاستقبال … فرضيات للاستكشاف (ليست تشخيصًا) … علامة «صحّحه المريض»»; rule «الجمل بلا مصدر لا تُعرض أصلًا (EC-15)» | `StaffEndpoints.cs:37,176-182`; `IntakeEndpoints.cs:39` (`SectionDto(Text, EditedByPatient)`) | Store statements with a `statementKey` and source turn IDs. Return them with an edited-by-patient flag. Always tag hypotheses. |
| **major** | No intake chat transcript (turn number, speaker, «مصدر» highlight, 90-day deletion notice), and no `المصدر: n رسائل` button or `GET …/evidence/{statementKey}`. | element «محادثة الاستقبال»; SB-12 | No endpoint. Messages exist encrypted (`IntakeEndpoints.cs:325-333`) | Add an audited evidence endpoint and a transcript panel with a "source removed" state. |
| **major** | No `تعديل` or `استبعاد` per statement, and no `POST …/file/dismiss-item` (reason required, kept in audit). | SB-13, SB-14 | Absent | Implement the inline edit (saved with approve) and a dismiss dialog with a reason. |
| **major** | Approve sends no edits and no `If-Match`, and there is no 412 handling. The approved state shows no approver name or date. | SB-15 «يرسل If-Match؛ عند 412 يعرض الحالة الأحدث»; state «الملف معتمد: … مع اسم المعتمِد وتاريخه» | `intake-dialog.component.ts:112-122`; `StaffEndpoints.cs:198-217` | Add a version/ETag, approve with an edits payload, and return the approver and time. |
| **major** | No 1–5 usefulness rating and no `POST …/file-feedback`. | SB-16 | Absent | Add the rating control and endpoint. |
| **major** | `الإجابات بالتفصيل` is missing (per-item answers and instrument version). PHQ-9 item 9 is not shown, and there are no severity labels ("متوسط إلى شديد"). | button «الإجابات بالتفصيل»; element «بطاقات الدرجات … وقيمة البند 9» | `intake-dialog.component.ts:35-49`; `StaffIntakeView` has no item-level fields (`core/api/types.ts:31-40`) | Return the instrument items, version, classification and item 9. |
| minor | The «الملخص يُعدّ من الفريق» state (EC-14, generation failed) is missing. Loading is a single bar, not separate skeletons. | states | `intake-dialog.component.ts:29-31` | Add both states. |
| n/a | The rule «اعتماد الملف شرط قبل حفظ أول خطة علاج (S15)» cannot be checked: S15 does not exist. | rules | — | Enforce it when S15 is built. |

**Needed to deliver S02:** the patient-file route and tab shell; a file DTO with AI statements, sources, flags, scores and instrument items; the evidence, dismiss, approve (with ETag and edits), file-feedback, risk-flag and manual-crisis endpoints; and the UI panels listed above.

---

## S03 — Triage review (`/s/triage/:id`, supervisor)

**What exists:**
- **UI:** none. There is no route, no menu entry (`triage` is filtered out by `IMPLEMENTED_ROUTES`, `staff-role.ts:37`), and no component.
- **API:** two endpoints (`StaffEndpoints.cs:64-66`, supervisor and lead).
  - `GET /v1/staff/triage` returns up to 200 pending cases, ordered by risk then due time (`:292-298`): `{id, pseudonymId, path, riskLevel, phq9, gad7, cssrsPositive, submittedAt, dueAt}`.
  - `POST /v1/staff/triage/{id}/review` takes `{riskLevel 1–4, focus[]}`. It sets the level, marks the case Reviewed and replaces the match suggestions with up to three directory picks. It is audited (`:301-324`).

| severity | what's missing | design ref | code evidence | suggested fix |
|---|---|---|---|---|
| **blocker** | The S03 page is missing: no side queue, claim bar, level cards, case summary, suggested therapists, decision-reason field, or the five decision buttons. | whole screen; route «/s/triage/:id» | `app.routes.ts:19-23`; `staff-role.ts:19,37` | Build `s/triage` and `s/triage/:id`. |
| **blocker** | Safety: the API lets a supervisor **lower** the risk, even from 3 or 4, with no clinical-lead co-sign, no reason, and no "highest of rules and human" floor. `Review` overwrites `RiskLevel` with any value from 1 to 4. | rules «المستوى النهائي هو الأعلى بين القواعد والقرار البشري»; «خفض المستوى من 3 أو 4 يحتاج توقيعًا مشتركًا من المسؤول السريري (EC-48)»; SB-33 | `StaffEndpoints.cs:303-310`; `TriageCase.cs:86-97` | Reject lowering without a co-sign and reason. Keep the rule-based floor. |
| **major** | No claim or release: no `POST`/`DELETE /v1/triage/cases/{id}/claim`, no 15-minute lock, no "claimed by another" state, and decisions are not tied to a claim. | SB-30/31; rule «لا قرار دون حجز فعّال للحالة»; EC-49 | Absent | Add the claim entity (server time, 15 min) and check it on decisions. |
| **major** | No case detail (`GET /v1/triage/cases/{id}`): no summary, AI suggestion, safety indicators, language or preferences, and no read-only link to the full file and chat. | elements «بطاقات المستوى», «ملخص الحالة»; button «الملف الكامل والمحادثة» | Only the list DTO (`StaffEndpoints.cs:43`) | Add an audited detail endpoint. |
| **major** | No decision endpoint with the kinds Approve, OverrideRisk, Reassign, Escalate (opens a crisis event and routes to `/s/crisis/:eventId`) and Close-with-referral. Approve and set-level are merged into `review`. There is no reason field (≤1000 characters, required for anything but Approve), and no reviewer-chosen therapist list. | SB-32…36; field «سبب القرار» | `StaffEndpoints.cs:301-324` | Add `POST /v1/triage/cases/{id}/decisions` with a kind, a reason and suggestions. |
| **major** | The SLA is a fixed 24 h for every case. | rule «4 ساعات للمستوى 2 و24 ساعة للمستوى 1 (SlaPolicies)» | `TriageCase.cs:18,31` | Take the due time from the level. |
| **major** | Missing data: suggested therapists come with no capacity or next slot, and there is no filter to approved therapists with free capacity. The case source ("طلب التحدث مع شخص") is not in `path`. There is no "no therapist available" state. | elements «الأخصائيون المقترحون», «قائمة الفرز الجانبية»; states | `StaffEndpoints.cs:312` (`directory.SuggestAsync`); `TriageCase.Path` = intake or direct | Extend the DTO and the directory query. |

**Needed to deliver S03:** the queue and detail UI; claim and release; a detail endpoint; a decision endpoint with reason, co-sign and escalation; and the level-based SLA. The current `review` endpoint must also stop allowing unguarded lowering.

---

## S04 — Crisis console (`/s/crisis/:eventId` → built as `/crisis`, no event param)

✅ Matches
- The page exists. Supervisors, the lead and on-call therapists reach it from the menu; therapists reach it from a dashboard alert (`auth.guard.ts:28-33`).
- Health bar: gate, classifier and calls/messages, plus on-call and backup names, polled every 30 s (`crisis-console.component.html:5-21`). The hub-disconnected chip is shown (`:18-20`).
- Open events list: count, CE number, level, pseudonym, source, countdown or "acknowledged", owner. Sorted unacknowledged first, then by deadline. Empty state «لا توجد أحداث مفتوحة» (`html:24-57`).
- Event header: number, level and label, rule IDs, rule-pack version, start time to the second (`html:63-74`).
- Acknowledgement countdown from the server deadline. It turns red under 60 s and is announced once through a polite live region (`ts:20,110-112,329-338`).
- `إقرار وتولي الحالة`: the first wins; a 409 is shown (`ts:128-137`; worker `ConsoleEndpoints.cs:128-138`).
- `إظهار البيانات (تحقق إضافي)`: TOTP plus a reason, then `reveal-contact`. It is audited, shown for 10 minutes and auto-hidden. Before the reveal, only the last two digits are visible (`ts:153-165,340-348`; `ConsoleEndpoints.cs:155-178,272-273`).
- The patient's words appear only after step-up, are audited and hidden after 10 minutes (stricter than the design).
- `إضافة ملاحظة` requires text. The server limits it to 2000 characters (`ConsoleEndpoints.cs:201-205`).
- `إجراء الطوارئ المحلي` is a confirm dialog with a required note.
- `إغلاق مع النتيجة` offers four outcomes plus a required reason. It is shown only to supervisor or lead (`canClose`) and enforced by the server (`CrisisEvent.cs:205-226`). Closing releases the chat lock.
- Timeline to the second: opened, pages, acknowledgement, actions, reveals, close.

| severity | what's missing | design ref | code evidence | suggested fix |
|---|---|---|---|---|
| **blocker** | Broken deep link from paging. The worker sends `ConsoleUrl = {base}/crisis/{eventId}`, but there is no `crisis/:eventId` route, so the `**` redirect sends the responder to their home page. Supervisors land on whichever event sorts first. On-call therapists land on `/dashboard`. Selecting an event never updates the URL. | route «/s/crisis/:eventId»; entry «من رسالة أو اتصال التنبيه … أو من «فتح الحدث» في S20»; button «حدث في القائمة → /s/crisis/:eventId» | `backend/src/Tamaneen.Crisis.Worker/Paging/PageDispatcher.cs:46`; `web-staff/src/app/app.routes.ts:21,23`; `crisis-console.component.ts:118-126` | Add a `crisis/:eventId` route, select from the param, and update the URL on select. The S01 link (`dashboard.component.html:37`) should pass the event ID too. |
| **major** | `الاتصال بالمريض` is only a `tel:` link. It does not log a `CallAttempt` and does not ask for the call outcome. The worker has no `call_attempt` kind, so the 3-attempts-in-15-minutes rule (EC-33) cannot work. | SB-39 «يفتح رابط tel: ويسجّل محاولة اتصال، ثم يسأل عن نتيجة المكالمة»; rule EC-33 | `crisis-console.component.html:130-135`; `Tamaneen.Crisis/CrisisAction.cs:11` | Add a `call_attempt` action. Post it on click, then prompt for the outcome. |
| **major** | `تم التواصل` takes no note, and the event does not move to InContact. Statuses are only Open, Acknowledged and Closed. The mockup shows «قيد التواصل». | SB-40 «يسجّل التواصل مع ملاحظة قصيرة وينقل الحدث إلى InContact» | `crisis-console.component.ts:167-169`; `ConsoleEndpoints.cs:207-213` (only handover changes state) | Add a note dialog and an InContact status, and use the contact deadline. |
| **major** | `تسليم لمناوب آخر` has no picker for an available colleague and no acceptance step. It drops ownership at once and re-pages the queue. | SB-43 «يختار مناوبًا متاحًا مع ملاحظة؛ لا تنتقل الملكية حتى يقبل المستلم» | `crisis-console.component.ts:181-185`; `CrisisEvent.cs:94-106` | Add `POST …/handover {toUserId, note}` with an accept flow. |
| **major** | `إجراء الطوارئ المحلي` shows no local protocol script or emergency number. It does not require either a visible location or a note explaining why there is none (EC-34). | SB-41 «يعرض سكربت البروتوكول المحلي والرقم … يتطلب موقعًا ظاهرًا أو ملاحظة لسبب غيابه» | `crisis-console.component.ts:171-175`; `assets/i18n/ar.json` `crisis.emergencyDialog.body` | Show the rule-pack script and the configured number, and require location or a reason. |
| **major** | Close: `ClosedUnreachable` does not require a clinical-lead co-sign, there is no 72-hour review task, and nothing routes on to S34 or S40. | SB-44 «ClosedUnreachable يحتاج توقيع المسؤول السريري … وينشئ مهمة مراجعة 72 ساعة … → S34 / S40» | `CrisisEvent.cs:108-117,205-226`; `crisis-dialogs.ts:145` | Add the co-sign for `unreachable_referred` and a follow-up task. |
| **major** | When someone else owns the event, the action buttons stay enabled for non-owners. The server does not check the owner on actions. | state «أُقِرّ من غيرك: «أُقِرّ مسبقًا من [الدور]» والأزرار للقراءة (EC-35)»; rule «مالك واحد للحدث» | `crisis-console.component.html:129-142`; `ConsoleEndpoints.cs:180-221` | Disable actions unless the user owns the event, and enforce this on the server. |
| minor | The timeline does not show the next scheduled escalation. The mockup has «10:33:05 إن لم يُقَرّ: اتصال بالاحتياطي…». | element «التسلسل الزمني … والتصعيد المجدول القادم» | `ConsoleEndpoints.cs:288-305` | Append a future entry from `AckDeadlineUtc` and the next tier. |
| minor | A gate or classifier outage appears only as a chip, not as a red page-top bar with the outage start time. Calls and messages are merged into one chip. The gate is always "ok". | state «تعطل بوابة أو مصنّف: شريط أحمر في رأس الصفحة مع وقت بدء العطل» | `crisis-console.component.html:5-17`; `ConsoleEndpoints.cs:262-264` | Add a banner and a `since` field. |
| minor | The note textarea has no client-side 2000-character limit or counter (the server enforces it). | SB-42 «حتى 2000 حرف» | `crisis-dialogs.ts:116,133` | Add `maxlength="2000"`, `Validators.maxLength` and a counter. |
| minor | The 409 message does not name the acknowledger's role, which the design asks for. | EC-35 | `CrisisResource.ar.resx:24` | Include the role. |
| minor | Undocumented route: `/crisis` instead of `/s/crisis`. | route | `app.routes.ts:21` | Rename or document. |

---

## S06 — Staff sign-in (`/staff/login`, `/staff/login/mfa`, `/staff/recover`, `/staff/forbidden`)

✅ Matches
- `/login` exists, with a guard that sends signed-in users home (`app.routes.ts:10`).
- Work email: required, email format, `dir=ltr`. Password: required (`login.component.ts:71-74`, `html:162-177`).
- `متابعة` → `POST /v1/auth/login` always leads to the MFA step. Errors use the server's generic text, with remaining attempts and lockout minutes (`ts:77-99`, `html:216-232`).
- MFA code: 6 digits only, `inputmode=numeric`, `autocomplete=one-time-code` (`html:188`). `تأكيد` → `/v1/auth/login/mfa`, then the role's home or a safe `returnUrl` (`ts:101-120`). A wrong code clears the field and refocuses it.
- MFA is mandatory and cannot be skipped. Recovery-code fallback is offered as an extra.
- While submitting, the button is disabled and a progress bar shows (prevents double submit).
- Session expiry returns to sign-in with the requested path saved (`auth.guard.ts:7-13`).
- Passkey (`استخدام مفتاح المرور`) is out of scope and not counted.

| severity | what's missing | design ref | code evidence | suggested fix |
|---|---|---|---|---|
| **major** | There is no `/staff/recover` page and no `نسيت كلمة المرور؟` link. The page should take the work email, send a reset link (with the same answer whether or not the account exists), and note that resetting MFA needs two admins. `POST /v1/auth/recover` already exists in the API (`AuthEndpoints.cs:66-77`). | S06 elements «استرجاع الحساب /staff/recover»; buttons «نسيت كلمة المرور؟», «إرسال رابط إعادة التعيين» | `login.component.html:159-180` (no link); `app.routes.ts` (no route) | Add the recover route and form, and confirm the API's staff behaviour. |
| **major** | There is no `/staff/forbidden` page. Denied routes silently redirect home (`crisisAccessGuard`, `therapistGuard`, `**`), the attempt is not logged, and there is no «العودة للوحتي» link. | element «منع الوصول /staff/forbidden … إشعار بأن المحاولة سُجّلت، ورابط العودة»; state «منع الوصول: الصفحة الرابعة، والمحاولة مسجّلة» | `auth.guard.ts:32,38`; `app.routes.ts:23` | Add a forbidden route. Have the guards redirect there and post an audit event. |
| **major** | No CAPTCHA after 3 failures. Nothing in the backend or portal implements it; the lockout after 5 failures does exist. | state «قفل الحساب: بعد 5 محاولات خلال 15 دقيقة، و CAPTCHA بعد 3 (EC-39)» | No `captcha` anywhere in `backend/src` or `web-staff/src` | Add a CAPTCHA challenge flag from the API after 3 failures. |
| **major** | The email field has no 254-character limit. | «بريد بصيغة صحيحة، حتى 254 حرفًا، dir=ltr» | `login.component.ts:72`; `html:164` | Add `Validators.maxLength(254)` and `maxlength`. |
| minor | No `الدخول بحساب المؤسسة` (OIDC) button. The guide allows password or OIDC («OIDC or ASP.NET Identity»), so this is a design difference, not a requirement. | button «الدخول بحساب المؤسسة» | `login.component.html:159-180` | Add it if an OIDC provider is adopted, or remove it from the design. |
| minor | No `قدّم طلب الانضمام` link. Its target, S07 `/staff/apply`, is not built. | «أخصائي جديد؟ قدّم طلب الانضمام» | `login.component.html` | Add it when S07 exists. |
| minor | Undocumented routes: `/login` instead of `/staff/login`. MFA is an in-page step rather than its own `/staff/login/mfa` route. | route list | `app.routes.ts:10`; `login.component.ts:17,60` | Rename or document. |
| minor | The access token lasts 15 minutes; the design says 10. | «access 10 دقائق، refresh 8 ساعات» | `README.md` (phase 4: "Access tokens last 15 minutes") | Align the token lifetime with the design, or record the change. |

---

## Summary

| severity | S01 | S02 | S03 | S04 | S06 | total |
|---|---|---|---|---|---|---|
| blocker | 1 | 3 | 2 | 1 | 0 | **7** |
| major | 4 | 6 | 5 | 6 | 4 | **25** |
| minor | 8 | 1 | 0 | 5 | 4 | **18** |

**Blockers**
1. S01: if the first dashboard load fails, crisis alerts (including live pushes) are hidden (`dashboard.component.html:14-16`, `dashboard.component.ts:140-142`).
2. S02: no page or route `/t/patients/:id/intake`. Only a reduced modal exists.
3. S02: no manual crisis (`POST /v1/crisis/manual`) in UI or API.
4. S02: no raise-risk action (`POST …/risk-flag`) in UI or API.
5. S03: no page or route `/s/triage/:id`.
6. S03: the `POST /v1/staff/triage/{id}/review` API lets a supervisor lower risk, even from 3 or 4, with no co-sign, no reason and no rule floor (`StaffEndpoints.cs:303-310`).
7. S04: the paging deep link `/crisis/{eventId}` matches no route and falls through to the home page (`PageDispatcher.cs:46`, `app.routes.ts:21-23`).

**Majors**
- **S01:** refer-to-on-call fails on open alerts (handover requires Acknowledged) and has no note dialog or navigation. No SignalR reconnect strip or 5 s polling. `مراجعة الملف` and `الملف` open a modal instead of S02 and S14.
- **S02:** no AI statements with evidence, flags or hypotheses. No transcript or evidence endpoint. No edit or dismiss. No If-Match/412 and no approver details. No usefulness rating. No item-level answers or PHQ-9 item 9.
- **S03:** no claim or release. No case detail. No decision endpoint (Approve, Override, Reassign, Escalate, Close) and no reason field. Fixed 24 h SLA. No capacity, next slot or source data.
- **S04:** Call does not log `CallAttempt` or ask for the outcome. Contacted takes no note and does not move to InContact. Handover has no picker and no acceptance. The local emergency step has no script or number and no location rule. Close has no co-sign for unreachable and no 72 h task. Non-owners can still act.
- **S06:** no recover page or link. No forbidden page and no logging of denied attempts. No CAPTCHA after 3 failures. No 254-character email limit.
