# Conformance review r1: patient app P01 to P12

Reviewer scope: P01–P12 (patient app). Code: `/home/user/ibrahim/tamaneen/web-patient`, contract `docs/api-v1.md`.
Route renames from `docs/frontend-patient.md` are accepted as documented: `/signup` → `/start/signup`, `/onboarding/consents` → `/start/consents`, `/help-now` → `/crisis`, `/matching` → `/matches`, `/book/[id]` → `/therapists/[id]/book`, `/book/confirmed` → `/appointments/[id]/confirmed`, `/dashboard` → `/home`, `/settings/privacy` → `/privacy`, `/settings/profile` → `/settings`. Endpoint renames that `docs/api-v1.md` documents (for example `POST /v1/crisis/help`, `PUT /v1/me/consents`, `/matches/{id}/dismiss`, `/bookings/holds`, `/intake/{id}/complete`, `/me/contact-request`, `GET /auth/nickname`) are not counted as gaps.

All paths below are relative to `web-patient/src/` unless they start with `backend/`, `docs/` or `messages/`.

---

## X1. Cross-cutting: "مساعدة فورية" on signed-in screens

| severity | what's missing | design ref | code evidence | suggested fix |
|---|---|---|---|---|
| blocker | On signed-in screens (P03–P11), "مساعدة فورية" should show the numbers, then `POST /v1/crisis/help`, then open P06 with a Critical event. The header link always goes to `/crisis-resources` (P14) with no request. Opening an event needs a second tap on "اطلب أن يتصل بك مختص الآن". | P03–P11 buttons: «يعرض أرقام الطوارئ فورًا ثم يرسل الطلب ويفتح حدث أزمة بمستوى Critical (PB-26)… → POST /v1/crisis/help-now ثم /help-now (P06)»; guide.md:1060 PB-26 «Header on every page · Opens crisis screen; opens Critical event» | `components/HelpNowLink.tsx:9` `href="/crisis-resources"` for every page; `components/AppHeader.tsx:16`; the POST exists only in `components/crisis/RequestCallButton.tsx:25` | When there is a session, make the header button a client control that goes to `/crisis` at once (numbers are server-rendered there) and fires `POST /v1/crisis/help`. If the request fails or the device is offline, queue it (EC-04). Keep the P14 link for public pages (PB-02). |

---

## P01 الترحيب (`/`)

✅ Matches:
- The page renders at `/` (`app/[locale]/page.tsx`).
- Header has the brand and "مساعدة فورية", which goes to `/crisis-resources` (P14, PB-02).
- The calm line «خذ نفسًا عميقًا. أنت في المكان الصحيح.» is present.
- Title and promise are present. The hours are a variable, not hard-coded.
- Both trust badges are present.
- «لنبدأ معًا» → `/start/eligibility`. «لدي حساب» → `/login`. A signed-in visitor gets «متابعة» → `/home`.
- The emergency line «في خطر الآن؟ اتصل بـ {number}» is a `tel:` link, with a link to P14 when no number is configured.
- The page is server-rendered.
- The service worker precaches P14 for offline use (`public/sw.js:19-23`).

| severity | what's missing | design ref | code evidence | suggested fix |
|---|---|---|---|---|
| major | No "sign-ups paused" state (EC-26): the start button always shows. | states: «إيقاف التسجيل (EC-26): رسالة «التسجيل الجديد متوقف الآن» مع أرقام الطوارئ بدل زر البدء» | `app/[locale]/page.tsx:47-55` always renders `cta-start`; `PublicConfig` has no pause flag (`lib/api/types.ts:29-38`) | Add a `signupsOpen` (or similar) field to `GET /v1/meta/config`. When it is false, swap the CTA for the pause message and the numbers. |
| major | No maintenance redirect to `/maintenance` (P35). | states: «وضع الصيانة: تحويل إلى /maintenance (P35) مع بقاء أرقام الطوارئ» | `middleware.ts:35-45` only refreshes tokens and handles locale; no maintenance check in `app/[locale]/page.tsx` | Detect maintenance (a config flag or 503) and redirect to `/maintenance`. |
| minor | The emergency number and the "72 hours" value come only from deployment env vars. They don't use the live config with env fallback that P14 and P06 use. | rules: «أرقام الطوارئ… تُقرأ من جدول EmergencyContacts… مع نسخة احتياطية»; element: «الرقم يُقرأ من PlatformSettings» | `app/[locale]/page.tsx:14` `publicPlatformConfig()` (env only) instead of `loadSafetyNumbers()` (`lib/platform-config.ts:64`) | Use `loadSafetyNumbers()` and the `matchWithinHours` from `meta/config`. |
| minor | Copy is bundled in `messages/`, not taken from published ContentItems. The contract has no `/content/{key}`. | api: «GET /content/{key}?lang=» | `messages/ar/welcome.json` | Accept for the pilot, or add a content endpoint to the contract. |

