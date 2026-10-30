# Conformance review r3: P25–P37 (patient app)

I checked this by reading the code only. Paths are relative to `/home/user/ibrahim/tamaneen/`. `WP` = `web-patient/src`, `APP` = `web-patient/src/app/[locale]/(app)`.
I did not count documented route renames (docs/frontend-patient.md). Examples: `/book/confirmed` → `/appointments/[id]/confirmed`, `/settings/profile` → `/settings`, `/settings/privacy` → `/privacy`, `/dashboard` → `/home`, `/help-now` → `/crisis`, `/matching` → `/matches`, `/book/[id]` → `/therapists/[id]/book`.

## Cross-cutting (applies to every screen P25–P37; counted once)

| severity | what's missing | design ref | code evidence | suggested fix |
|---|---|---|---|---|
| blocker | The header button «مساعدة فورية» does not send `POST /v1/crisis/help` and does not open a Critical event. It only links to `/crisis-resources` (P14). The request needs a second tap on «اطلب اتصال» there. | every screen, button «مساعدة فورية»: "يعرض أرقام الطوارئ فورًا ثم يرسل الطلب ويفتح حدث أزمة بمستوى Critical (PB-26)… عند انقطاع الاتصال… يُصفّ الطلب (EC-04)"; guide PB-26 "Opens crisis screen; opens Critical event" | `WP/components/HelpNowLink.tsx:9` (`href="/crisis-resources"`); the POST exists only in `WP/components/crisis/RequestCallButton.tsx:25`, which P14 renders at `crisis-resources/page.tsx:53` | For signed-in users, make the header action show the numbers and then fire `crisis/help` automatically (queue it when offline), then go to `/crisis`. Keep the tel: links visible. |
| minor | No loading skeletons (no `loading.tsx` anywhere). Every screen spec has a «التحميل: هيكل…» state. | states on P25–P34, P36 | `find WP -name loading.tsx` → none | Add `loading.tsx` skeletons per route group. |
| minor | No patient realtime hub (AppointmentChanged, HomeworkAssigned, FeedbackReceived, MatchReady). It is also missing from the API contract. | P27/P29/P36 api «Hub /hubs/patient» | no signalr or hub code in `WP`; `docs/api-v1.md` lists only `/hubs/crisis` (line 118) | Add the hub to the contract, or poll. See the P36 item. |

## P25 Therapist profile (`/therapists/[id]`)
✅ Route exists (`APP/therapists/[id]/page.tsx`). Shown: initials avatar, name, title, «ترخيص موثّق من المنصة» badge, specialty tags, «عن أسلوبي», details (languages, «عن بُعد · N دقيقة», next slot). «اختيار موعد» goes to `/therapists/[id]/book` (P08). An unknown id returns 404.

| severity | what's missing | design ref | code evidence | suggested fix |
|---|---|---|---|---|
| major | The «اختيار موعد» button is always rendered. It is not hidden when triage is not approved or the safety check is not done. The booking page only reacts after the slots call fails. | buttons «اختيار موعد»: "مخفي إن لم يُعتمد الفرز ولم يُنجز فحص السلامة"; rule "زر الحجز لا يُعرض أصلًا لمن لا يحق له الحجز" (PB-39) | `APP/therapists/[id]/page.tsx:84-88`; `me.onboarding.triageStatus/screeningDone` are loaded (line 31) but not checked | Render the button only when `me.onboarding` shows triage approved or screening done. |
| major | No-slots state: the button stays enabled and there is no waitlist link. Only the value «لا مواعيد متاحة حاليًا» changes. | state "لا مواعيد متاحة: نص «لا مواعيد حاليًا» وتعطيل الزر مع رابط لقائمة الانتظار" | `page.tsx:79` (text only), `:85` (button unconditional) | Disable the button when `nextAvailableAt` is null and add a waitlist link. |
| minor | «رجوع» always goes to `/therapists`, even when the patient came from P07 `/matches`. | «رجوع» → "الشاشة السابقة"; entry "من بطاقة في P07 أو صف في P24" | `page.tsx:42` | Use history back, or a `from` param. |
| minor | The 404 page offers home and emergency resources but no link back to the therapist list. | state "404 مع رابط للقائمة" | `WP/app/[locale]/not-found.tsx:15-20` | Add a list link, or a profile-specific not-found. |

