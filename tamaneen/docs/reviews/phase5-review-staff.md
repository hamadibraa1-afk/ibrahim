# R5S: Staff portal phase 5 conformance (S14, S15, S16, S18, S19, S20, S21, S32, S33, S35, S41, S57 and the therapist session page)

**Scope**
- Spec: `g3.json` (S14–S35) and `g4.json` (S41, S57).
- Mockups: `mockups/project/S*.dc.html`.
- Code, on branch `claude/brave-bardeen-3nott4` at 7160ff0: `web-staff/src/app` and `backend/src/Tamaneen.Api`.
- Paths below are relative to `/home/user/ibrahim/tamaneen`. Paths starting `features/…` are under `web-staff/src/app/`, and backend paths starting `Endpoints/…` are under `backend/src/Tamaneen.Api/`.

**Route convention**
- The portal drops the spec's `/t/` and `/staff/` prefixes. For example, `/t/patients/:id` is built as `/patients/:id` and `/staff/settings` as `/settings` (`web-staff/src/app/app.routes.ts:36-57`).
- The phase 5 brief prescribes this ("The staff portal uses plain routes like /patients/:id, not /t/..."), so it is not counted as a gap.
- One wrinkle: nothing in the repo records it. `docs/frontend-patient.md` covers the patient app only.

**Screens excluded (later phases)**
- Links whose targets are later-phase screens are not counted. The phase is from the gap plan's `gap/data.py`:
  - phase 8: S17 Reports, S34 Risk assessment, S37 Measures, S56, S66, S69, S75;
  - phase 6: S09 Licence;
  - out of scope: messages and consults.
- This covers the «التقارير» tab, «فتح التقرير», «التقرير الكامل», «طلب مقياس», «تقييم خطر», «تحديث الترخيص» and the weekly-report and consult notifications.

**RLS baseline, verified once for every screen below**
- The relevant definitions:
  - `fn_patient_access` (`backend/src/Tamaneen.Infrastructure/Persistence/Migrations/20261020044608_SecurityAndLedger.cs:38-52`) gives a therapist only patients with an active `CareAssignment`.
  - `fn_clinician_only_access` (`…/20261025222102_Phase5ClinicalCareRecords.cs:357-368`) covers `SessionNotes` and `SessionNoteAddenda`. It allows supervisors, the lead, and an assigned therapist. **It never allows the Patient role.**
  - `StaffEndpoints.PatientIdAsync` (`Endpoints/StaffEndpoints.cs:294-310`) returns 404 for any patient the caller cannot see (EC-42).
  - `CareRecords.RequireTherapist` (`backend/src/Tamaneen.Api/Clinical/CareRecords.cs:67-73`) limits every care write to the Therapist role.
- What this means:
  - Supervisors and the lead are read-only on S14–S16, S33 and S35. They act only on the S57 sign-off (`CrisisCloserPolicy`, `backend/src/Tamaneen.Hosting/AuthSetup.cs:88`). This matches guide §3.4: supervisors see everything in the pilot, and every read is audited.
  - No patient endpoint reads `SessionNotes`. The only note-derived data a patient sees is the shared `SessionSummaries` points (`Endpoints/StaffCareEndpoints.Notes.cs:141-147`). **The patient never sees session notes.**

---

## S14 — Patient overview (`/patients/:id`)

**✅ Matches**
- **Page and guard:** the route exists (`app.routes.ts:37`, `clinicalGuard`). A patient who is not assigned gets the «غير موجود» state (`features/care/patient-overview.component.html:4-10`). Every opening is audited as `patient.overview_opened` (`Endpoints/StaffCareEndpoints.cs:139`).
- **Header:** pseudonym, risk tag and weeks in care (`html:17-21`).
- **Header buttons:**
  - «رفع مستوى الخطر»: the dialog offers only levels above the current one, and a reason is required. It posts to `risk-flag` (`features/care/patient-overview.component.ts:252-275`). Lowering the level is not possible.
  - «إطلاق تنبيه طوارئ»: an explicit confirmation dialog with a level (3 or 4) and a note, then it opens the console (`ts:277-300`).
  - «إنهاء الرعاية» links to S57 (`html:29`).
  - The buttons are hidden for supervisors, the lead and ended care (`canEdit`, `Endpoints/StaffCareEndpoints.cs:159`).
- **Tabs and breadcrumb:** «مرضاي» breadcrumb, then overview, clinical file, plan, homework and safety-plan tabs (`features/care/patient-tabs.component.ts:306-344`).
- **Plan goals card:** goals of the latest version, each with improving / ongoing / not-started status. Empty state «لا خطة بعد» with a link to write the plan (`html:96-111`).
- **Homework card:** this week's done / target and the average feeling (`html:113-128`).
- **Next session card:** time and session number. Empty state «لا جلسة قادمة» (`html:68-77`).
- **Read-only banner** when care has ended or a closure is pending (`html:35-40`).

