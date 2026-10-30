# Conformance review r5p: patient phase 5 screens

Branch `claude/brave-bardeen-3nott4` (head 7160ff0). Read-only review against g1/g2/g3 spec objects, the mockups and the guide.
Paths: `WP` = `web-patient/src`, `APP` = `WP/app/[locale]/(app)`, `BE` = `backend/src/Tamaneen.Api`, `WS` = `web-staff/src/app`.
Documented renames (docs/frontend-patient.md:11-16) are not gaps: `/sessions/[id]/*` → `/appointments/[id]/*`, `/help-now` → `/crisis`, `/help-now/after` → `/crisis/after`, `/dashboard` → `/home`, `/book/[id]` → `/therapists/[id]/book`, `/v1/crisis/help-now` → `/v1/crisis/help`, `/v1/me/notifications` → `/v1/me/inbox`.
Applies to every screen: Help now is in the shared header (`WP/components/crisis/HelpNowButton.tsx`). It is never disabled, it shows the numbers first, then it sends a Critical event and queues the request offline. So it is not listed again under each screen.

---

## P38 After the crisis: `/crisis/after`

✅ The route exists, and home redirects to it while the follow-up is unread (`APP/home/page.tsx:28-31`). The notification `crisis.follow_up` links to it (`BE/Notifications/NotificationCatalog.cs:94`). The page appears only after a clinician closes the event. While an event is still open it shows «نتواصل معك الآن» with the numbers (`APP/crisis/after/page.tsx:47-68`). On a load error it shows retry plus the numbers. Other items that match:
- «أنا سعيدة أنك هنا», and «تحدثت اليوم مع {responder}» using the name from CrisisEvents.
- The «خطة أمانك جاهزة» card with «فتح» → `/safety-plan`.
- «العودة للرئيسية» marks the follow-up seen (POST `crisis/follow-up/seen`) → `/home`.
- «أحتاج التحدث مع أحد الآن» sends the same help request, joined to an open episode by the server.
- The API returns no transcript, no risk level and no diagnosis, and each read is audited (`BE/Endpoints/CrisisFollowUpEndpoints.cs:45-86`).

| severity | what's missing | design ref | code evidence | suggested fix |
|---|---|---|---|---|
| major | The «ما اتفقتما عليه» card is missing two of its three items: the follow-up call time («سيتصل بك د. فهد غدًا الساعة 11:00») and the support person («أختك ستبقى معك الليلة»). Only "therapist informed" and "next appointment" are shown. | P38 elements: «قائمة بنود المتابعة: موعد اتصال المتابعة، إبلاغ الأخصائية وتقديم الموعد، الشخص الداعم. تأتي من خطوات الحل التي سجلها الطبيب بصيغة موجهة للمريض» | `WP/components/afterCrisis/AfterCrisisView.tsx:45-51`; `FollowUpDto` has no such fields (`BE/Endpoints/CrisisFollowUpEndpoints.cs:25-35`) | Let the clinician record patient-facing close steps (follow-up call time, support person) when closing the event, and add them to the CrisisFollowUp DTO and the card. |
| minor | No loading skeleton. There is no `loading.tsx` in the whole app. | P38 states: «تحميل: هيكل بطاقات مؤقت» | `find WP/app -name loading.tsx` finds nothing | Add a `loading.tsx` skeleton (this also covers P41/P43/P44/P46/P53). |
| minor | Offline, the last copy is not shown. The page is not in the service worker's offline list. The numbers still work as server-rendered `tel:` links. | P38 states: «بلا اتصال: تُعرض آخر نسخة مخزنة وأرقام الطوارئ» | `web-patient/public/sw.js:22` | Leaving it out may be deliberate (PHI in the HTML). If so, document it. |
| minor | The copy ends «هذا ما اتفقنا عليه:» where the mockup has «اتفقتما». The colon stays even when there are no items. | Mockup P38 | `web-patient/messages/ar/afterCrisis.json` key `brave` | Use «اتفقتما», and drop the trailing sentence when `items` is empty. |