---

## P02 إنشاء الحساب (`/start/signup`)

✅ Matches:
- Back link → `/start/eligibility`.
- «الخطوة 1 من 4» with a 4-part bar.
- Title and subtitle are present.
- The nickname is prefilled, and «اسم جديد» regenerates it.
- The phone/e-mail switch clears the field (`SignupForm.tsx:169-172`).
- The contact input is LTR with the 6-digit hint.
- The password field has show/hide, a 4-segment meter and the «قوية: 12 حرفًا…» message.
- «إنشاء الحساب» validates all fields, shows the busy state, maps field errors under each field, keeps the other inputs, then `POST /v1/auth/signup` → `/start/verify`.
- «تسجيل الدخول» → `/login`.
- The server answers a used contact with a generic response (`backend/.../AuthService.cs:92-99`).
- Header has "مساعدة فورية" → P14.

| severity | what's missing | design ref | code evidence | suggested fix |
|---|---|---|---|---|
| major | No sign-ups paused state (EC-26). | states: «إيقاف التسجيل (EC-26): يُمنع الإنشاء وتُعرض أرقام الطوارئ» | `components/onboarding/SignupForm.tsx` has no pause branch; no flag in `PublicConfig` | Same flag as P01. Block the form and show the numbers. |
| minor | The client does not reject disallowed characters in the nickname; it only checks for empty. The server does reject them (`Nicknames.IsAcceptable`). | buttons «الاسم المستعار»: «يُرفض الفارغ والرموز غير المسموحة» | `SignupForm.tsx:71` | Add an allowed-character check client-side, matching the server. |
| minor | The contact format is checked on submit, not when leaving the field. | «إدخال إلزامي مع تحقق الصيغة عند مغادرة الحقل» | `SignupForm.tsx:68-76` validates in `submit` only | Run the contact check in `onBlur`. |
| minor | The minimum password length is hard-coded at 12, not read from PlatformSettings. | «يُقرأ الحد الأدنى من PlatformSettings ويُوحَّد النص معه» | `components/auth/contact.ts:4` `MIN_PASSWORD_LENGTH = 12`; the copy also hard-codes 12 (`messages/ar/signup.json`) | Put it in `meta/config`, or accept it (the API doc fixes it at 12). |
| minor | No Idempotency-Key on signup. | «مقيّد بـ 3 محاولات في الساعة لكل IP، مع Idempotency-Key» | no `Idempotency` anywhere in `web-patient/src` or `backend/src` (grep) | Send a key per submit if the backend adds support. The busy state already blocks double taps. |

---

## P03 الموافقات (`/start/consents`)

✅ Matches:
- «الخطوة 3 من 4».
- Title «قبل أن نبدأ» with the "change later" note.
- The list comes from `GET /v1/me/consents` (contract).
- Each item has a title, description and a مطلوب/اختياري tag.
- «اقرأ النص كاملًا» expands the text inline, with the version.
- «موافق، متابعة» stays disabled until every required item is ticked (PB-16).
- Saving sends `PUT /v1/me/consents` with every item → `/start/safety-info` (P21).
- A failed save keeps the choices and shows the error.