**Gaps**

| severity | what's missing | design ref | code evidence | suggested fix |
|---|---|---|---|---|
| minor | The PHQ-9 chart over time is replaced by a mood sparkline. PHQ-9 appears only as the intake score. There is a screen-reader table, but it is for mood. The platform has no repeated PHQ-9 data until S37 (phase 8), so this is a data dependency. | «رسم PHQ-9 عبر الوقت: نقاط كل أسبوعين من البدء لليوم، مع جدول بديل نصي»; mockup «PHQ-9 عبر الوقت · 14 12 11 9 8 · كل أسبوعين» | `html:43-66`; `Endpoints/StaffCareEndpoints.cs:115-117` (MoodEntries only) | Keep the mood chart and label it as such. Add the PHQ-9 series when S37 lands. |
| minor | «إنهاء الرعاية» is always enabled, even when closure is blocked (risk 3–4 within 14 days). S57 does block submission, so this is a UX gap only. | «محظور إن كان الخطر 3–4 خلال 14 يومًا. (SB-27)» | `html:29` | Disable it or add a hint when `ClosureBlockedAsync` is true (expose a `closureBlocked` flag on the overview). |
| minor | The "no upcoming session" state has no booking link. | «لا جلسة قادمة: رسالة مع رابط الحجز» | `html:75-77` | Add the link when therapist-side booking exists (see S33 «حجز الجلسة القادمة»). |
| minor | Loading shows one progress bar, not a skeleton per card. | «تحميل: هيكل لكل بطاقة» | `html:1-3` | Add card skeletons. |

---

## S15 — Treatment plan (`/patients/:id/plan`)

**✅ Matches**
- **Version header:** «الإصدار N · مسودة» with the note that the therapist writes it (`features/care/treatment-plan.component.html:31-32`).
- **Fields and validation:**
  - Goals: one per line, each with a status; required, ≤ 4000.
  - Interventions: required, ≤ 4000.
  - Frequency: weekly, biweekly or monthly.
  - Review date: in the future, with `min` set and the server checking `reviewOn > today`.
  - Follow-up notes: optional, ≤ 4000.
  - Code: `html:33-74`, `features/care/treatment-plan.component.ts:232-235`, `Endpoints/StaffCareEndpoints.Plans.cs:76-110`.
- **«حفظ كإصدار جديد»:**
  - It sends If-Match. A 412 shows the newer version beside the draft (`ts:251-271`; EC-50).
  - It is disabled until the clinical file is approved, with a link to S02 (`html:13-18`, `html:79`). The server also refuses with `PlanFileNotApproved` (`Endpoints/StaffCareEndpoints.Plans.cs:71-74`).
- **«تجاهل المسودة»:** asks for confirmation, then reloads the latest version (`ts:275-282`).
- **Earlier versions:** each opens read-only with «عرض», and a note says versions are never deleted (`html:84-111`).
- **Encryption and audit:** content is encrypted with the patient key (`Plans.cs:112`). Reads and saves are audited (`Plans.cs:42,59,115`).

**Gaps**

| severity | what's missing | design ref | code evidence | suggested fix |
|---|---|---|---|---|
| major | Drafts are not kept locally and are not resent when the connection drops. A failed save shows an error, and the draft lives only in memory. | «فقد الاتصال: المسودة محفوظة محليًا ويُعاد الحفظ» | `ts:258-270` | Keep the draft in the session store (or IndexedDB, encrypted) and retry on `online`. |
| minor | The read-only view of an old version omits frequency, review date and follow-up notes. | SB-20 «يفتح إصدارًا سابقًا للقراءة فقط» | `html:101-111` | Render all the fields. |

---

## S16 — Patient homework (`/patients/:id/homework`)

**✅ Matches**
- **Current homework list:** title, frequency, end date, done this week / target, average feeling and the patient's latest note (`features/care/patient-homework.component.html:17-73`). The empty state reads «لا واجبات حالية».
- **«إرسال تعليق»:** write, preview, then send (`html:44-59`). The reply is ≤ 1000 characters and the server notifies the patient (`Endpoints/StaffCareEndpoints.Homework.cs:147-166`).
- **«تعديل»:** prefills the form and sends a PUT (`features/care/patient-homework.component.ts:256-260`, `ts:287`).
- **«إلغاء»:** asks for confirmation, then sends a DELETE, and the patient is notified (`ts:307-314`; `Homework.cs:134-144`).
- **Library:** behavioural activation, thought record, sleep diary, breathing, plus «نص خاص» (`html:87-95`).
- **Form fields:** title (required, ≤ 120), instructions (required, ≤ 2000), frequency and end date. The server validates as well (`Homework.cs:222-237`).
- **«معاينة كما يراها المريض»** (`html:119-131`) and **«إسناد الواجب»**, which notifies the patient (`Homework.cs:102-106`). A failed assignment keeps the form (`ts:299-303`).
- **Risk notes:** a note the crisis gate rated 3–4 gets a risk badge (`html:33-36`).