## P41 Session lobby: `/appointments/[id]/lobby`

✅ The route exists. The entry points are P28 (`JoinSessionCard.tsx:27`) and home (`HomeJoinButton.tsx:29`). Other items that match:
- «رجوع» → `/appointments/[id]` (`APP/appointments/[id]/lobby/page.tsx:36`).
- The countdown is mm:ss from server time, using the EC-09 offset (`WP/components/session/sessionClock.ts:171`), with the session number and duration.
- The connection and camera/microphone checks run on the device only. A failed check shows ⚠ plus a tip and does not block entry.
- «أنا في مكان هادئ وخاص» is optional and kept only on this device.
- The note field has a 1,000-character limit, autosaves after a pause and passes the crisis gate. A crisis result → `/crisis` (`PreSessionNoteField.tsx`).
- «دخول الجلسة» is disabled before the window opens, with the hint «يفتح قبل البدء بـ 10 دقائق». The server refuses a join outside the window.
- Offline, the countdown continues and a notice is shown.
- The late state turns into P54.
- The join goes to the in-app room (P42) through the video adapter, not an external link. This is documented in docs/api-v1.md:175-178 and the dev adapter is expected, so it is not counted as a gap.

| severity | what's missing | design ref | code evidence | suggested fix |
|---|---|---|---|---|
| blocker | If the therapist cancels (status `cancelled_by_therapist`), the lobby does not switch to P67. It falls into phase `closed`, shows «انتهى وقت هذه الجلسة» and offers «بعد الجلسة» → `/summary`. That page then says «أحسنت، أنهيت جلستك» for a session that never took place. This is a wrong link. The same happens for `no_show` (it should go to P63). | P41 states: «إلغاء الأخصائي: تتحول إلى P67 عبر AppointmentChanged»; next: P67 | `WP/components/session/sessionClock.ts:197` (`status !== 'confirmed'` → closed); `WP/components/session/SessionLobby.tsx:143-149`; `APP/appointments/[id]/summary/page.tsx:29` (no status check) | When the polled `status` is `cancelled_by_therapist`, call `router.replace('/appointments/{id}/cancelled')`. For a missed session use `/missed`. Link to the summary only when the status is `completed` or the session time is over. |

## P42 Live session: `/appointments/[id]/live` (design for a later phase, built with the dev adapter)

✅ The route exists. Other items that match:
- The therapist's name and the elapsed time.
- The «لا يوجد تسجيل» badge.
- Mute and camera toggles (local only).
- «إنهاء الجلسة» asks for confirmation, then → `/summary` (P43).
- «نعيد الاتصال…» on a connection loss.
- When the camera is denied: an explanation and «الاستمرار بالصوت».
- The join token is short-lived (`SessionRoom.tsx`).
- The messages button is out of scope.

| severity | what's missing | design ref | code evidence | suggested fix |
|---|---|---|---|---|
| minor | Help now leaves the room (`router.push('/crisis')`). The design wants an overlay on top of the session, with the therapist told inside the session. | P42 buttons: «يُنشئ حدثًا حرجًا ويعرض شاشة الأزمة كطبقة فوق الجلسة، ويُبلَّغ الأخصائي داخل الجلسة» | `WP/components/crisis/HelpNowButton.tsx` (`openCrisis`) | Can wait until P42 is in scope (D-v2-1). |
| minor | «جلسة مشفرة» is missing from the status bar. | P42 elements: «شريط الحالة العلوي … وعبارة «جلسة مشفرة»» | `WP/components/session/SessionRoom.tsx:141-152` | Add the label. |

## P43 After the session: `/appointments/[id]/summary`

