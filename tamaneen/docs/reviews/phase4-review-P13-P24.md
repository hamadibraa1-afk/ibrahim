# Conformance review r2: patient app P13–P24

Scope: P13, P14, P15, P16, P17, P18, P19, P20, P21, P22, P23, P24. I checked these by reading the code against `spec/g1.json`, `phase4/screens.txt` and `guide.md`. Route renames listed in `docs/frontend-patient.md` are not counted as gaps. Paths are relative to `/home/user/ibrahim/tamaneen/web-patient/` unless stated.

## Cross-cutting issue X1: the header's «مساعدة فورية» on P17 and P21–P24 does not open a crisis event

The spec for P17, P21, P22, P23 and P24 says: «يعرض أرقام الطوارئ فورًا ثم يرسل الطلب ويفتح حدث أزمة بمستوى Critical (PB-26)… POST /v1/crisis/help-now ثم /help-now (P06)». The guide says the same at PB-26 (guide.md:1060): "Header on every page | Opens crisis screen; opens Critical event".

In the code there is a single header link for every screen, `src/components/HelpNowLink.tsx:9` `href="/crisis-resources"`, rendered from `src/components/AppHeader.tsx:16`. It never calls `POST /v1/crisis/help` and never opens `/crisis` (P06). A signed-in patient has to tap a second button on P14 (`(public)/crisis-resources/page.tsx:53`, RequestCallButton) before an event opens. The public screens (P13–P16, P18–P20) correctly expect `/crisis-resources`.

| severity | what's missing | design ref | code evidence | suggested fix |
|---|---|---|---|---|
| blocker | On signed-in screens, Help Now does not open a Critical crisis event or go to P06 | P17/P21/P22/P23/P24 buttons «مساعدة فورية»; PB-26 | HelpNowLink.tsx:9; AppHeader.tsx:16 | Pass the session state into HelpNowLink. When signed in, render the crisis numbers or go to `/crisis` at once, then fire `POST /v1/crisis/help`, queuing it if offline (EC-04). Keep `/crisis-resources` for anonymous visitors. |

---

## P13 Privacy and terms: `/legal` (documented rename of `/privacy`, `/terms`)

✅ The page exists at `(public)/legal/page.tsx`. It has privacy and terms tabs (`?doc=`, lines 36–47), an English link that switches locale and direction (line 51), and the version line. The five summary sections match the mockup copy, including the 90-day deletion. The legal-text placeholder note is there (line 68), and Help Now is in the header.

| severity | what's missing | design ref | code evidence | suggested fix |
|---|---|---|---|---|
| major | No screen links to P13. The spec's entry points (footer, consents P03) have no link, so the page is reachable only by typing the URL. | entry: «روابط التذييل، أو من شاشة الموافقات» | `grep -rn legal src` finds no Link outside the page itself; ConsentsForm.tsx and `[locale]/page.tsx` have no link | Add links to `/legal?doc=privacy` and `/legal?doc=terms` in the P03 consent rows and on the welcome screen or a footer. |
| major | «رجوع» always goes to `/`, not to the previous screen. Opening it from P03 would drop the patient on Welcome. | «يعود إلى الشاشة السابقة دون فقد ما حُفظ» | legal/page.tsx:34 `<BackLink href="/" />` | Use `router.back()` with `/` as the fallback, or pass a `from` parameter. |
| minor | The version is hard-coded and the publish date is missing. The design shows «الإصدار 1.0 · [تاريخ النشر]» from ConsentVersions. The contract has no `GET /v1/consents/current`, so the loading, error and cached states also don't apply. | elements «العنوان والإصدار»; api | legal/page.tsx:16 `LEGAL_VERSION = '1.0'`, :55 | Add the publish date. Raise the missing endpoint as a contract gap. |
| minor | The retention text is static and not read from PlatformSettings. | rules «مدة الحفظ تُقرأ من PlatformSettings» | messages/ar/legal.json `retention.body` | Read it from config once the contract exposes it. |

## P14 Crisis resources: `/crisis-resources`

✅ Public page with an offline badge. The emergency and support-line `tel:` links come from config with an env fallback, not hard-coded (`lib/platform-config.ts`). The small steps, «تساعد شخصًا آخر؟» and the 112 line all match the mockup copy. The service worker precaches the page (`public/sw.js:19-23`). There is no analytics and no app lock. The nearest-hospital list is out of scope.