| severity | what's missing | design ref | code evidence | suggested fix |
|---|---|---|---|---|
| blocker | "مساعدة فورية" does not open P06 or an event. | see X1 | `components/HelpNowLink.tsx:9` | see X1 |
| major | A required consent can be ticked before its full text has been opened (PB-15). | «لا يمكن تحديد بند مطلوب قبل فتح نصه الكامل (PB-15)»; guide.md:1044 «Must open each required consent» | `components/onboarding/ConsentsForm.tsx:62` the checkbox is always enabled; the `open` state (`:72`) is not tied to `disabled` | Disable each required checkbox until `open[c.key]` has been true once, and explain why. |
| minor | The full consent text comes from the bundled messages, not from versioned server content, and opening it is not recorded. | «النص الكامل: يعرض نص ContentItems من نوع Consent»; «يوسّع نص الموافقة ويسجّل أنه فُتح» | `ConsentsForm.tsx:78` `t(\`items.${key}.full\`)`; `Consent` has no text field (`lib/api/types.ts:84`) | Add the text to the consent DTO, or confirm the bundled text matches the version shown. |

---

## P04 محادثة الاستقبال (`/intake`)

✅ Matches:
- Assistant identity and «مساعد ذكي، ويراجع كلامك مختص بشري».
- Progress bar with %.
- Message log.
- The question card shows «PHQ-9 · السؤال 2 من 9», the verbatim item and the 0–3 options with their score.
- Composer: Enter sends, Shift+Enter adds a line, IME-safe.
- Send is disabled while waiting.
- An offline message is kept and auto-sent on reconnect («لا يوجد اتصال الآن…»).
- «إيقاف مؤقت» → `POST pause`, with the 14-day note.
- «أريد التحدث مع شخص» → `POST /v1/me/contact-request` (contract).
- «تخطي السؤال» sends a null answer.
- «إنهاء المحادثة» is enabled only when `canFinish`; it calls `POST complete` → `/intake/summary`.
- Any `crisis: true` → `router.replace('/crisis')`.
- The draft is kept locally.
- The expired state offers a restart.

| severity | what's missing | design ref | code evidence | suggested fix |
|---|---|---|---|---|
| blocker | "مساعدة فورية" does not open P06 or an event. | see X1 | | see X1 |
| major | The message limit is 4,000, not 2,000, and there is no "send it in parts" message (EC-19). | element: «حقل رسالة بحد أقصى 2,000 حرف»; «أكثر من 2,000 حرف يُرفض مع طلب الإرسال على أجزاء (EC-19)»; guide.md:1222 | `components/intake/Composer.tsx:6` `MAX_MESSAGE_LENGTH = 4000`; the API also allows 4000 (`backend/src/Tamaneen.Api/Endpoints/IntakeEndpoints.cs:24`) | Set 2,000 on both sides and show the "send in parts" message when a paste exceeds it. |
| major | «إيقاف مؤقت» does not return to the home screen. It stays on `/intake` with a paused notice and a resume button. | «يحفظ الجلسة ويعود للرئيسية (PB-24) → /dashboard (P09)» | `components/intake/IntakeChat.tsx:204` `apply(...pause)`; no navigation | After a successful pause, `router.push('/home')`. Home then needs the "continue intake" card (see P09). |
| major | «أريد التحدث مع شخص» has no confirmation sheet and does not show the expected wait. | «يعرض ورقة تأكيد أولًا، ثم يفتح حالة فرز بأولوية High ويعرض وقت الانتظار المتوقع (PB-25)» | `IntakeChat.tsx:222` posts at once; `ContactRequestCreated` has only `requestedAt` (`lib/api/types.ts:226`) | Add a confirm dialog. Return an expected-by time from the API and show it. |
| major | There is no «التحويل إلى نموذج» (switch to form) when the AI is down. | states: «تعطل المساعد (EC-10/11/16): شريط وضع النموذج مع زر «التحويل إلى نموذج» (PB-28)» | No form-mode UI in `components/intake/*`; the contract has no form-mode endpoint | Add a form mode (same questions, progress kept) and a banner with the switch button. |
| major | A failed message has no client message id and no «إعادة المحاولة» button. A retry can create a duplicate turn (EC-07). | «يرسل النص بمعرّف رسالة من العميل (PB-21)»; «زر «إعادة المحاولة» بنفس معرّف الرسالة (PB-27)» | `IntakeChat.tsx:133` body `{ text }` only; `SendMessageRequest { text }` (`lib/api/types.ts:130`); non-network errors drop the bubble | Add `clientMessageId` to the contract and the request. Show a retry button on the failed bubble that reuses it. |
| minor | No "reconnecting" banner and no multi-tab read-only handling (EC-03, EC-05). | states EC-03 and EC-05 | no hub or lease code in `components/intake/*` | Add these when the intake hub lands. |
| minor | Skip exists only on question cards; there is no way to skip a chat topic. | «يتخطى الموضوع أو البند (PB-23)» | `IntakeChat.tsx:337` renders skip only when `question` is set | Add a topic skip if the API supports it. |