✅ The route exists, and P28 links to it only for completed or started sessions (`APP/appointments/[id]/page.tsx:70`). Other items that match:
- The heading changes with the session number (`doneHeading`).
- The next session from Appointments, or a booking link when there is none.
- The homework card with «عرض» → `/homework/[id]`, hidden when there is no homework.
- Three single-choice rating options and the comfort checkbox, with «رأيك خاص ويصل للمشرف فقط».
- «حفظ والعودة للرئيسية» saves once with an Idempotency-Key. With no rating it just goes home. An error keeps the choice. An existing rating is shown read-only.
- Therapists never read the feedback (api-v1.md:179).

| severity | what's missing | design ref | code evidence | suggested fix |
|---|---|---|---|---|
| major | The page does not check the session status. Opened from the lobby's closed state, or from a direct link, it shows «أحسنت، أنهيت جلستك …» for a cancelled, missed or future session. | P43 entry: «بعد تسجيل الأخصائي نتيجة الجلسة (Completed)» | `APP/appointments/[id]/summary/page.tsx:18-34`; `SessionSummaryView.tsx:77` | Redirect `cancelled_by_therapist` → `/cancelled`, missed → `/missed`, and future → `/lobby`. |
| minor | The «ملخص من أ. سارة» card is missing. The code says there is no API for it. The guide (P43: "Homework + next booking") does not require it, and the API is marked مقترح. | P43 elements: «بطاقة ملخص الأخصائي» | `SessionSummaryView.tsx:28` (comment) | Add it later, once a published patient summary exists. |
| minor | The homework card lists all active homework, not «واجب جديد» from this session. | Mockup: «واجب جديد · تمرين التنفس قبل النوم · يوميًا» | `SessionSummaryView.tsx:100-118` | Filter to the homework assigned since the session started, or keep it and rename the card (it is titled «واجباتك» now). |

## P44 Change therapist: `/care/change-therapist`

✅ The route exists, with an entry from Settings (`WP/components/settings/SettingsMenu.tsx:10`). Other items that match:
- The reassurance text names the therapist and says the file stays.
- The five reasons come from the catalog API, are optional, and there is no free text.
- «سيقترح عليك المشرف … خلال {hours} ساعة», with the hours taken from config.
- «إرسال الطلب» sends with an Idempotency-Key. A 409 returns the open request and shows its status (requested, matching, offered → «عرض الاقتراحات» → `/matches`).
- The success message is shown. On error: a message and «إعادة المحاولة».
- The supervisor is alerted when the request is created (`care.change_requested`).

| severity | what's missing | design ref | code evidence | suggested fix |
|---|---|---|---|---|
| minor | «رجوع» always goes to `/settings`, not to the previous screen. | P44 buttons: «رجوع … الشاشة السابقة» | `APP/care/change-therapist/page.tsx:30` | Use history back, with `/settings` as the fallback. |
| minor | There is no entry from the therapist's page. Only Settings links here. | P44 entry: «من صفحة الأخصائي أو من الإعدادات أو من «المزيد»» | grep `change-therapist` finds only `SettingsMenu.tsx:10` | Add a link on the current therapist's profile. |
| minor | After success the screen stays put and shows the notice. The spec goes on to `/dashboard`. | P44 «إرسال الطلب» → `/dashboard` | `ChangeTherapistView.tsx:55-56` | Acceptable. Optionally add a «الرئيسية» link. |

## P46 Notifications and states: `/notifications`

✅ The route exists, with the bell in the header (`WP/components/AppHeader.tsx:17`). Other items that match:
- Each entry has an icon by category, its text, a relative time and an unread dot.
- A press marks it read (POST `me/inbox/{id}/read`) and opens its link.
- «ليس أنت؟ أنهِ الجلسة» asks for confirmation, then sends DELETE `me/sessions/{id}` and suggests changing the password (→ `/settings/security`).
- Pagination by cursor.
- The empty state «لا توجد إشعارات».
- The error state blames the system and shows «إعادة المحاولة» and the problem code from RFC 7807 `code` (`APP/notifications/page.tsx:25-45`).