**Gaps**

| severity | what's missing | design ref | code evidence | suggested fix |
|---|---|---|---|---|
| minor | The risk badge links to the generic `/crisis` list, not to the event. | «ملاحظة مريض فيها خطر: تظهر مع شارة تنبيه ورابط للحدث» | `html:35` | Return the crisis event id with the check-in and link to `/crisis/{eventId}`. |
| minor | The end date may be today (client `min` = today; server `EndsOn < today`). | «ينتهي في: إلزامي، تاريخ في المستقبل» | `ts:212`; `Homework.cs:83` | Use `localDate(1)` and `<=`, or accept today and adjust the spec. |
| minor | A reply that fails to send shows its error in the "new homework" form, not next to the reply. | — | `ts:336-339` | Use a separate error signal for the reply. |

---

## S18 — Schedule (`/schedule`)

**✅ Matches**
- **Week header:** «السابق / اليوم / التالي», with aria-labels «الأسبوع السابق / التالي» (`features/schedule/schedule.component.html:9-11`).
- **Grid:** days are columns and hours are rows. Each session shows the pseudonym, the session number and its badge (high risk, file pending, record outcome, late, cancelled) (`html:27-46`; `features/schedule/schedule.component.ts:103-111`).
- **States:** loading skeleton and «لا جلسات هذا الأسبوع» (`html:19-26`). The time zone is shown and comes from the server (`html:14`).
- **Selected-session panel:** shows its state, including «انتهت الجلسة. سجّل النتيجة» (`html:49-75`).
- **«تمت الجلسة» and «لم يحضر»:** disabled until the server allows them (`canRecordOutcome`).
- **No-show follow-up:** a second missed session in a row, or one at risk 3–4, notifies supervisors (`Endpoints/StaffEndpoints.cs:244-252`; EC-53).
- **«إلغاء»:** a reason from the list is required (the server checks it, `Endpoints/StaffScheduleEndpoints.cs:110-115`). The patient is notified and supervisors are asked to rebook (`backend/src/Tamaneen.Api/Clinical/AppointmentNotices.cs:26-30`; EC-52).
- **Errors:** a failed action shows the error and refreshes (EC-51, `ts:326-331`).
- **Authorization:** therapists only (`Endpoints/StaffScheduleEndpoints.cs:52`), own appointments only (`Endpoints/StaffScheduleEndpoints.cs:70,138-140`).

**Gaps**

| severity | what's missing | design ref | code evidence | suggested fix |
|---|---|---|---|---|
| **blocker** | «تمت الجلسة» does not open S33. It shows a snack bar and reloads. The schedule has no link to session notes at all, so both documented entry points to S33 are missing. S33 is reachable only from the S14 session list. | «تمت الجلسة … POST /v1/appointments/{id}/outcome (Completed) → S33»; S33 entry «بعد «تمت الجلسة» في S18، أو من الجدول على موعد منتهٍ»; next [S33] | `ts:266-268`, `ts:317-333`; `html:59-74` (no notes link) | After a successful `attended`, navigate to `/patients/{pseudonymId}/sessions/{id}/notes`. Add a «ملاحظات الجلسة» button to the panel for completed sessions. |
| major | «إعادة جدولة» is missing. There is no button and no staff reschedule endpoint; only the patient's `POST /v1/appointments/{id}/reschedule` exists. | «إعادة جدولة: يختار موعدًا جديدًا مع سبب إلزامي؛ المريض يُبلغ. (SB-26) → POST /v1/appointments/{id}/reschedule»; mockup button «إعادة جدولة» | `html:59-74`; `Endpoints/StaffScheduleEndpoints.cs:52-57` | Add `POST /v1/staff/appointments/{id}/reschedule` { slotId, reasonCode } and a dialog that picks one of the therapist's free slots. |
| minor | Outcome buttons enable 10 minutes before the start (`JoinOpensBefore`), not at the start. | «معطل قبل وقت البدء»; rule «تسجيل النتيجة متاح بعد وقت البدء فقط» | `Endpoints/StaffScheduleEndpoints.cs:185`; `backend/src/Tamaneen.Clinical/Scheduling/Appointment.cs:210` | Use `now >= StartsAtUtc` for the outcome; keep the 10-minute window for joining only. |

---

## Therapist session page (`/sessions/:appointmentId`)