---

## P05 مراجعة الملخص (`/intake/summary`)

✅ Matches:
- Title and subtitle.
- Four sections (concern, goals, supports, prior care).
- «تعديل» per section turns it into a textarea with save/cancel → `PUT /v1/intake/{id}/summary`.
- Edits go through the crisis gate → `/crisis`.
- The «صحّحته أنت» tag shows on edited sections.
- «أضف شيئًا لم نذكره» adds a free note.
- The review note uses the configured hours.
- «إرسال إلى فريق الرعاية» → `POST submit` → `/waiting` (P36).
- When the AI summary failed, submitting without edits is allowed.
- Status guards redirect to `/intake` or `/waiting`.

| severity | what's missing | design ref | code evidence | suggested fix |
|---|---|---|---|---|
| blocker | "مساعدة فورية" does not open P06 or an event. | see X1 | | see X1 |
| major | Sending has no confirmation step. | «يعرض تأكيدًا «سيراجع مختص ملفك خلال [المهلة]» ثم يقفل الملخص (PB-33)» | `components/intake/SummaryReview.tsx:74-79,203` submits at once | Show a confirm dialog with the SLA text before `submit`. |
| major | The client allows 4,000 characters per section but the API rejects anything over 2,000 with a generic "sections" error. | «أضف شيئًا لم نذكره… حتى 2,000 حرف (مقترح)» | `SummaryReview.tsx:15` `MAX_SECTION_LENGTH = 4000` vs `backend/src/Tamaneen.Api/Endpoints/IntakeEndpoints.cs:229` `> 2000` → invalid | Set `MAX_SECTION_LENGTH = 2000` and show a counter. |
| minor | No If-Match/412 conflict handling (show the current version). The contract has no ETag. | «يرسل مع If-Match؛ تعارض 412 يعرض النسخة الحالية» | `SummaryReview.tsx` PUT has no version | Add a version or ETag to `IntakeSummary` and handle 412. |
| minor | The AI-failure copy differs from the design. | EC-14: «فريقنا يجهّز ملخصك» | `messages/ar/summary.json` `noAiTitle` «اكتب ملخصك بنفسك» | Align the copy, or confirm the deliberate change. |
| minor | No Idempotency-Key on submit. | «الإرسال مزوّد بمفتاح Idempotency» | no `Idempotency` in code (grep) | Add one if the backend supports it. The busy state already prevents double taps. |

---

## P06 شاشة الطوارئ (`/crisis`)

✅ Matches:
- «أنت لست وحدك» and the reassurance line.
- Steps «تم إبلاغ الفريق» → «مختص يتصل بك», with «على الرقم المنتهي بـ ####» (last 4 only, hidden when there is none).
- The status polls `GET /v1/crisis/status` every 20 s, with an acknowledged notice.
- The script from the API is shown.
- «اتصل بالطوارئ» and «خط الدعم النفسي» are server-rendered `tel:` links (PB-03).
- When no number is set, there is a link to P14.
- The "until someone calls" guidance and «المحادثة متوقفة الآن…» are shown.
- «رقمي تغيّر؟ حدّث رقم التواصل» → `/settings/safety` (P34).
- `CrisisLockGuard` keeps a locked patient on `/crisis` (plus P34 and P37).
- No bottom nav.