| severity | what's missing | design ref | code evidence | suggested fix |
|---|---|---|---|---|
| major | The shared empty state for homework has no «تمرين تنفس الآن» button. P29's empty state shows only a title and body. | P46 elements «حالة فارغة (الواجبات) … مع بديل مفيد»; button «تمرين تنفس الآن» | `WP/components/homework/HomeworkListView.tsx:58` | Add a secondary button that opens the existing `BreathingPacer` in a dialog (as P54 does), since `/library/[key]` is مقترح. |
| minor | The loading state has no skeleton with `aria-busy` and «لحظات…». | P46 elements «حالة التحميل» | No `loading.tsx` exists | Add `loading.tsx`. |
| minor | Offline, the last stored list is not shown. | P46 states «بلا اتصال: آخر قائمة مخزنة» | `web-patient/public/sw.js:22` | Document it as deliberate (no PHI cached), or cache it encrypted. |
| minor | The bottom nav highlights «الرئيسية» on this page. | Not in the design | `APP/notifications/page.tsx:48` | Pass no active item. |
| minor | The `homework.cancelled` notice is not catalogued. The patient sees the generic «لديك إشعار جديد.» in the «system» category. | P46: each entry has meaningful text | `BE/Endpoints/StaffCareEndpoints.Homework.cs:142`; missing from `BE/Notifications/NotificationCatalog.cs:90-93` and from the resx | Add the kind with category homework and ar/en text. |

## P53 End of care: `/care/complete`

✅ The route exists. Entry: the home card (`CareEndedCard.tsx:8`) and `care.ended`. The page shows only after the supervisor signs off (`GET me/care-closure` returns 404 before that, and the page then shows an empty state). Other items that match:
- The thanks with the session count and the therapist's name.
- The relapse-prevention plan (signs, what to do, the reminder at one month), hidden when there is none.
- The «يبقى لك» card.
- «تحميل ملخصي» downloads a summary with no diagnosis, and the download is audited. It is plain text rather than PDF, which falls under export file generation and is out of scope.
- «أحتاج أرجع للجلسات» → POST `me/care/return` → `/matches`, with the previous therapist suggested first.
- The return note.
- Help now stays in the header (§6).

| severity | what's missing | design ref | code evidence | suggested fix |
|---|---|---|---|---|
| blocker | The return does not ask for a short safety check. `ReturnAsync` only re-seeds the match and sends the patient to `/matches`. Missing safety feature. | P53 «أحتاج أرجع للجلسات»: «يعيد فتح المطابقة مع الأخصائي السابق أولًا، **ويطلب فحص سلامة قصير**» | `BE/Endpoints/CareJourneyEndpoints.cs:217-232`; `WP/components/care/CareCompleteView.tsx:134-144` | Send the patient through the existing safety check (`/start/safety-check`, P23), or a short gated check-in, before `/matches`, and have the server record it. |
| minor | Items in «يبقى لك» are plain text. There is no link to the safety plan, even though P37 is in `next`. | P53 next: P37, P49, P50 | `CareCompleteView.tsx:167-170` | Link «خطة السلامة» → `/safety-plan`. P49 and P50 belong to later phases. |

## P54 Therapist late: lobby state

✅ This is a state of P41 (`lobbyPhase` → `late`/`lateOptions`). Other items that match:
- The therapist card and «{name} تأخرت قليلًا».
- The therapist's notice (when sent) with «الوقت المتأخر لا يُخصم من جلستك».
- A counter since the start, from server time.
- «وأنت تنتظر» with a breathing exercise in a dialog, without leaving the lobby.
- The 15-minute explanation, using `lateOptionsAfterMinutes` from config.
- «إعادة الجدولة الآن» is always there and becomes primary after 15 minutes. The server makes it free and not bound by the 24-hour limit, audited `therapist_late` (`BE/Endpoints/SchedulingEndpoints.cs:325-338`).
- «أخصائي آخر متاح» after 15 minutes (the directory, as documented in api-v1.md:177).
- When the therapist joins, the join button enables itself through polling.