## P26 Booking confirmed (`/appointments/[id]/confirmed`)
✅ Shown: success mark, «تم الحجز», appointment card (date and time, therapist, type, duration), the three «ماذا يحدث بعد ذلك» steps (copy matches the mockup). «إضافة للتقويم» downloads `.ics` from `GET /v1/appointments/{id}/calendar`. «الذهاب للرئيسية» goes to `/home`. Booking pushes here (`BookingFlow.tsx:174`).

| severity | what's missing | design ref | code evidence | suggested fix |
|---|---|---|---|---|
| major | The `.ics` is not neutral. SUMMARY is «جلسة طمأنينة مع {therapist}», the file name is `tamaneen-session.ics`, and the UID ends `@tamaneen.app`. | «إضافة للتقويم»: "ينزّل ملف .ics بعنوان محايد «موعد» دون اسم المريض أو الأخصائية أو المنصة (PB-42)" | `backend/src/Tamaneen.Api/Resources/SharedResource.ar.resx:76` and `SharedResource.resx:76`; `Endpoints/SchedulingEndpoints.cs:240,246,257` | Set SUMMARY to «موعد» / "Appointment" with no therapist and no platform name, and use a neutral file name and UID domain. |
| minor | Opening the page for an old or unrelated appointment still shows «تم الحجز». There is no redirect to `/appointments`, and a fetch failure throws instead of using the previous page's state. | states "فتح الصفحة دون حجز حديث: تحويل إلى /appointments"; "فشل الجلب: عرض بيانات الحجز من حالة الصفحة السابقة" | `APP/appointments/[id]/confirmed/page.tsx:27-29` | Redirect when the appointment is not newly created, or is not `confirmed` and in the future. |

Note (not counted): the documented route puts the appointment id in the URL, against rule "لا معرّف الموعد في الرابط". The rename is documented.

## P27 My appointments (`/appointments`)
✅ Shown: «القادمة» and «السابقة» sections, rows with date, time, therapist, «الجلسة الأولى…», the «مؤكد» tag, and the cancelled row «تم إلغاؤه · ألغته الأخصائية · أُعيدت الجدولة». Each row links to `/appointments/[id]`. The bottom nav goes to /home, /appointments, /homework, /settings. There is an empty state.

| severity | what's missing | design ref | code evidence | suggested fix |
|---|---|---|---|---|
| major | «حجز موعد جديد» always opens the directory `/therapists`. It should open booking with the current therapist, or the matches when no therapist is assigned. | «حجز موعد جديد» → "/book/[therapistId] (P08) أو /matching (P07)" | `APP/appointments/page.tsx:28` | Link to `/therapists/{assignedTherapistId}/book` when one is assigned, otherwise to `/matches`. |
| minor | The empty state has no booking button inside it (the link is below it). | state "لا مواعيد: رسالة مع زر الحجز" | `WP/components/appointments/AppointmentListView.tsx:65` | Add the CTA inside the EmptyState. |
| minor | No live update on AppointmentChanged. | state "تحديث لحظي عند AppointmentChanged" | see cross-cutting | — |

## P28 Appointment detail (`/appointments/[id]`)
✅ Shown: details (date, therapist, type, status), the session-link card, and the pre-session tip with the mockup text. The join button stays disabled and labelled «· بعد يومين» until `joinAvailableAt`. The server only returns `joinUrl` once it opens by server time (`SchedulingEndpoints.cs:313`), and it opens in a new tab. «تغيير الموعد» opens `/therapists/{id}/book?reschedule=` (P08). «إلغاء الموعد» opens a confirmation dialog and then `POST …/cancel`. Both are disabled after the 24 h deadline, with the deadline note. Cancelled or past appointments are read-only. «رجوع» goes to `/appointments`.

| severity | what's missing | design ref | code evidence | suggested fix |
|---|---|---|---|---|
| major | The cancel dialog has no cancel reason (Catalogs cancel reasons). The contract's cancel takes no body either. | «إلغاء الموعد»: "نافذة تأكيد مع سبب من Catalogs (cancel reasons)… (PB-45)" | `WP/components/appointments/AppointmentActions.tsx:49` (POST without body), dialog `:95-114`; `docs/api-v1.md:82` | Add a reason select to the dialog and a `{ reasonCode }` body to the contract and endpoint. |
| minor | No Idempotency-Key on cancel or reschedule. | rule "الطلبات بمفتاح Idempotency" | no `Idempotency` anywhere in `WP` or `backend/src/Tamaneen.Api` | Send an `Idempotency-Key` header and dedupe on the server. |