| severity | what's missing | design ref | code evidence | suggested fix |
|---|---|---|---|---|
| blocker | The header "مساعدة فورية" never reaches this screen and never opens an event. | entry: «زر «مساعدة فورية» في أي شاشة»; PB-26 | `components/HelpNowLink.tsx:9` | see X1 |
| major | The help request is not queued offline. A network failure just shows an error. | states: «بلا اتصال (EC-04): تُعرض الأرقام من ذاكرة PWA فورًا ويُصفّ طلب help-now ويُرسل عند العودة» | `components/crisis/RequestCallButton.tsx:25-29` | Queue `POST crisis/help` (localStorage plus an online listener, or Background Sync) and say «سنرسل طلبك عند عودة الاتصال». |
| major | No out-of-region fallback (EC-37): 112/911 is not shown on P06; only P14 has 112. | states: «خارج منطقة التجربة (EC-37): عرض 112/911 والموارد المحلية» | `app/[locale]/(app)/crisis/page.tsx` has no 112 line (P14 has it at `(public)/crisis-resources/page.tsx` `INTERNATIONAL_EMERGENCY`) | Add the same «في الخارج؟ 112» line to P06. |
| minor | «المحادثة متوقفة الآن…» shows even when no event is open or the chat is not locked. | element «تنبيه القفل» | `app/[locale]/(app)/crisis/page.tsx:72` is rendered unconditionally | Render it only when `status.locked`. |

---

## P07 الأخصائيون المقترحون (`/matches`)

✅ Matches:
- Title «اخترنا لك ثلاثة أخصائيين» (with count variants) and subtitle.
- Cards show initials, short name, title, «مرخصة», «الأنسب» for rank 1, specialty tags and the «لماذا اقترحناها» reason.
- The third suggestion is a compact row with «عرض» → `/therapists/[id]`.
- «عرض الملف والمواعيد» → `/therapists/[id]` (P25).
- «غير مناسب» → `POST /v1/matches/{id}/dismiss`.
- «تصفح جميع الأخصائيين» → `/therapists`.
- The pending state links to `/waiting` (P36).
- URLs carry only the id.

| severity | what's missing | design ref | code evidence | suggested fix |
|---|---|---|---|---|
| blocker | "مساعدة فورية" does not open P06 or an event. | see X1 | | see X1 |
| major | «غير مناسب» removes the suggestion at once: no confirmation sheet and no optional reason. | «ورقة تأكيد مع سبب اختياري، ثم يزيل الاقتراح دون أي عقوبة (PB-36)» | `components/matching/MatchSuggestions.tsx:53,135`; the dismiss endpoint takes no body (`docs/api-v1.md:72`) | Add a confirm dialog with an optional reason, and add `{ reason? }` to the contract. |
| minor | After all three are declined, the empty state doesn't say the supervisor will suggest alternatives. | states: «رفض الثلاثة: رسالة أن المشرف سيقترح بدائل، مع رابط التصفح» | `MatchSuggestions.tsx:74`; `messages/ar/matching.json` `emptyBody` «يمكنك تصفح جميع الأخصائيين…» | Change the copy to mention alternatives from the supervisor. |

---

## P08 حجز موعد (`/therapists/[id]/book`)

✅ Matches:
- Back link → `/therapists/[id]` (or to the appointment in reschedule mode).
- Therapist card «جلسة عن بُعد · {50} دقيقة · مجانية في التجربة».
- Day picker from `GET slots?days=14`, then time buttons.
- Choosing a time calls `POST /v1/bookings/holds`; the previous hold is released.
- The countdown «حجزنا لك … لمدة m:ss دقيقة» is computed from the server's `expiresAt`.
- On expiry the confirm button is disabled with a message.
- A 409 reloads the slots («حدّثنا الأوقات المتاحة»).
- The reminders note is shown.
- «تأكيد الحجز» → `POST holds/{id}/confirm` → `/appointments/[id]/confirmed` (P26). Double taps are guarded.
- The no-slots state links to the directory.
- The not-eligible state links to `/start/path`.