| severity | what's missing | design ref | code evidence | suggested fix |
|---|---|---|---|---|
| blocker | Cancellation does not turn into P67. This is the same defect as P41, because P54 is a lobby state. | P54 states: «إلغاء: تتحول إلى P67»; next: P67 | `sessionClock.ts:197`; `SessionLobby.tsx:143-149` | Same fix as P41. |
| minor | The therapist's own message («أعتذر، سأكون معك خلال 5 دقائق») is replaced by a template built from the minutes. | P54 elements «رسالة الأخصائي (إن أرسلها)» | `LateNotice.tsx:203-205` | Fine if S18 only sends minutes. Otherwise show the text. |
| minor | Two labels differ: «تمرين تنفس» instead of «تمرين تنفس دقيقتين», and there is no «→ غرفة الانتظار» link (the state is shown inside the lobby). | Mockup P54 | `messages/ar/session.json` `late.breathing` | Copy only. |

## P63 Missed session: `/appointments/[id]/missed`

✅ The route exists. Entry: the P28 redirect, the home notice and the `appointment.missed` link. Other items that match:
- The title «فاتتك جلسة اليوم», with the date on other days.
- «يصير، ولا عليك شي. ما تنخصم ولا تنحسب ضدك.»
- «→ رجوع» → `/home`.
- «كيف حالك الآن؟» with three choices. «مو بخير» shows resources and talk-now.
- «حجز موعد جديد» → `/therapists/{same}/book`.
- «أحتاج أتكلم مع أحد الآن» sends the help request → `/crisis`.
- The server raises a supervisor follow-up for a second missed session in a row (EC-53) or for one at risk 3–4 (`BE/Endpoints/StaffEndpoints.cs:246`).

| severity | what's missing | design ref | code evidence | suggested fix |
|---|---|---|---|---|
| blocker | «مو بخير» does not alert the care team, and the answer is never sent to the server. A patient who says they are not OK after a missed session goes unseen unless they also press another button. Missing safety feature. | P63 «بخير / متعب شوي / مو بخير»: «يسجل الحال؛ «مو بخير» يعرض موارد الأزمة وخيار التحدث الآن **وينبه فريق الرعاية**» | `WP/components/appointments/MissedCheckIn.tsx:13-16` ("The answer is not sent"), `:30-37` | Add `POST /v1/appointments/{id}/missed-check-in`, audited. For `not_ok`, notify the treating therapist and the supervisors, or open a follow-up task, without closing any existing task. |
| major | The supervisor notices `appointment.no_show_followup` (EC-53, §11.1) and `appointment.rebook_needed` (EC-52) are not catalogued. They are stored as generic «لديك إشعار جديد.» notices in the «system» category, not as important. | P63 rules (S66 task, EC-53) | `BE/Clinical/AppointmentNotices.cs:21,24`; missing from `BE/Notifications/NotificationCatalog.cs:79-100` and the resx | Catalogue both: category appointments or risk, Important, ar/en text. |
| minor | The «أ. سارة كتبت لك» card is always hidden. There is no therapist note in the API. | P63 elements «رسالة الأخصائي» (the hidden state is allowed) | `APP/appointments/[id]/missed/page.tsx:89-91` | Add an optional note to S18's no-show action. |

## P67 Therapist cancelled: `/appointments/[id]/cancelled`

✅ The route exists. Entry: the P28 redirect, the home notice and the `cancelled_by_therapist` link `/appointments/{id}/cancelled`. Other items that match:
- The apology gives only «لظرف طارئ» and never the real reason.
- «→ رجوع» → `/home`.
- «أنتظر {name} · اختر موعدًا جديدًا» → the same therapist's booking.
- The 48-hour team note. The supervisors get `rebook_needed`.
- «لن ينقص هذا من عدد جلساتك.»
- Talk-now.