| severity | what's missing | design ref | code evidence | suggested fix |
|---|---|---|---|---|
| minor | «رجوع» goes to a fixed `/home` or `/`, not the previous screen | «يعود إلى الشاشة السابقة» | crisis-resources/page.tsx:23 | Use history back with that fallback. |
| minor | The labels show «الطوارئ:» followed by the number. The design shows «الطوارئ: [رقم]». This is equivalent. | buttons | crisisResources.json `emergency` | None needed. |

## P15 Eligibility: `/start/eligibility`

✅ «رجوع» goes to `/`. The age question has yes/no, the city select is filled from `meta/config` pilotCities, and the "if not eligible" card is there. Continue goes to `POST /v1/auth/eligibility` (the contract endpoint) and then to `/start/signup`. The not-eligible screen shows the emergency numbers and creates no account. A failed city fetch offers a retry. The copy matches.

| severity | what's missing | design ref | code evidence | suggested fix |
|---|---|---|---|---|
| minor | «متابعة» is not disabled until both answers are given. The code checks on submit and shows inline errors instead. | «معطّل حتى الإجابة على السؤالين» | EligibilityForm.tsx:44-45, :176 | Add `disabled={isAdult === null \|\| !cityCode}`, or accept the current pattern. |
| minor | There is no dedicated "sign-ups paused" (EC-26) state. It relies on the API's problem text. | states «إيقاف التسجيل (EC-26)» | EligibilityForm.tsx:64 (generic problemText) | Map the problem code to a paused message with crisis numbers. |

## P16 Verify: `/start/verify` (documented rename)

✅ The step indicator reads 2 of 4. The masked destination is shown, the code field is LTR with `one-time-code`, digits only and a maximum of 6. The resend countdown comes from the server's `resendAfterSeconds`, and the resend button stays disabled while it runs, with 429 handling. «تغيير رقم الجوال» goes to `/start/signup` and the draft is kept in sessionStorage. «تحقق» calls `signup-verify` and then `/start/recovery-codes`. Back goes to `/start/signup`.

| severity | what's missing | design ref | code evidence | suggested fix |
|---|---|---|---|---|
| minor | The code is one text field, not 6 separate boxes. Paste and autofill still work. | «6 خانات رقمية LTR، الانتقال التلقائي» | VerifyForm.tsx:65-81 | This is visual only. Optionally build a 6-box OTP input. |
| minor | «تحقق» is not disabled until 6 digits are entered. It validates on submit. | «معطّل حتى اكتمال 6 أرقام» | VerifyForm.tsx:41-44, :103 | Add `disabled={code.length !== 6}`. |
| minor | The copy says «إلى الرقم {masked}». The design says «إلى الرقم المنتهي بـ 4521». | mockup text | messages/ar/verify.json `sentPhone` | Align the wording. |

## P17 Recovery codes: `/start/recovery-codes` (documented rename)

✅ The 10 codes are shown in an LTR monospace grid with the mockup copy. «تنزيل كملف» and «نسخ» both give feedback. The authenticator setup is optional (passkey is out of scope). The "I saved the codes" checkbox exists and «متابعة» goes to `/start/consents`. Regenerating the codes after they are lost is handled.

| severity | what's missing | design ref | code evidence | suggested fix |
|---|---|---|---|---|
| blocker | Help Now does not open a crisis event (see X1) | «مساعدة فورية» → P06 | HelpNowLink.tsx:9 | See X1. |
| major | «متابعة» is enabled by the checkbox alone. It does not also need a download or a copy. | «مفعّل فقط بعد التنزيل أو النسخ وتحديد الإقرار»; PB-09 (guide.md:1038) | RecoveryCodesPanel.tsx:140 `disabled={!saved}` | Use `disabled={!saved \|\| status === null \|\| status === 'copyFailed'}`. |
| major | The downloaded file contains more than the codes: a title with the app name, a body line and a date. | «ينزّل ملف .txt بالرموز فقط دون أي بيانات تعريفية (PB-09)» | RecoveryCodesPanel.tsx:76-77 | Write only the codes, one per line. |
| major | There is no warning when leaving before saving. A reload shows the codes again from sessionStorage, when the spec says they should not be shown again. | states «مغادرة الصفحة قبل الحفظ: تحذير تأكيد»، «إعادة التحميل: لا تُعرض الرموز مجددًا» | RecoveryCodesPanel.tsx:43 (readFlow on mount); flowStorage.ts:44 (sessionStorage) | Add a `beforeunload` guard while not confirmed. Read the codes once and clear the key straight away, keeping them in memory only. |
| minor | The authenticator setup shows the secret and an otpauth link but no QR code | «يعرض رمز QR لتطبيق المصادقة» | TotpSetup.tsx:19 ("No QR library") | Render a QR code, for example as an inline SVG. |