| severity | what's missing | design ref | code evidence | suggested fix |
|---|---|---|---|---|
| blocker | "مساعدة فورية" does not open P06 or an event. | see X1 | | see X1 |
| minor | The countdown subtracts the device clock from `expiresAt`, so a skewed clock shows the wrong time (EC-09). | rules: «العدّاد يعتمد على وقت الخادم لا ساعة الجهاز (EC-09)» | `components/booking/slots.ts:25` `now = Date.now()` | Compute an offset from the `Date` response header (or a `serverNow` field) when the hold is created. |
| minor | Booked times are not shown disabled; only free slots are listed. | «المحجوز يظهر معطّلًا» | `Slot { slotId, startsAt }` has no availability flag (`lib/api/types.ts:165`) | Visual only. Accept, or add `available` to `Slot`. |
| minor | No explicit Idempotency-Key on confirm. The hold id makes repeats mostly safe. | «يؤكد الحجز بمفتاح Idempotency (PB-41)» | `components/booking/BookingFlow.tsx:171` | Optional: send an `Idempotency-Key`. |

---

## P09 الرئيسية (`/home`)

✅ Matches:
- Greeting by time zone and the nickname.
- «كيف تشعر الآن؟»: five faces → `POST /v1/mood` with 0/3/5/7/10; a crisis response goes to `/crisis`.
- «يُحفظ كرقم من 0 إلى 10 · أضف ملاحظة إن أحببت» links to `/check-in`.
- Next-session card (date, «جلستك مع …», relative time) with «التفاصيل» → `/appointments/[id]`.
- When there is no session, a card links to `/therapists`.
- Latest-message card.
- Homework tile → `/homework/[id]` with «3 من 7 أيام».
- «خطة أماني» tile («تعمل بلا إنترنت») → `/safety-plan`.
- Triage pending → `/waiting`; suggestions ready → `/matches`.
- Bottom nav: home, appointments, homework, settings.

| severity | what's missing | design ref | code evidence | suggested fix |
|---|---|---|---|---|
| blocker | "مساعدة فورية" does not open P06 or an event. | see X1 | | see X1 |
| major | No «متابعة الاستقبال» card while an intake is open (PB-46). A patient who paused the intake has no way back to it from home. | states: «استقبال مفتوح: بطاقة «متابعة الاستقبال» (PB-46) بدل الجلسة»; P04 entry «أو «متابعة الاستقبال» في الرئيسية» | `components/home/HomeView.tsx:40-85` branches only on triage; `Me.onboarding.intakeStatus` is available (`lib/api/types.ts:76`) | When `intakeStatus` is active or paused, render a card → `/intake` in place of the session card. |
| major | No "open meeting link" button 10 minutes before the session (PB-43). The card only has «التفاصيل». | element: «يظهر زر فتح الرابط قبل الموعد بعشر دقائق (PB-43)»; guide.md:1082 (`/dashboard`) | `HomeView.tsx:74` only the details link; `components/appointments/JoinSessionCard.tsx` exists but is not used on home | Reuse `JoinSessionCard`, or show the join button when `startsAt - now ≤ 10 min`. |
| minor | The last tab is «الإعدادات» (→ `/settings`), not «المزيد». The «المحادثات» tab is omitted; P39 is out of scope, so that part is not counted. | «الرئيسية، المحادثات، المواعيد، الواجبات، المزيد» | `components/ui/BottomNav.tsx:93-98`; `messages/ar/nav.json` | Copy only. Accept, or rename to «المزيد». |
| minor | The unread dot on the latest message is not shown. The `newFeedback` key exists but is unused. | «معاينة آخر رسالة من الأخصائية مع نقطة غير مقروء» | `HomeView.tsx:91-98`; `messages/ar/home.json` `newFeedback` | Add an unread flag to `latestFeedback` and render a dot. |

Not counted: the notifications link (P46) and the message link (P40/P39) are out of the reviewed set or out of scope.