No spec object. Checked against docs/api-v1.md (sessions table) and the P41/P42 counterparts.

**✅ Matches**
- **Page and entry:** the route exists (`app.routes.ts:45`, `therapistGuard`). It is reached from «بدء الجلسة» in the S18 panel during the join window (`features/schedule/schedule.component.html:61-63`).
- **Lobby:** patient presence, pre-session note, local device check, and «انضمام» enabled only inside the window from server time (EC-09) (`features/session/session.component.ts:75-99`, `ts:185-188`).
- **Live:** dev test room (expected), mute / camera / leave (`ts:100-128`). Heartbeat and token renewal (`ts:290-308`).
- **Crisis flag:** an open crisis event for the patient is flagged in a `role=alert` banner (`ts:50-54`).
- **Backend:** only the session's own therapist; everyone else, supervisors included, gets 404 (`Endpoints/SessionEndpoints.cs:259-262`). Opening is audited as `session.opened` (`SessionEndpoints.cs:228`).

**Gaps**

| severity | what's missing | design ref | code evidence | suggested fix |
|---|---|---|---|---|
| minor | After «مغادرة» there is no way on to the session note (S33), although S33's entry is "after the session". | S33 entry «بعد «تمت الجلسة»…» | `session.component.ts:280-288` | After leaving, show «تسجيل النتيجة» and «ملاحظات الجلسة» links. |
| minor | The crisis banner links to `/crisis`, not to the event. The back link always goes to `/dashboard`, even when the page was opened from the schedule. | — | `session.component.ts:45,52` | Return the open event id in `SessionState`; use `history.back()` or a `from` parameter. |
| minor | The risk tag uses the highest level of any crisis event ever, and the newest triage case of any path, change requests included. | — | `SessionEndpoints.cs:223-225` | Reuse `CareRecords.RiskAsync` (clinical triage plus open or recent crisis). |

---

## S19 — Availability (`/availability`)

**✅ Matches**
- **Usual week:** each day has a switch, time periods (end after start, no overlap, error shown next to the day, save blocked) and «إضافة فترة» (`features/availability/availability.component.html:17-41`; `features/availability/availability.component.ts:26-31`).
- **Session settings:** length and gap from server options (`html:42-55`).
- **Exceptions:** leave or absence with from / to and type, «حذف» before saving, «إضافة استثناء» (`html:60-99`).
- **Booked sessions affected:** a warning with the list, and save becomes «حفظ على أي حال» (`html:101-116`; `ts:259-283`).
- **Save:** «حفظ وتحديث المواعيد لـ 8 أسابيع» (`html:114-116`). A failed save keeps the edits (`ts:298-301`). Booked slots stay locked (`html:135-137`).
- **Licence:** an unverified licence pauses new times (`html:11-13`).
- **Loading skeleton** (`html:5-9`).
- **Deferrals:** prayer times and holidays are deferred to S69/P60, documented in `docs/api-v1.md:268-270`.

**Gaps**

| severity | what's missing | design ref | code evidence | suggested fix |
|---|---|---|---|---|
| minor | No «رابط أداة الاجتماعات» field (required https link). In-app sessions replace the external tool: `docs/api-v1.md:91` says "the apps now use the lobby", and the room comes from `IVideoSessionProvider`. | element «رابط الجلسات»; field «رابط https صالح، إلزامي» | `html:42-56` (no field) | Treat as superseded and record the decision in docs; otherwise add the field with https validation. |

---

## S20 — Alerts (`/alerts`)

**✅ Matches**
- **Filters:** open (with count), handled, all (`features/alerts/alerts.component.ts:44-48`).
- **Open card:** level, pseudonym, rule IDs, source, time, and «المناوب: لم يُقِرّ بعد» or the acknowledger's name (`ts:57-75`).
- **«إقرار»:** posts to the worker's acknowledge (`ts:160-174`). A 409 shows who already acknowledged and refreshes.
- **«إحالة للمناوب مع ملاحظة»:** the note is required (≤ 1000) and sent as `kind: refer`. The worker allows it on Open events for the patient's therapist (`ts:176-198`; `backend/src/Tamaneen.Crisis.Worker/Endpoints/ConsoleEndpoints.cs:234-246`).
- **«فتح الحدث»** opens `/crisis/:id` (`ts:73`), which the matcher route supports.
- **Polling and errors:** refresh every 5 s; a failed refresh keeps the list and offers a retry (`ts:20-21`, `ts:50-52`, `ts:149-152`). The empty state reads «لا تنبيهات مفتوحة الآن» (`ts:76-78`).
- **Backend:** a therapist sees only patients with an active assignment (on top of RLS), and reads are audited (`Endpoints/AlertsEndpoints.cs:49-54`, `AlertsEndpoints.cs:68`). There is no mute anywhere.