## P18 Login: `/login`

✅ «أهلًا بعودتك» is shown. The contact field is LTR and the password field is hidden. «نسيت كلمة المرور؟» goes to `/recover`. The error box has `role=alert` with a generic message and the remaining attempts. The 423 lockout message is there. MFA goes to `/login/mfa`, and «ابدأ الآن» goes to `/start/eligibility`. The button is busy while the request runs. Tokens are kept in BFF cookies. Passkey is out of scope.

| severity | what's missing | design ref | code evidence | suggested fix |
|---|---|---|---|---|
| major | The contact field only checks that it is not empty. There is no format check, although `isValidContact()` exists. | «رقم الجوال أو البريد: إلزامي مع تحقق الصيغة» | LoginForm.tsx:37 `if (!contact.trim())` | Use `isValidContact(contact)` as RecoverForm.tsx:53 does. |
| major | After login the patient lands on the onboarding or home page, not the last route they were on | «ثم /login/mfa (P19) أو آخر مسار» | LoginForm.tsx:57 → finishSignIn.ts `landingPath(me)`; no `next` or returnTo parameter anywhere in src | Carry `?next=` from session-expired and middleware redirects, validate it as a same-origin path, and use it after login. |
| major | No CAPTCHA after 3 failures | «CAPTCHA بعد 3 (EC-39)»; guide.md:1252 | `grep -ri captcha` finds nothing in web-patient or backend | Add a CAPTCHA challenge. The contract has no field for it yet, so this is a contract gap. |
| minor | The locked-account message has no link to recovery. The «نسيت كلمة المرور؟» link stays on the page above it. | «حساب مقفل: «حاول بعد 15 دقيقة» مع رابط الاسترجاع» | LoginForm.tsx:60-63 | Add a recover link inside the locked notice. |
| minor | Not-activated account → P16 and new consent version → P59 redirects are not handled. The contract has no codes for them. | states | LoginForm.tsx:65-75 | Raise as a contract gap. |

## P19 Login MFA: `/login/mfa`

✅ «تأكيد الدخول» is shown. The code field is LTR with `one-time-code` and exactly 6 digits. «تذكر هذا الجهاز 30 يومًا» works through the BFF device cookie (`api/auth/[action]/route.ts:94`). «تأكيد» calls `login-mfa` and then the landing page. Under «طرق أخرى» there is SMS with the masked phone number and a recovery code in XXXX-XXXX form. Back goes to `/login`, and a lost MFA state goes to `/login?restart=1`.

| severity | what's missing | design ref | code evidence | suggested fix |
|---|---|---|---|---|
| major | After an SMS is sent there is no 60-second resend countdown and no resend option. In SMS mode the SMS entry is filtered out of the other-methods list. | «إرسال رمز برسالة نصية: … مع عدّاد إعادة إرسال 60 ثانية» | MfaForm.tsx:52-64, :98 (`filter((m) => m !== method …)`) | Show a ResendCountdown, or an equivalent timer, after `auth/login/mfa/sms`. |
| minor | When the password step expires on the server, the code shows an error instead of returning to P18 | «انتهاء خطوة كلمة المرور: العودة إلى P18» | MfaForm.tsx:79-82 | On an expired-token problem code, clear the flow and replace with `/login?restart=1`. |
| minor | The design keeps the masked number visible inside «إرسال رمز برسالة نصية ••21». Here it shows only when SMS is an alternative method. | mockup | MfaForm.tsx:172-176 | This is fine as is. |

## P20 Recover: `/recover`

✅ «رجوع» goes to `/login`. Both methods are radio choices with the mockup copy. The contact field has a format check. The same neutral message is shown whether or not the account exists. «إرسال الرمز» calls `auth/recover` and then the code plus new-password step, with a resend countdown and `recover/complete`. The recovery-code path uses `recover/code`. Success goes to `/login?reset=1`.