---

## P10 الواجب المنزلي (`/homework/[id]`)

✅ Matches:
- Back link → `/homework` (P29).
- Title, plus «من {therapist} · {schedule}».
- Numbered instructions.
- «كيف كان اليوم؟» with أنجزته / جزئيًا / ليس اليوم; a choice is required, with an inline error.
- "Not today" shows no warning or streak loss.
- Slider 0–10 with its value.
- Optional note.
- Latest therapist feedback.
- «حفظ المتابعة» → `POST /v1/homework/{id}/checkins`; a crisis response goes to `/crisis`.
- Prefilled from today's check-in.

| severity | what's missing | design ref | code evidence | suggested fix |
|---|---|---|---|---|
| blocker | "مساعدة فورية" does not open P06 or an event. | see X1 | | see X1 |
| major | An offline check-in is not queued. It only shows a network error. | «بلا اتصال تُصفّ وتُرسل عند العودة»; states «بلا اتصال: «سنرسلها عند عودة الاتصال»» | `components/homework/HomeworkCheckinForm.tsx:58` | Queue the body on network failure (as intake does) and show the queued message. |
| major | A completed or cancelled task is not read-only; the form is always shown. | states: «المهمة ملغاة أو منتهية: للقراءة فقط» | `components/homework/HomeworkDetailView.tsx:32` always renders the form; `HomeworkItem.completed` exists (`lib/api/types.ts:207`) | When `completed` is true (or the task has ended), render the check-in as read-only. |
| minor | There is no explicit "mark feedback read" call. It depends on the server marking it read on GET, and the contract has no endpoint for it. | «يُعلَّم مقروءًا تلقائيًا عند عرضه (PB-51)» | no call in `components/homework/*` | Confirm the server marks it read on GET, or add the call. |

Not counted: the note limit of 1,000 against the design's 2,000, which is marked (مقترح).

---

## P11 الخصوصية (`/privacy`)

✅ Matches:
- Back link → `/settings` (P31).
- Title «الخصوصية وبياناتي».
- «موافقاتي» switches → `PUT /v1/me/consents`.
- Research and location switches work.
- «من اطّلع على ملفي» shows role, action and time with no staff names; the latest 3, with «السجل كاملًا» to expand.
- Empty state «لم يطّلع أحد على ملفك بعد».
- «تنزيل نسخة من بياناتي» shows the request status in place of the button once requested.
- «حذف حسابي» asks for the password in a dialog that explains what is kept.

| severity | what's missing | design ref | code evidence | suggested fix |
|---|---|---|---|---|
| blocker | Deleting the account is not blocked while a crisis event is open. The API closes the account and crypto-shreds the data at once. | «يُمنع ما دام هناك حدث أزمة مفتوح (EC-55)»; states «حذف ممنوع لوجود حدث مفتوح: رسالة توضيحية»; guide.md:1273 | `backend/src/Tamaneen.Api/Endpoints/MeEndpoints.cs:513-524` has no open-event check; `components/privacy/MyData.tsx:79` | Reject with a ProblemDetails code while a crisis event is open, and show the message. (The 30-day pipeline is a further backend gap.) |
| blocker | "مساعدة فورية" does not open P06 or an event. | see X1 | | see X1 |
| major | The AI-assistance switch cannot be withdrawn. Required consents are rendered locked and on, so the withdrawal dialog never opens. The catalog marks AI as `Required: true, Withdrawable: true`. | «المساعدة بالذكاء الاصطناعي · مفتاح تبديل: السحب يعرض نافذة تشرح الأثر… ويُعرض خيار مسار الحذف (PB-56)» | `components/privacy/ConsentsSection.tsx:59` `locked = consent.required \|\| …`, `:70` `checked={consent.required ? true : …}`; dialog at `:45` unreachable; `backend/src/Tamaneen.Identity/Consent.cs:20` | Lock only when `!withdrawable`. Let AI be switched off through the dialog, and offer the delete path in it. |
| major | Requesting the export does not ask the patient to sign in again. | «يطلب إعادة المصادقة ثم ينشئ طلب تصدير (PB-58)» | `MyData.tsx:54` posts directly | Ask for the password (as delete does) before `POST me/data-export`. |
| major | Account deletion has no typed confirmation; it asks only for the password. | «إعادة مصادقة ثم كتابة تأكيد (PB-59)» | `MyData.tsx:69-79` | Add a "type حذف to confirm" field. |
| major | Withdrawing the emergency-location consent has no confirmation. | «موقعي عند الطوارئ: يمنح أو يسحب… مع تأكيد عند السحب» | `ConsentsSection.tsx:45` opens a dialog only for `ai` | Add a confirm dialog when `emergency_location` goes from on to off. |
| minor | Platform and sharing consents are also listed as locked rows; the design shows three switches. | element «موافقاتي: … المساعدة بالذكاء الاصطناعي، تحسين الخدمة، موقعي عند الطوارئ» | `ConsentsSection.tsx` renders every consent | Visual. Accept, or hide the consents that can't be withdrawn. |