## P29 My homework (`/homework`)
✅ Shown: title and the reassurance text (copy matches), rows with title, «رد جديد» or «اليوم» badge, frequency, weekly count, and the 7-dot meter with an aria-label. Completed rows are read-only with the end date. Rows link to `/homework/[id]` (P10). Bottom nav, empty state. No streaks or blame.

| severity | what's missing | design ref | code evidence | suggested fix |
|---|---|---|---|---|
| minor | The due badge shows only «اليوم». A weekday such as «الجمعة» (in the mockup) is never shown. | element "شارة الاستحقاق (اليوم، الجمعة)" | `WP/components/homework/HomeworkListView.tsx:31-32` (`dueToday` boolean only) | Add `nextDueOn` to HomeworkItem and format the weekday. |
| minor | Offline does not show the last saved list. The service worker serves `/offline` for `/homework`. | state "بلا اتصال: آخر نسخة مخزنة" | `web-patient/public/sw.js:22` (OFFLINE_CAPABLE excludes /homework) | Cache the list data, encrypted or with no PHI, or accept the gap. |

## P30 Mood check-in (`/check-in`)
✅ Shown: 0–10 slider (step 1, keyboard, large value, «سيئ جدًا»/«ممتاز»), the five optional tags as toggles, optional note, 7-day chart (bars plus an sr-only table, no blame for empty days), «حفظ» → `POST /v1/mood`, `crisis:true` → `/crisis`. One entry a day is pre-filled. «رجوع» goes to `/home`.

| severity | what's missing | design ref | code evidence | suggested fix |
|---|---|---|---|---|
| major | No offline queue. A save while offline shows a network error instead of «سنرسلها عند عودة الاتصال» and queueing the entry. | «حفظ»: "بلا اتصال يُصفّ"; state "بلا اتصال: «سنرسلها عند عودة الاتصال»" | `WP/components/mood/CheckInForm.tsx:51,60` | Queue the entry in an outbox (IndexedDB, with a client id) and replay it on reconnect. |
| minor | The note limit is 1,000 characters, while the spec suggests 2,000 (marked «مقترح»). | «ملاحظة (اختياري)»: "حتى 2,000 حرف (مقترح)" | `CheckInForm.tsx:96` | Align with the API limit. |

## P31 Settings / profile (`/settings`)
✅ Section menu: profile `/settings`, security `/settings/security`, privacy `/privacy`, notifications `/settings/notifications`, emergency contact `/settings/safety`. The nickname is required (max 40). The language toggle (العربية / English) uses `PUT me/language` and re-renders RTL/LTR. There is a time zone select. «حفظ» sends `PUT /v1/me/profile` and shows a short «saved» notice. «تسجيل الخروج» calls logout and goes to `/`. Bottom nav.

| severity | what's missing | design ref | code evidence | suggested fix |
|---|---|---|---|---|
| major | Sign-out does not clear the saved pages or the local safety plan. `clearCachedPlan()` is never called, and the service-worker runtime cache is never purged. The same applies to «تسجيل الخروج من كل الأجهزة» on P32. | «تسجيل الخروج»: "ينهي الجلسة ويمسح الصفحات المخزنة (PB-61)"; rule "تسجيل الخروج… يمسح ذاكرة الصفحات" | `WP/components/settings/ProfileForm.tsx:97-98`; `WP/components/safety/planCache.ts:36` (`clearCachedPlan`, no callers); `SecurityPanel.tsx:218-219` | On sign-out, call `clearCachedPlan()`, clear the drafts, and send a message to the service worker to delete `tamaneen-runtime-*`. |
| major | No If-Match / ETag on save and no handling of a 412 conflict. Neither the client nor the API supports it. | «حفظ»: "يحفظ التغييرات مع If-Match (PB-52)"; state "تعارض 412: عرض القيم الحالية" | `ProfileForm.tsx:79`; no `If-Match` or `ETag` in `WP` or `backend/src/Tamaneen.Api/Endpoints` | Return an ETag from GET /me, send If-Match, and reload the values on 412. |
| minor | The time zone list has 6 Gulf zones, not the IANA list. | «المنطقة الزمنية»: "قائمة مناطق IANA" | `ProfileForm.tsx:16-23` | Use `Intl.supportedValuesOf('timeZone')`, or document the pilot limit. |

## P32 Security (`/settings/security`)
✅ Rows for password (changed ago, «تغيير» opens a form with current and new password, min 12; the server signs out other sessions, `MeEndpoints.cs:349`), authenticator («إدارة»: set up or disable), recovery codes («إنشاء جديدة»: confirmation dialog, 10 codes shown once, copy and download), devices (this device «نشط الآن», others with last activity, «إنهاء»), «تسجيل الخروج من كل الأجهزة» → revoke-all + logout → `/login`. A wrong current password keeps the fields. «رجوع» goes to `/settings`. Passkeys are out of scope.