**Gaps**

| severity | what's missing | design ref | code evidence | suggested fix |
|---|---|---|---|---|
| major | The 30-day history shows closed crisis events only. Risk raises (risk-flag) and no-show alerts are missing, and each row lacks a type and details. | «سجل آخر 30 يومًا: كل تنبيه بنوعه (رفع مستوى، لا حضور، إنذار خاطئ)، التفاصيل، التاريخ، والحالة النهائية»; mockup rows «رفع مستوى … رفعه المشرف إلى المستوى 2», «لا حضور … غياب مرتين متتاليتين» | `Endpoints/AlertsEndpoints.cs:48` (CrisisEvents only); `ts:86-92` | Merge in risk-flag audit rows and `appointment.no_show_followup` events as typed history items (kind, detail, final state). |
| minor | Only the rule IDs are shown, not the rule's description. | mockup «قاعدة CR-4 (أفكار انتحارية دون خطة)» | `ts:62` | Return the rule-pack label with each rule ID. |
| minor | `/hubs/crisis` is not used here, so updates rely on 5-second polling only. This is acceptable for the "disconnected" state. | api «/hubs/crisis» | `ts:130-134` | Optionally subscribe to `CrisisHubService` for instant updates. |

---

## S21 — Professional profile (`/profile`)

**✅ Matches**
- **Display name:** Arabic and English, required, ≤ 60 (`features/profile/profile.component.ts:150-151`).
- **Capacity:** shown with the current caseload. The client blocks values below it and the server refuses them (`error.profile.capacity_below_caseload`) (`ts:71-78`, `ts:193`; `Endpoints/StaffProfileEndpoints.cs:110-130`).
- **Specialties** from the catalog. **Bio** ≤ 600, Arabic and English. No contact details, checked by a server regex (`Endpoints/StaffProfileEndpoints.cs:121-126`, `StaffProfileEndpoints.cs:170`).
- **«أستقبل مرضى جددًا»** switch and **«حفظ»**. Therapist only (`Endpoints/StaffProfileEndpoints.cs:59`).

**Gaps**

| severity | what's missing | design ref | code evidence | suggested fix |
|---|---|---|---|---|
| major | The licence card shows only verified / unverified. The expiry date, who verified it and when, and the 30-day expiry warning are missing. The data model has no licence expiry at all (no `LicenseExpires…` anywhere in the backend). | «بطاقة الترخيص: الحالة (موثّق)، تاريخ الانتهاء، من وثّقه ومتى، وتنبيه قبل 30 يومًا»; state «ترخيص قريب الانتهاء: شريط تحذير قبل 30 يومًا»; mockup «ينتهي في 14 مارس 2028 · وثّقه فريق التحقق · 19 أكتوبر» | `ts:117-121`; `web-staff/src/app/core/api/types.ts:320-337` | Add `licenseExpiresOn`, `licenseVerifiedBy` and `licenseVerifiedAt` to the profile (from S05 verification) and show the warning banner within 30 days. |
| minor | Specialties appear as a full checkbox list, not as chips with «تعديل». Saving with no language selected silently does nothing. | «شارات من الكتالوج مع رابط تعديل» | `ts:80-91`, `ts:196-199` | Show chips and open the picker on «تعديل». Show an error when no language is chosen. |

---

## S32 — Account settings (`/settings`)

**✅ Matches**
- **Entry:** «الإعدادات» at the bottom of the side menu (`web-staff/src/app/layout/shell.component.html:40-42`).
- **Password change:** current, new and confirm, plus a TOTP step-up and a mismatch check (`features/settings/settings.component.ts:50-60`).
- **«نقل لجهاز آخر»:** password and code step-up, then the new secret. The old device stays valid until a new code confirms (`ts:62-82`). Two-step verification cannot be turned off.
- **Notifications:** risk alerts are locked on (disabled). The three other switches save at once and revert on failure (`ts:119-128`, `ts:241-259`).
- **Language:** switches the direction at once and is saved (`ts:136-141`, `ts:261-264`).
- **Active sessions:** listed, including the S41 «ليس أنت؟» deep link that highlights the session (`ts:105-112`).
- **Passkeys:** out of scope.

**Gaps**