| severity | what's missing | design ref | code evidence | suggested fix |
|---|---|---|---|---|
| minor | «اطلب مساعدة الفريق» is plain text, not a link. The spec marks the route and API as «(مقترح)», but the guide's EC-41 (guide.md:1254) expects an assisted reset. | button «اطلب مساعدة الفريق» | RecoverForm.tsx:127-131 | Once the endpoint exists, link it, or at least point to support contact details. |

## P21 Safety info: `/start/safety-info` (documented rename)

✅ «الخطوة 4 من 4 · اختياري» is shown, with the explanation text. The three fields are there, the phones are LTR with a format check, and the location switch has its hint. «حفظ والمتابعة» calls `PUT me/safety-info` and then `/start/path`. «تخطٍّ الآن» goes to `/start/path`. A failed save keeps the input and shows the error.

| severity | what's missing | design ref | code evidence | suggested fix |
|---|---|---|---|---|
| blocker | Help Now does not open a crisis event (see X1) | «مساعدة فورية» → P06 | HelpNowLink.tsx:9 | See X1. |
| major | The paired check only works one way. A phone without a name is an error, but a name without a phone is accepted. | «إذا أُدخل اسم الجهة يلزم رقمها والعكس» | SafetyInfoForm.tsx:48-50 | Add `if (trustedName.trim() && !trustedPhone.trim()) next.trustedPhone = …`. |
| major | «تخطٍّ الآن» sends `PUT` with every field null. If the patient had safety data already, it is erased, when the spec says skip continues without saving. | «يتابع دون حفظ» | SafetyInfoForm.tsx:18 `EMPTY`, :81 `put(EMPTY,'skip')` | When `initial` has data, skip should navigate without a PUT. Otherwise add a contract flag to mark the step done. |
| minor | The «لماذا يساعد هذا» note next to Skip is missing | «مع ملاحظة «لماذا يساعد هذا»» (PB-18) | SafetyInfoForm.tsx:81-83 | Add a short note. |
| minor | The trusted-name field is limited to 60 characters. The spec says 100. | «حتى 100 حرف» | SafetyInfoForm.tsx:69 `maxLength={60}` | Change it to 100. |

## P22 Choose path: `/start/path` (documented rename)

✅ «كيف تحب أن تبدأ؟» is shown. The intake card has the «ننصح به» tag and the 14-day note, and goes to `/intake`. The direct card goes to `/start/safety-check`. Both save the choice through `PUT me/path`. «اطلب التواصل مع الفريق» calls `POST me/contact-request`, which is the contract endpoint. The copy matches the mockup.

| severity | what's missing | design ref | code evidence | suggested fix |
|---|---|---|---|---|
| blocker | Help Now does not open a crisis event (see X1) | «مساعدة فورية» → P06 | HelpNowLink.tsx:9 | See X1. |
| major | Requesting the team has no confirmation sheet and does not show the expected waiting time. It posts as soon as the link is tapped. | «ورقة تأكيد ثم يفتح حالة فرز بأولوية High ويعرض وقت الانتظار المتوقع» | ChoosePath.tsx:35-46, contactSent message | Add a confirm dialog. Show the wait time from `triageWithinHours` in meta/config, or add it to `ContactRequestCreated`. |
| minor | When an intake is already open, the card does not say «متابعة» | states «استقبال مفتوح سابقًا: بطاقة المحادثة تعرض «متابعة»» | path/page.tsx:21 passes only `me.onboarding.path`; ChoosePath.tsx:48-63 | Pass `intakeStatus` in and switch the label for active or paused intakes. |
| minor | There is no specific handling for paused intakes (EC-26). It falls back to the generic error. | states | ChoosePath.tsx:30-32 | Map the problem code to a paused message with crisis numbers. |

## P23 Safety check: `/start/safety-check` (documented rename)

✅ The questions come from `GET /v1/screening/cssrs`. The progress label reads «أسئلة السلامة · n من 6» with a bar. The intro, the «C-SSRS · فحص سريع» eyebrow, the API note (the translation placeholder) and the emergency line with a `tel:` link all match. The yes/no buttons have no skip. `crisis: true` goes to `/crisis` with replace, and otherwise the patient goes to `/therapists`. A failed send keeps the answers and offers a retry.