| severity | what's missing | design ref | code evidence | suggested fix |
|---|---|---|---|---|
| major | Disabling TOTP asks only for the code, not the password. | «إدارة»: "التعطيل يتطلب كلمة المرور والرمز" | `WP/components/settings/SecurityPanel.tsx:137`; server `MeEndpoints.cs:380-386` checks only the code | Add a password field and verify it on the server. |
| major | Regenerating recovery codes needs no re-authentication (no password, no MFA), on the client or the server. | «إنشاء جديدة»: "يتطلب كلمة المرور والمصادقة إن كانت مفعّلة… (PB-55)"; rule "الإجراءات الحساسة تتطلب إعادة المصادقة" | `SecurityPanel.tsx:159` (POST with no body); `MeEndpoints.cs:395-401`; `docs/api-v1.md:51` | Require `{ password, code? }` in the contract and endpoint, and add the fields to the dialog. |
| major | «إنهاء» per device revokes at once, with no confirmation. | «إنهاء»: "تأكيد ثم إلغاء جلسة ذلك الجهاز" | `SecurityPanel.tsx:389` | Add a confirmation dialog. |
| major | «تسجيل الخروج من كل الأجهزة» runs at once, with no confirmation. | "تأكيد ثم إلغاء كل الجلسات بما فيها الحالية (PB-54)" | `SecurityPanel.tsx:402` | Add a confirmation dialog. |
| minor | The new-password field has no strength meter, although sign-up uses `PasswordField` with one. | «تغيير»: "نفس قواعد القوة" | `SecurityPanel.tsx:239-247` (plain TextField) | Reuse `components/auth/PasswordField`. |

## P33 Notifications (`/settings/notifications`)
✅ Channel switches (push, SMS, e-mail), neutral notifications, the quick-exit switch (stored and applied through a cookie in `(app)/layout.tsx`), reminder frequency (daily / every 2 days / off) and time, quiet hours with «تعديل» from/to pickers, the emergency note («اتصالات فريق الطوارئ تصلك دائمًا…»), «حفظ» → `PUT /v1/me/notifications`, saved notice, «رجوع» → `/settings`.

| severity | what's missing | design ref | code evidence | suggested fix |
|---|---|---|---|---|
| minor | Turning on app notifications does not request browser permission and has no "permission denied" notice with steps. There is no iOS non-installed redirect (P65 is outside this set). | state "إذن الإشعارات مرفوض في المتصفح: تنبيه مع خطوات التفعيل"; switch "على iOS غير المثبت يحوّل… (P65)" | `WP/components/settings/NotificationsForm.tsx:80` (plain switch) | Check `Notification.permission` and request on enable. Show a notice when denied. |

## P34 Emergency contact (`/settings/safety`)
✅ Intro text, your phone, trusted person, their phone, location switch with «مفعّلة منذ …», «حفظ» → `PUT /v1/me/safety-info` (the server validates the phone format, encrypts, and syncs the location consent). «حذف جهة الطوارئ» opens a confirmation dialog and then `DELETE /v1/me/safety-info/trusted-contact`. Field errors show under each field. «رجوع» → `/settings`. The screen stays reachable during a crisis lock (`CrisisLockGuard.tsx:7`).

| severity | what's missing | design ref | code evidence | suggested fix |
|---|---|---|---|---|
| major | No confirmation when the patient withdraws location sharing. | «مشاركة موقعي عند الطوارئ»: "منح أو سحب موافقة الموقع، مع تأكيد عند السحب" | `WP/components/settings/SafetyForm.tsx:97` (`onChange={setShareLocation}`) | Open a confirmation dialog when switching from on to off. |
| major | The rule that the trusted name and number go together is not enforced on the client or the server. | inputs: "فحص صيغة الهاتف؛ الاسم والرقم متلازمان" | `SafetyForm.tsx:54-56` (no check); `backend/.../MeEndpoints.cs:276-282` | Reject a name without a number or a number without a name, with field errors. |
| major | No If-Match on save. | «حفظ»: "يحفظ البيانات مشفرة مع If-Match" | `SafetyForm.tsx:56` | Same fix as P31. |
| minor | The trusted name allows 80 characters (spec: 100), and the phone inputs have no 32-character limit. | elements "حتى 100 حرف"; "E.164، حتى 32 حرفًا" | `SafetyForm.tsx:90` (`maxLength={80}`), `:89,:91` (no maxLength); server `MeEndpoints.cs:279` | Align the limits with the spec. |