| severity | what's missing | design ref | code evidence | suggested fix |
|---|---|---|---|---|
| major | The 15-minute idle timeout is not enforced. It is only displayed (`IdleTimeoutMinutes = 15`). The portal has no idle logic, and staff sessions last 12 h (`AuthSession.StaffLifetime`). | element «الجلسات النشطة مع مهلة الخمول 15 دقيقة»; rule «مهلة الخمول 15 دقيقة لحسابات الفريق» | `Endpoints/StaffSettingsEndpoints.cs:25`; `backend/src/Tamaneen.Identity/Auth/AuthSession.cs:12`; no idle handling in `web-staff/src/app/core` | Sign out after 15 minutes without user activity (portal timer plus a server-side last-activity check on refresh). |
| major | The time zone cannot be chosen. It is a fixed read-only `Asia/Riyadh`. | «المنطقة الزمنية: اختيار من قائمة المناطق → PUT /v1/me/preferences» | `ts:21`, `ts:134`, `ts:161` | Add a select backed by `PUT /v1/me/preferences` { timeZone } and use it in the date pipes. |
| major | «إنهاء الأخرى» and per-session «إنهاء» act immediately, with no confirmation. | «إنهاء الأخرى: تأكيد ثم إلغاء كل الجلسات عدا الحالية»; S41 «بعد تأكيد» | `ts:102`, `ts:109`, `ts:227-239` | Wrap both actions in `ReasonDialog` confirm mode. |
| minor | An expired TOTP move token (410) shows a generic error and does not restart the step-up. | «انتهاء التحقق الإضافي: يُطلب من جديد» | `ts:219-224`, `ts:273-286` | On 410, clear `move` and reopen the step-up form. |

---

## S33 — Session notes (`/patients/:id/sessions/:appointmentId/notes`)

**✅ Matches**
- **Header:** patient, session number, time, and an autosave indicator with the last save time (`features/care/session-notes.component.html:10-30`).
- **Template choice:** SOAP, DAP or free text, from the server's template map. Switching asks for confirmation when text would move (`features/care/session-notes.component.ts:223-239`).
- **Fields:** ≤ 4000 each, checked on the client and the server (`Endpoints/StaffCareEndpoints.Notes.cs:63`).
- **«حفظ كمسودة»** with autosave every 25 s and If-Match. A 412 shows both versions with keep-mine or use-theirs (`ts:137`, `ts:241-270`; `html:34-46`).
- **«توقيع وقفل الملاحظة»** asks for confirmation. A signed note is read-only and offers «إضافة ملحق» only (`html:50-52`, `html:85-91`).
- **Patient summary:** three short points with «إرسال ملخص قصير للمريض». They are shared only when the note is signed (`html:67-75`; `Notes.cs:103-115`).
- **«آخر أسبوع»** panel (`html:95-102`) and **«إسناد واجب»**, which links to S16 (`html:106`).
- **Authorization:** only the session's own therapist writes, and only from the start (`Notes.cs:160-171`). Encrypted with the patient key. Every read and write is audited. RLS keeps notes from the patient.

**Gaps**

| severity | what's missing | design ref | code evidence | suggested fix |
|---|---|---|---|---|
| major | «ما أراد المريض الحديث عنه» is missing. The patient's pre-session note is not shown, although the API has it (the session page shows it). | element «ما أراد المريض الحديث عنه: اقتباس من تحضير المريض قبل الجلسة»; api «GET /v1/patients/{id}/prep»; mockup «النوم تحسن، لكن ما زلت قلقًا من اجتماع الأحد.» | `html:94-111`; `Notes.cs:35` (no prep in `SessionNoteViewDto`); compare `Endpoints/SessionEndpoints.cs:226-227` | Add `preSessionNote` from `SessionPrep` to `SessionNoteViewDto` (audited) and show it as a quote in the side panel. |
| major | «حجز الجلسة القادمة» is missing. There is no button and no therapist-side booking. | quick action «حجز الجلسة القادمة: يفتح اختيار موعد من أوقات التوفر»; mockup button | `html:103-110` | Add a dialog listing the therapist's free slots and a staff booking endpoint. |
| minor | With unsaved changes, «توقيع» only saves. The therapist must click a second time to sign, and nothing says so. | «تأكيد ثم توقيع» | `ts:277-280` | Save, then continue to the sign confirmation in one flow. |
| minor | A failed save keeps the text in memory only. It is lost if the tab closes. (This is a deliberate privacy choice, stated in the code comment.) | «فشل الحفظ: «لم يُحفظ» والنص محفوظ محليًا» | `ts:139-145` | Confirm with design. Otherwise keep an encrypted local copy until it is saved. |
| minor | The sign confirmation does not show the three points before they are sent, and nothing checks that they contain no diagnosis. | «يُرسل 3 نقاط بلغة بسيطة بعد معاينتها»; «الملخص المرسل للمريض لا يحتوي تشخيصًا» | `ts:281`; `Notes.cs:68-72` | List the points in the confirm dialog. Add a soft warning for diagnostic terms or scores. |
| minor | The page marks the "overview" tab as active, which is misleading. | — | `html:31` | Add no active tab, or add a "sessions" tab. |

---