| severity | what's missing | design ref | code evidence | suggested fix |
|---|---|---|---|---|
| blocker | The same-slot substitute card and its primary action «الجلسة مع د. منى» are missing. The code says this waits for S65. The guide requires it: "P67 … Same-slot substitute offered first, or priority rebook". | P67 buttons «الجلسة مع د. منى … POST /v1/appointments/{id}/accept-substitute → lobby»; guide.md:1478 | `APP/appointments/[id]/cancelled/page.tsx:22-26` | Build S65's substitute suggestions plus the accept endpoint, with Idempotency and a fallback when the slot is taken. If S65 is scheduled for phase 6–8, downgrade this to a documented deferral. |
| major | The rebook has no priority. It is a plain new booking, not a reschedule with priority, and «موعدك الجديد له أولوية في جدولها» is missing. | P67 «أنتظر أ. سارة»: «يفتح مواعيد الأخصائي الأصلي مع أولوية … POST /v1/appointments/{id}/reschedule» | `cancelled/page.tsx:54-57` (no `?reschedule=`) | Pass `?reschedule={id}`, let the server accept a reschedule of a therapist-cancelled appointment (with priority slots, or at least the hold), and add the line. |
| major | The `rebook_needed` supervisor alert (EC-52) arrives as generic text. See P63. | P67 states (EC-52) | `NotificationCatalog.cs` | See P63. |

---

## End to end: P10 / P29 with the therapist's homework (S16)

✅ The flow works:
- S16 (`WS/features/care/patient-homework.component.ts`) assigns through POST `staff/patients/{p}/homework`. It validates the title (≤ 120), instructions (≤ 2,000), frequency and an end date of today or later on both client and server. The record is encrypted with the patient key (`BE/Endpoints/StaffCareEndpoints.Homework.cs:71-109`), and the patient gets `homework.assigned` → `/homework/{id}`.
- P29 reads the same `HomeworkAssignments` rows. It shows the therapist's name, the schedule, the «اليوم» and «رد جديد» badges, 7 dots with an aria-label, the completed items and the empty state.
- P10 shows the numbered instructions and «كيف كان اليوم؟» (required), with the 0–10 slider hidden on «ليس اليوم». The note (≤ 1,000) passes the crisis gate, and the gate level is kept for S16's risk badge. The save goes through the outbox (queued offline, Idempotency).
- The therapist replies to a check-in (`staff/checkins/{id}/feedback`, with a preview) and the reply appears on P10. Opening P10 marks it seen, so the P29 badge clears.
- S16 edits show at once. Cancelling (after confirmation) makes the exercise read-only on P10 and moves it to «مكتملة».

| severity | what's missing | design ref | code evidence | suggested fix |
|---|---|---|---|---|
| minor | A cancelled exercise shows its original, future end date: P10 «انتهى هذا التمرين في <future date>» and P29 «انتهت <future date>». `Cancel` sets only `CancelledAtUtc`, while the API doc says "cancel: ends today". | P29 «المكتملة: … مع تاريخ الانتهاء»; api-v1.md:217 | `backend/src/Tamaneen.Clinical/Care/Homework.cs:106-115`; `BE/Endpoints/CareEndpoints.cs:354-363`; `HomeworkDetailView.tsx:175`; `HomeworkListView.tsx:78` | Set `EndsOn = today` on cancel (or expose `endedOn`). |
| minor | The 7 dots for a daily exercise count all done days, capped at 7, while the label says «هذا الأسبوع». | P29 «الإنجاز الأسبوعي»; «3 أيام من 7» | `BE/Endpoints/CareEndpoints.cs:361` (`Daily ? doneDays : doneThisWeek`); `HomeworkListView.tsx:21,37` | Use `doneThisWeek` for the dots. |
| minor | The patient's notice for a cancelled exercise is generic. See P46. | — | `NotificationCatalog.cs` | Catalogue `homework.cancelled`. |
| minor | `startsOn` is the therapist's browser date, not the patient's local date. Check-ins are also accepted before `StartsOn`. | — | `patient-homework.component.ts:33,150`; `CareEndpoints.cs:303-307` | Let the server set `startsOn` (`care.TodayAsync`), and refuse a check-in before the start. |