| severity | what's missing | design ref | code evidence | suggested fix |
|---|---|---|---|---|
| blocker | Risk is not escalated at the answer that triggers it. All 6 answers are sent only after the last question. A «نعم» on Q4–Q6 (Critical) or Q1–Q3 (High) does not open an event or show P06 at that point. A patient who leaves after answering «نعم» never triggers the gate. A «السؤال السابق» button, which is not in the design, also lets the patient change a «نعم» before anything is sent. | «نعم / لا: يسجل الإجابة… نعم في Q4 أو Q5 أو Q6 يفتح Critical، ونعم في Q1–Q3 يفتح High، وتظهر شاشة الأزمة» | SafetyCheck.tsx:59-67 (submit only at `index === total-1`), :42; backend `IntakeEndpoints.cs:90` requires the full answer count; previous button SafetyCheck.tsx:101-108 | On any «نعم», submit at once: either extend the contract so `POST /v1/screening/cssrs` accepts partial answers or a per-item call, or open `POST /v1/crisis/help` and go to `/crisis`. Remove the previous button, or block changing a «نعم». |
| blocker | Help Now does not open a crisis event (see X1) | «مساعدة فورية» → P06 | HelpNowLink.tsx:9 | See X1. |

## P24 Therapists: `/therapists`

✅ «جميع الأخصائيين» is shown. The search field has the mockup placeholder and a debounced query, and `q` is passed to the API. The chips (القلق، الاكتئاب، العربية، أخصائية، مسائي) toggle specialty, language, gender and evening through the query string, with no health data in it. «مسح» resets everything. The count uses plural forms. Each row shows initials, name, specialties and the next slot, and links to `/therapists/[id]`. There is an empty state. P23 and P07 both link here.

| severity | what's missing | design ref | code evidence | suggested fix |
|---|---|---|---|---|
| blocker | Help Now does not open a crisis event (see X1) | «مساعدة فورية» → P06 | HelpNowLink.tsx:9 | See X1. |
| minor | The empty state has no clear button and no waitlist link. The clear chip above it is visible only while filters are active. The copy differs: «لا يوجد أخصائيون مطابقون» against the design's «لا يوجد أخصائي بهذه الفلاتر». | states «لا نتائج: … مع زر مسح وقائمة الانتظار» | TherapistDirectory.tsx:136; therapists.json `emptyTitle` | Pass an `action` with a clear button and a waitlist link (P56) to EmptyState, and align the copy. |
| minor | «مسح» is hidden when no filter is set. The mockup always shows it. | mockup chips row | TherapistDirectory.tsx:94 | This is visual only. |
| minor | «رجوع» always goes to `/home` | «يعود إلى الشاشة السابقة» | therapists/page.tsx:27 | Use history back with that fallback. |
| minor | There is no skeleton loading for rows and no cursor pagination. The contract returns a plain array. The 350 ms debounce and missing 50-character limit are in «(مقترح)» items, so they are not counted. | states | TherapistDirectory.tsx:17, :161 | Add skeleton rows. Treat pagination as a contract gap. |

---

## Summary

| severity | count |
|---|---|
| blocker | 2 distinct (X1 Help Now, P23 per-answer escalation). X1 appears on P17, P21, P22, P23 and P24. |
| major | 12 |
| minor | 23 |

**Blockers**
1. X1, on P17, P21, P22, P23 and P24: the header «مساعدة فورية» only links to `/crisis-resources`. It never calls `POST /v1/crisis/help` and never opens P06, so PB-26 is not met (HelpNowLink.tsx:9).
2. P23: C-SSRS answers are sent only after Q6. A «نعم» does not escalate at once, leaving the page after a «نعم» opens no event, and the previous button lets the patient change an answer (SafetyCheck.tsx:59-67, 101-108).

**Majors**
- P13: no screen links to /legal (consents or footer).
- P13: Back always goes to `/`.
- P17: Continue does not need a download or copy (PB-09).
- P17: the downloaded file includes a title, the app name and a date.
- P17: no leave warning, and the codes show again on reload.
- P18: no format check on the contact field.
- P18: no return to the last route after login.
- P18: no CAPTCHA after 3 failures (EC-39).
- P19: no SMS resend countdown or resend option.
- P21: a trusted name is accepted without a phone.
- P21: Skip erases saved safety data with an all-null PUT.
- P22: no confirmation sheet or wait time for the team request.