## P35 System states (`/offline`, `/session-expired`, `/maintenance`, 404)
✅ All four exist, with copy that matches the mockup. Offline shows the emergency and support-line tel: links and «إعادة المحاولة». Session-expired shows «تسجيل الدخول» → `/login`. Maintenance shows the return time (env) and the emergency number. 404 shows «العودة للرئيسية» and «موارد الطوارئ». The service worker precaches crisis resources and offline in both languages. The header with Help now is present on every state.

| severity | what's missing | design ref | code evidence | suggested fix |
|---|---|---|---|---|
| major | There is no offline outbox (IndexedDB with client ids), and «إعادة المحاولة» only reloads the page. The offline copy promises «رسالتك محفوظة وسنرسلها تلقائيًا», but nothing is queued (drafts exist only for intake and the triage addendum, in localStorage, for 10 minutes). | «إعادة المحاولة»: "يرسل صندوق الرسائل المعلّق بنفس المعرّفات"; rule "الصندوق المحلي في IndexedDB بمعرّفات العميل… (EC-01)" | `WP/components/pwa/RetryButton.tsx:8`; `WP/components/pwa/drafts.ts:6-18` | Build an outbox module that `RetryButton` and the `online` event can flush. |
| major | After login the patient is not returned to the previous path. `/session-expired` gets no `next` and LoginForm has none. | «تسجيل الدخول»: "ينتقل للدخول ثم يعيد المسودة والمسار" | `WP/lib/api/server.ts:25,27`, `WP/lib/api/client.ts:48` (redirect with no path); `session-expired/page.tsx:12` | Pass a validated relative `next`, and use it in `finishSignIn`. |
| major | Session-expired shows no emergency numbers, and 404 shows only a link. | rule "أرقام الطوارئ ظاهرة في كل حالة وتُقرأ من النسخة المخزنة" | `WP/app/[locale]/(public)/session-expired/page.tsx:11-15`; `not-found.tsx:18` | Render the `loadSafetyNumbers()` tel: links on both. |
| major | Nothing routes to `/maintenance`. There is no feature flag or middleware check; the page is only reached by typing the URL. | state "الصيانة تُفعَّل بعلامة ميزة من S72"; entry "يعرضها… الموجّه تلقائيًا" | `WP/middleware.ts:35-44` (no maintenance check); `WP/lib/platform-config.ts:43` (env var only) | In middleware, redirect to /maintenance when the flag from meta/config is on, and keep /crisis-resources exempt. |
| minor | 404 «العودة للرئيسية» always goes to `/`, even for signed-in users. P01 then offers a "continue" action. | «العودة للرئيسية»: "للمسجل: الرئيسية، ولغيره: الصفحة العامة" | `not-found.tsx:15` | Choose `/home` when `hasSession()`. |

## P36 Waiting for review (`/waiting`)
✅ Shown: «شكرًا لثقتك» and subtitle, a 3-step timeline (sent at, reviewing with «المتوقع خلال N ساعة» from `expectedBy`, three therapists «سنرسل لك إشعارًا»), the breathing tile (inline 3-minute pacer), «أضف شيئًا» (gated `POST /v1/triage/addendum`, `crisis` → `/crisis`, draft kept), and the safety hint. No risk level or scores are shown. `/home` sends pending triage here (`WP/lib/onboarding.ts:27`). When suggestions are ready, a link to `/matches` appears.

| severity | what's missing | design ref | code evidence | suggested fix |
|---|---|---|---|---|
| major | When triage is approved, the page does not move to P07 by itself. The patient has to reload to see the link (no MatchReady hub and no polling). | state "اعتماد الفرز: MatchReady ينقل إلى P07" | `APP/waiting/page.tsx:90` (link only on server render) | Poll `GET /v1/triage` or subscribe to a hub, then `router.replace('/matches')`. |
| minor | Overdue copy: it shows «نتوقع أن تنتهي المراجعة قريبًا جدًا» instead of «نراجع ملفك، شكرًا لصبرك». | state "تجاوز المهلة: نص «نراجع ملفك، شكرًا لصبرك»" | `waiting/page.tsx:83`; `messages/ar/waitingTriage.json` `expectedSoon` | Use the spec text when `expectedBy` is past. |
| minor | Escalation to High/Critical does not change the screen to show emergency numbers. Only the crisis lock, when one is set, redirects. | state "رفع المستوى إلى High/Critical: … الشاشة تعرض أرقام الطوارئ" | `waiting/page.tsx` (no numbers block) | Show the tel: links when `crisis/status.eventOpen`. |
| minor | The help hint links to `/crisis-resources`, not to the Help Now action. | element "تنبيه السلامة: إن ساءت حالتك اضغط «مساعدة فورية»" | `waiting/page.tsx:106` | Follows the cross-cutting fix. |