## S35 — Safety-plan builder (`/patients/:id/safety-plan`)

**✅ Matches**
- **Header:** the patient and the reminder «بكلماته» (`features/care/safety-plan-builder.component.html:10-14`).
- **Steps:** steps 1–6 and «سبب للاستمرار», ≤ 1000 each, with example placeholders for a new plan (`html:47-91`).
- **Step 5:** adds the therapist, the support line and the emergency number automatically. They come from platform settings, not hard-coded values (`html:51-58`; `Endpoints/StaffCareEndpoints.SafetyPlan.cs:95-97`).
- **People to ask for help:** a list with «إضافة شخص» and remove.
- **«معاينة كما يراها المريض»** (`html:95-103`).
- **«حفظ وإرسالها للمريض»:** every save is a new version with If-Match. A 412 against the patient's own edit shows both versions. The patient is notified (`features/care/safety-plan-builder.component.ts:251-279`; `Endpoints/StaffCareEndpoints.SafetyPlan.cs:69-90`).
- **Version:** number, date and author, plus the earlier versions, read-only (`html:108-130`). The guidance card is present (`html:104-107`).
- **Encryption and audit:** encrypted with the patient key; reads are audited.

**Gaps**

| severity | what's missing | design ref | code evidence | suggested fix |
|---|---|---|---|---|
| major | The «إضافة شخص» form has no relationship field, and its phone is required. The design asks for name, relationship and an optional phone. | «إضافة شخص: حوار: الاسم والعلاقة ورقم اختياري» | `html:72-83`; `ts:228-241` (`PHONE` regex required) | Add `relation` (≤ 50) to `PlanContact` on both sides and make the phone optional (validated only when present). Keep P37 in step. |
| minor | The emergency numbers come from `PlatformOptions`, not the `EmergencyContacts` content table. The version list (`/safety-plan/versions`) is not audited, unlike the other reads. | «أرقام الطوارئ من EmergencyContacts وليست ثابتة»; S15/S35 «كل قراءة … مسجل» | `Endpoints/StaffCareEndpoints.SafetyPlan.cs:48-54`, `SafetyPlan.cs:97` | Read from the emergency-contacts source when it exists. Audit the list call. |

---

## S41 — Staff notifications (`/notifications`)

**✅ Matches**
- **Header:** title and «تحديد الكل كمقروء», which leaves unacknowledged risk alerts alone (`features/notifications/notifications.component.ts:37-39`, `ts:160-168`; `Endpoints/NotificationEndpoints.cs:118-119`).
- **Filters:** all (with count), risk, appointments, messages, system (`ts:40-46`).
- **Risk alerts:** come first in the danger colour with «لم يُقَرّ بعد». Opening one goes to `/alerts` without marking it read, and it stays until the event is acknowledged (`ts:54-62`, `ts:141-149`; `NotificationEndpoints.cs:125-151`).
- **«ليس أنت؟ أنهِ الجلسة»** opens `/settings?session=…` with that session highlighted (`ts:64-66`, `ts:151-156`).
- **Empty states and errors:** both empty states; a load error with retry keeps the shown items (`ts:72-74`, `ts:47-49`, `ts:126-130`).
- **Entry:** the bell and the menu (`web-staff/src/app/shared/notification-bell.component.ts:18`).
- **Data scope:** the reader's own rows only (`fn_notification_access`). Risk entries are limited to the therapist's own patients (`NotificationEndpoints.cs:132-138`). Pseudonyms only.

**Gaps**

| severity | what's missing | design ref | code evidence | suggested fix |
|---|---|---|---|---|
| major | The new-booking notification opens the patient overview (`/patients/{id}`), not the clinical file waiting for approval (`/patients/{id}/file`). Arguably "wrong link" under the rubric. | «إشعار حجز جديد: يفتح ملف الاستقبال للاعتماد → /t/patients/:id/intake» | `backend/src/Tamaneen.Api/Notifications/NotificationCatalog.cs:97` | Set `StaffLink: "/patients/{pseudonymId}/file"` for `staff.appointment.booked`. |
| minor | No live update: the page loads once and only the bell polls (60 s). There is no "غير متصل" strip with the last update time, only a static note. | rule «تحديث فوري عبر SignalR»; state «انقطاع الاتصال: شريط «غير متصل» مع آخر تحديث» | `ts:111-113`, `ts:77`; `notification-bell.component.ts:10` | Poll or subscribe while the page is open. Show the offline strip with a timestamp. |
| minor | Loading shows a progress bar, not five skeleton rows. | «تحميل: هيكل لخمسة عناصر» | `ts:34-36` | Add skeleton rows. |

---

## S57 — End of care (`/patients/:id/close`)