## End to end: P37 with the therapist-built plan (S35)

✅ The flow works:
- S35 (`WS/features/care/safety-plan-builder.component.*`) saves through POST `staff/patients/{p}/safety-plans` with If-Match. A 412 shows both versions. P37 reads the same `SafetyPlans` row and the same section keys (`backend/src/Tamaneen.Clinical/Care/SafetyPlan.cs:11`), with the same encryption.
- A save by the therapist sets `CoAuthorTherapistId`, so P37 shows «كتبناها معًا … · آخر تحديث» (`CareEndpoints.cs:436-442`).
- The contacts appear as `tel:` buttons in step 4.
- Step 5 takes the emergency number and support line from platform config.
- The reason card is there.
- The patient gets `safety_plan.updated` → `/safety-plan`. P37 refreshes from the API over the encrypted device copy.
- The shared version number makes a later patient edit and the therapist's save conflict-safe in both directions.

| severity | what's missing | design ref | code evidence | suggested fix |
|---|---|---|---|---|
| minor | S35's «إضافة شخص» has no relationship field, and the phone is required. The design says «الاسم والعلاقة ورقم اختياري». P37 shows contacts as call buttons, so a contact without a phone could not be shown there anyway. | S35 buttons «إضافة شخص» | `WS/features/care/safety-plan-builder.component.html:72-85` | Add the optional relationship. Allow no phone and render such a contact as text in P37. |
| minor | Section length limits differ: S35 1,000 (design), server 1,500. | S35: «نصوص حتى 1000 حرف» | `CareEndpoints.cs` `CleanPlan` (1500); `safety-plan-builder.component.html:61` (1000) | Align them. |

---

## Summary

| severity | count |
|---|---|
| blocker | 5 |
| major | 5 |
| minor | 24 |

The uncatalogued `rebook_needed` alert appears under both P63 and P67 but is counted once among the majors.

**Blockers**
1. P41 and P54: a therapist cancellation (or a missed session) is not routed to P67 (or P63). The lobby shows «انتهى وقت هذه الجلسة» plus a wrong link to the P43 «أحسنت، أنهيت جلستك» page (`sessionClock.ts:197`, `SessionLobby.tsx:143-149`).
2. P63: «مو بخير» does not alert the care team, and the answer is not sent (`MissedCheckIn.tsx:13-16`).
3. P53: «أحتاج أرجع للجلسات» skips the required short safety check (`CareJourneyEndpoints.cs:217-232`).
4. P67: the same-slot substitute card and its primary action «الجلسة مع د. منى» are missing, though guide.md:1478 requires them. Downgrade this if S65 is formally deferred to phase 6–8.

The table counts 5 blockers because item 1 appears once under P41 and once under P54.

**Majors**
- P38: two of the three «ما اتفقتما عليه» items are missing (the follow-up call time and the support person).
- P43: the summary page has no status guard, so it shows "well done" for cancelled, missed or future sessions.
- P46 and P29: the homework empty state lacks «تمرين تنفس الآن».
- P63 and P67: the supervisor notices `appointment.no_show_followup` (EC-53) and `appointment.rebook_needed` (EC-52) are not catalogued, so they show generic, non-important text.
- P67: the rebook is a plain booking, with no priority and no reschedule.

The end-to-end flows work: P10 and P29 with S16, and P37 with S35. Only minor data issues remain: the end date of a cancelled exercise, the weekly dots, and `startsOn` set from the therapist's timezone.