## P37 Safety plan (`/safety-plan`)
✅ Shown: the «محفوظة على جهازك · تعمل بلا إنترنت» badge, «خطة أماني», a byline with co-author and update date, numbered sections 1–6, and the dark «سببي للاستمرار» card. Section 5 has server-rendered tel: links for emergency and the support line (from meta/config, with an env snapshot fallback). The page shows the cached copy at once and refreshes it in the background. When offline it shows a notice and the cached copy, and keeps that copy if the refresh fails. Empty plans get a message, and the numbers always show. «رجوع» → `/home`. The page is reachable during a crisis lock and is linked from home.

| severity | what's missing | design ref | code evidence | suggested fix |
|---|---|---|---|---|
| blocker | Section 4 «من أطلب منه المساعدة» shows the contacts as text chips. They are not `tel:` call buttons, and the data model has no phone number per contact (the section is free text). | element "أزرار اتصال بجهات اتصال شخصية من الخطة، كل زر رابط tel:"; button «أختي / صديقي خالد» → "tel:<رقم جهة الاتصال>… يعمل بلا إنترنت" | `WP/components/safety/SafetyPlanView.tsx:224-229` (`<li>` with no link); `WP/lib/api/types.ts:223` (`sections: Record<…, string>`) | Add structured `contacts: {name, phone}[]` to SafetyPlan (contract and API) and render `<a href="tel:">` buttons. |
| major | The cached plan is stored as plaintext JSON in `localStorage`. | element "IndexedDB مشفرة"; rule "لا تُخزَّن في localStorage كنص صريح (لا PHI في التخزين المحلي)" | `WP/components/safety/planCache.ts:18,29` | Move it to IndexedDB, encrypted with a non-extractable WebCrypto key, and clear it on sign-out (see P31). |
| minor | The screen has an edit mode («تعديل» → `PUT /v1/safety-plan`), but the spec says the screen is view-only. | rule "هذه الشاشة للعرض فقط" | `SafetyPlanView.tsx:267` | Confirm with product, or remove it. |
| minor | The numbers come from meta/config and env, not from `EmergencyContacts` filtered by ShowIn = safety plan. | element section 5 "من جدول EmergencyContacts (ShowIn يشمل خطة الأمان)، لا من نص ثابت" | `APP/safety-plan/page.tsx:12`; `WP/lib/platform-config.ts:37-60` | Read the published EmergencyContacts (content API) with the snapshot fallback. |

## Summary

| severity | count |
|---|---|
| blocker | 2 |
| major | 21 |
| minor | 21 |

**Blockers**
1. Cross-cutting: the header «مساعدة فورية» only links to `/crisis-resources`. It never sends `POST /v1/crisis/help` or opens a Critical event (PB-26), and does not queue the request offline (`HelpNowLink.tsx:9`).
2. P37: the personal help contacts are not `tel:` call buttons, and the model has no contact phones (`SafetyPlanView.tsx:224-229`).

**Majors**
- P25: the book button is shown to ineligible patients; no-slots does not disable it and has no waitlist link.
- P26: the `.ics` holds the platform and therapist names (not neutral «موعد»).
- P27: «حجز موعد جديد» goes to `/therapists` instead of the current therapist's booking or `/matches`.
- P28: the cancel dialog has no cancel reason.
- P30: no offline queue for mood saves.
- P31: sign-out does not clear cached pages or the local safety plan; no If-Match / 412.
- P32: disabling TOTP has no password check; regenerating recovery codes has no re-auth; no confirmation on «إنهاء» or on «تسجيل الخروج من كل الأجهزة».
- P34: no confirmation when withdrawing location; the name and number pairing is not enforced; no If-Match.
- P35: no outbox, and Retry only reloads; no return path after login; session-expired and 404 show no emergency numbers; nothing routes to `/maintenance`.
- P36: no automatic move to P07 when matches are ready.
- P37: the safety plan is cached as plaintext in localStorage.