**✅ Matches**
- **Entry** from S14 «إنهاء الرعاية».
- **Care summary:** closure reason from a fixed list (required on submit), plus session count and weeks, prefilled from appointments and editable (`features/care/end-of-care.component.html:46-63`; `Endpoints/StaffCareEndpoints.Closure.cs:277-283`).
- **Measures table:** therapist-only (`html:64-70`).
- **Patient summary:** ≤ 1500, required. A warning appears when it contains digits (`html:73-83`; `features/care/end-of-care.component.ts:132`).
- **Relapse-prevention plan:** warning signs and what to do (required, ≤ 300 each), follow-up select, keep-library switch. The safety-plan switch is locked on (`html:85-107`).
- **«حفظ كمسودة»** and **«إرسال للمشرف»:**
  - Submit checks the required fields and the risk rule on the server (409 `ClosureBlocked`) (`Closure.cs:107-147`).
  - It is disabled while blocked, with a banner (`html:19`, `html:31-33`).
- **States:**
  - Pending: read-only, with «سحب».
  - Rejected: shows the reason, and the form is editable again.
  - Signed off: banner (`html:21-42`).
- **Supervisor sign-off:** sign off or send back (reason required). Sign-off:
  - ends the assignment (so RLS cuts the therapist off);
  - cancels the remaining sessions;
  - notifies the patient (`Closure.cs:197-261`).
  - Only supervisors and the lead, and not on a closure they authored (`html:24-27`; `Closure.cs:299`). There is a queue at `/closures`.
- **Encryption:** the text is encrypted with the patient key.

**Gaps**

| severity | what's missing | design ref | code evidence | suggested fix |
|---|---|---|---|---|
| major | The second blocking condition, "the patient asked to continue", is not implemented. Blocking checks only risk 3–4 now or a 3–4 crisis within 14 days, and the banner text does not give the specific reason. | «لا تُغلق الحالة إن كان الفرز 3 أو 4 خلال آخر 14 يومًا أو طلب المريض الاستمرار»; rule «الإغلاق ممنوع في الـ API … أو طلب المريض الاستمرار»; state «شريط بالسبب» | `backend/src/Tamaneen.Api/Clinical/CareRecords.cs:49-60`; `html:31-33` | Record a patient "continue care" request (P53 or a check-in), add it to `ClosureBlockedAsync`, and return a `blockedReason` code (`risk` / `patient_continue`) to show in the banner. |
| minor | Measures show only the intake start values, so the end column is always «—». This depends on S37 measures (phase 8). | «البداية والنهاية لكل مقياس (PHQ-9 16 ← 7)» | `Closure.cs:288` | Fill the end values when S37 lands. |
| minor | The closure reasons are a hard-coded list, not a catalog. Session count and weeks have no client bounds, so negative values can be typed. | «سبب الإغلاق: من كتالوج أسباب الإغلاق» | `ts:129`; `html:55-62` | Read the reasons from `/v1/catalogs/closure_reasons` when the catalogs exist. Add `min="0"` validation. |

---

## Summary

| severity | count |
|---|---|
| blocker | 1 |
| major | 12 |
| minor | 26 |

**Blockers**
1. **S18:** «تمت الجلسة» does not open S33, and the schedule has no link to session notes at all. Both documented entry points to S33 are missing (`features/schedule/schedule.component.ts:266-268`).

**Majors**
1. **S15:** no local draft or retry when the connection drops.
2. **S18:** «إعادة جدولة» is missing in both the UI and the API.
3. **S20:** the 30-day history has no risk-raise or no-show items, and rows lack a type and details.
4. **S21:** the licence card lacks the expiry date, the verifier and date, and the 30-day warning; there is no expiry in the data model.
5. **S32:** the 15-minute idle timeout is not enforced (staff sessions last 12 h).
6. **S32:** the time zone cannot be chosen (fixed `Asia/Riyadh`).
7. **S32:** ending sessions has no confirmation.
8. **S33:** «ما أراد المريض الحديث عنه» (the patient's pre-session note) is missing.
9. **S33:** «حجز الجلسة القادمة» is missing.
10. **S35:** «إضافة شخص» has no relationship field, and its phone is required instead of optional.
11. **S41:** the new-booking notification links to the overview, not the clinical file.
12. **S57:** the "patient asked to continue" blocking rule is missing, and the banner gives no specific reason.


**RLS and authorization: no gaps found.**
- A therapist sees only assigned patients (`fn_patient_access` plus `PatientIdAsync`, which returns 404).
- The patient never sees session notes: `fn_clinician_only_access` excludes the Patient role, and no patient endpoint reads them.
- Supervisors and the lead read everything (guide §3.4), with every read audited, but write only the S57 sign-off.
- Schedule, session-page and profile endpoints are limited to the therapist's own rows.