---

## P12 كيف تعمل الخدمة (`/how-it-works`)

✅ Matches:
- Public, static, server-rendered page.
- Back link → `/`.
- Header "مساعدة فورية" → P14.
- Four numbered steps with the mockup copy.
- «ما يفعله الذكاء الاصطناعي وما لا يفعله» with the يفعل / لا يفعل lists.
- «ابدأ الآن» → `/start/eligibility` (P15).

| severity | what's missing | design ref | code evidence | suggested fix |
|---|---|---|---|---|
| major | No page in the app links to `/how-it-works`, so it is reachable only by typing the URL. | entry: «من الصفحة الرئيسية العامة أو روابط التذييل» | `grep -rn how-it-works web-patient/src` finds only the page itself | Add a link on P01 (and in a public footer if there is one). |
| major | No sign-ups paused state (EC-26). | states: «إيقاف التسجيل (EC-26): زر البدء يُستبدل برسالة الإيقاف وأرقام الطوارئ» | `app/[locale]/(public)/how-it-works/page.tsx:69` always renders the CTA | Same flag as P01. |
| minor | "24 ساعة" is hard-coded in the copy, not read from the SLA. | rules: «النصوص ومهلة الـ 24 ساعة تُقرأ من ContentItems و SlaPolicies وليست ثابتة» | `messages/ar/howItWorks.json:16` | Pass `triageWithinHours` from `loadSafetyNumbers()` as a placeholder. |

---

## Summary

| severity | count |
|---|---|
| blocker | 2 distinct: X1 (listed on every signed-in screen) and the P11 deletion block |
| major | 24 |
| minor | 22 |

### Blockers
1. **X1 / P03–P11**: on signed-in screens, "مساعدة فورية" goes to `/crisis-resources` and does not `POST /v1/crisis/help` or open P06 (`components/HelpNowLink.tsx:9`, PB-26).
2. **P11**: the account can be deleted and crypto-shredded while a crisis event is open (EC-55) (`backend/src/Tamaneen.Api/Endpoints/MeEndpoints.cs:513-524`).

### Majors
- P01: no sign-ups paused state (EC-26); no maintenance redirect.
- P02: no sign-ups paused state.
- P03: a required consent can be ticked without opening its full text (PB-15).
- P04: 4,000-character limit instead of 2,000; Pause does not return to home; "talk to a person" has no confirmation or wait time; no switch to form; no client message id or retry button.
- P05: no confirmation before submit; section limit 4,000 on the client but 2,000 on the server.
- P06: the help request is not queued offline (EC-04); no 112/911 out-of-region fallback (EC-37).
- P07: "not a fit" has no confirmation sheet or optional reason.
- P09: no "continue intake" card; no join-session button 10 minutes before.
- P10: offline check-in is not queued; ended or cancelled tasks are not read-only.
- P11: AI consent cannot be withdrawn (locked); export has no re-auth; delete has no typed confirmation; no confirmation when withdrawing location.
- P12: no link to the page from anywhere; no sign-ups paused state.
