# المواصفة المشتركة: نظام الدخول المزدوج + التطبيق المكتبي + نظام الشكاوى

> مرجع ملزم لـ Claude و Codex. أي تغيير على عقد API هنا يُتفق عليه في `COLLAB.md` قبل تنفيذه.
> الحالة: **معتمدة من صاحب المشروع — التنفيذ بانتظار أمر البدء.**

---

## أ) الأدوار (تُحدَّد من داخل المنصة)

| القيمة في الكود | الاسم | الصلاحيات |
|---|---|---|
| `Employee` | موظف | يقدّم المقترحات والشكاوى ويتابعها |
| `Screener` | موظف فرز (الاقتراحات) | الفرز الأولي للمقترحات |
| `CommitteeMember` | عضو لجنة | دراسة المقترحات والتوقيع |
| `Executive` | مدير تنفيذي (**جديد**) | قرار الإدارة التنفيذية، تعيين مسؤول التنفيذ، التقارير، المستوى 2 من التصعيد |
| `ComplaintScreener` | موظف فرز الشكاوى (**جديد**) | فرز الشكاوى وتصنيفها وإسنادها |
| `Admin` | مدير نظام كامل | كل الصلاحيات + المستخدمين والإعدادات |

- القيم الجديدة **تُضاف في آخر** الـ enum؛ لا تُعاد ترقيم القيم الحالية.
- كل ما كان يسمح لـ `Admin` بالقرار التنفيذي يسمح الآن لـ `Executive` أو `Admin`.
- التعيين بالمهام (مُسند إليه، محقق، عضو لجنة تحقيق، مسؤول تنفيذ) **ليس دوراً**؛ أي موظف نشط يمكن إسناده.

---

## ب) نظام الدخول

### نوعا الحساب
| | داخلي (Domain) | محلي (Local) |
|---|---|---|
| كلمة المرور | كلمة مرور الويندوز، **لا تُخزَّن** في المنصة (`PasswordHash` فارغ) | محفوظة مشفّرة PBKDF2 |
| اسم الدخول | اسم الدومين (`DomainLogin`) | الرقم الوظيفي (`UserCode`) |
| المتصفح | ✅ يُتحقق مع الدومين (LDAPS bind) | ✅ يُتحقق من الكلمة المحفوظة |
| التطبيق المكتبي | ✅ دخول تلقائي (Windows Integrated / Kerberos) | ❌ رسالة: ادخل من المتصفح |
| الإيقاف المؤقت | 5 محاولات خاطئة = 15 دقيقة داخل المنصة (يحمي حساب الويندوز من القفل) | 5 محاولات = 15 دقيقة |

أسماء حسابات الدومين أرقام وظيفية (مثل `0462`)، لذلك **اسم الدخول فريد عبر النوعين**: لا يُقبل حساب محلي باسم يساوي `DomainLogin` لأي حساب داخلي والعكس، ويُبحث بالاسم المُطبَّع (بدون البادئة/اللاحقة، أحرف صغيرة) بنفس الصيغة دائماً.

حقول جديدة في `User`: `AccountType` (Domain/Local)، `DomainLogin` (sAMAccountName بأحرف صغيرة، فريد عند وجوده)، `MustChangePassword` (للمحلي).

### قواعد ثابتة
1. **رسالة فشل واحدة عامة** لكل الحالات (مستخدم غير موجود، كلمة خاطئة، حساب مقفل، موقوف، نوع غير مناسب): HTTP 401 + `"تعذّر تسجيل الدخول. تحقّق من اسم المستخدم وكلمة المرور."` مع زمن استجابة متقارب.
2. **لا كلمات مرور افتراضية**: يُلغى `--seed-passwords` وكلمة `Passw0rd!` خارج الاختبارات. أول مدير نظام = حساب دومين من الإعداد `Bootstrap:AdminDomainLogin` يُنشأ عند أول تشغيل إن لم يوجد مدير.
3. الحساب المحلي الجديد بكلمة أولية يُجبَر على تغييرها عند أول دخول (`mustChangePassword = true` في الاستجابة).
4. **جلسة واحدة بنفس الآلية** (كوكي HttpOnly + CSRF) أياً كان طريق الدخول → نفس الحساب والصلاحيات.
5. لا تُسجَّل كلمات المرور في أي log، ولا تُرسل للدومين إلا عبر LDAPS.

### إعدادات الخادم (appsettings)
```json
"Directory": { "Enabled": true, "Domain": "shjci.com", "NetbiosName": "SHJCI",
               "Server": "shjci.com", "Port": 636, "UseLdaps": true, "TimeoutSeconds": 10 },
"Bootstrap": { "AdminDomainLogin": "0462" }
```
الدومين `shjci.com` وأول مدير نظام `0462` مؤكدان من صاحب المشروع. **يُؤكَّد لاحقاً:** الاسم المختصر (NetBIOS، افتراضياً `SHJCI`) واسم خادم الدومين وإتاحة المنفذ 636. الاستضافة: IIS على ويندوز سيرفر منضم للدومين، مع تفعيل Windows Authentication و Anonymous معاً، وتسجيل SPN لاسم الموقع.

### عقد API
| الطلب | الوصف | الاستجابة |
|---|---|---|
| `POST /api/auth/login` `{ username, password }` | المتصفح. `username` يقبل: `0462` أو `SHJCI\0462` أو `0462@shjci.com` (داخلي) أو اسم المستخدم المحلي | 200 `{ expiresAt, user, mustChangePassword }` أو 401 بالرسالة العامة |
| `POST /api/auth/windows` + ترويسة `X-Client: desktop` | التطبيق المكتبي. مصادقة Negotiate؛ يرد 401 مع `WWW-Authenticate: Negotiate` حتى يكمل WebView2 المصافحة تلقائياً | 200 مثل login · 403 `{ code: "local_account", message }` · 403 `{ code: "not_registered", message }` · 403 `{ code: "unavailable", message }` |
| `GET /api/auth/me` | كما هو + الحقول `accountType`, `mustChangePassword` | |
| `POST /api/auth/change-password` | للحساب المحلي فقط؛ الداخلي يرد 400 "غيّر كلمة المرور من الويندوز" | |
| `POST/PUT /api/users` | يضاف `accountType`, `domainLogin`؛ `initialPassword` مطلوب للمحلي فقط ومرفوض للداخلي | |

رسائل 403 للتطبيق المكتبي:
- `local_account`: «حسابك في المنصة حساب محلي ولا يمكنه الدخول من التطبيق المكتبي. الرجاء الدخول من المتصفح باسم المستخدم وكلمة المرور.»
- `not_registered`: «حساب الويندوز الخاص بك غير مسجّل في المنصة. تواصل مع مدير النظام.»
- `unavailable`: «تعذّر الدخول حالياً. تواصل مع مدير النظام.» (موقوف/مقفل — دون تفاصيل)

### التطبيق المكتبي (`desktop/`)
- .NET 8 WPF + WebView2، اسم المشروع `SuggestionsDesktop`، إعداد `ServerUrl` في `appsettings.json` بجانب الملف التنفيذي.
- يفتح `ServerUrl` داخل WebView2 مع `AdditionalBrowserArguments = "--auth-server-allowlist=<host of ServerUrl>"` حتى يُرسل Kerberos تلقائياً.
- يضيف للـ WebView2 علامة يعرفها الـ SPA (الـ SPA يكتشف `window.chrome?.webview`).
- لا شاشة دخول: الـ SPA في وضع التطبيق المكتبي يستدعي `POST /api/auth/windows` تلقائياً. عند `local_account` تظهر الرسالة مع زر «فتح الموقع في المتصفح». عند تسجيل الخروج يظهر زر «الدخول مجدداً» (يعيد الاستدعاء) بدل نموذج كلمة المرور.
- نافذة RTL بعنوان «نظام الاقتراحات والشكاوى — جمعية الشارقة الخيرية»، ورسالة واضحة إن لم يكن WebView2 Runtime مثبتاً أو تعذّر الوصول للخادم.

---

## ج) نظام الشكاوى

### الحالات
```
New ─► (فرز) ─► Assigned ─► InProgress ⇄ AwaitingReply   (العداد متوقف)
 │  ▲                          │  ▲
 ▼  │                          │  └─ InvestigationDone
ReturnedForInfo (متوقف)        ├─► UnderInvestigation ─┘
                               ▼
                           Resolved ──(قبول أو انتهاء مدة الاعتراض)──► Closed
                               │
                               └──(اعتراض)──► InProgress  [ReopenCount++ + تصعيد]
```
| الحالة | العربية |
|---|---|
| `New` | جديد |
| `ReturnedForInfo` | معادة لمقدّمها لاستكمال البيانات |
| `Assigned` | مُسندة |
| `InProgress` | قيد المعالجة |
| `AwaitingReply` | بانتظار رد مقدّم الشكوى |
| `UnderInvestigation` | محالة للتحقيق |
| `Resolved` | تم الحل |
| `Closed` | مغلقة |

### البيانات
- `Complaint`: `ComplaintCode` (`SCC-2026-0001`)، `TypeId`، `Description`، `IsAnonymous`، `SubmitterId` (دائماً مرتبط حتى للمجهولة)، `ContactPhone`، `ContactEmail`، `SubjectUserId?` (المشتكى عليه، اختياري)، `HandlingDepartment`، `Priority` (Low/Medium/High/Urgent)، `Status`، `AssigneeId`، `HandlingPath` (None/DirectAction/Investigation)، `InvestigationFindings`، `Resolution`، `ActionTaken`، `ResolvedAt`، `ObjectionDeadline`، `ClosedAt`، `CloseReason` (Accepted/AutoClosed)، `ReopenCount`، `SatisfactionRating` (1–5)، `SlaDueAt`، `SlaPausedAt`، `EscalationLevel`، `EscalatedAt`، `EscalationNote`.
- `ComplaintInvestigator`: (ComplaintId, UserId, IsChair) — محقق واحد أو لجنة تحقيق.
- `ComplaintEntry` (سجل الإجراءات والرسائل): `Kind` (Note, ActionTaken, InfoRequest, ComplainantReply, InvestigationFinding, Resolution, Objection, Acceptance, Escalation)، `Text`، `AuthorId`، `VisibleToComplainant`، `CreatedAt`.
- `ComplaintType` (من الإعدادات): `Name`، `DefaultPriority`، `IsSensitive`، `IsActive`، `SortOrder`.
- المرفقات بنفس نظام مرفقات المقترحات.

**القائمة المبدئية للأنواع:** بيئة العمل · التعامل والسلوك · الإجراءات والأنظمة · الرواتب والمستحقات · الإجازات والدوام · المرافق والخدمات · تقنية المعلومات · الصحة والسلامة · التمييز أو التحرش (`IsSensitive`، أولوية عاجلة) · أخرى.

### المهل (قابلة للتعديل — قسم `Complaints` في الإعدادات)
| البند | المدة |
|---|---|
| الفرز | 24 ساعة |
| عاجل | 48 ساعة (يومان) |
| عالي | 72 ساعة |
| متوسط | 120 ساعة |
| منخفض | 240 ساعة |
| مدة الاعتراض بعد الحل | 5 أيام عمل (العطلة السبت والأحد) |

- العداد يبدأ عند الإسناد، و**يتوقف** في `AwaitingReply` و `ReturnedForInfo`؛ المدة الموقوفة تُضاف إلى `SlaDueAt` عند الاستئناف.
- التصعيد: المستوى 1 بعد 48 ساعة من المهلة → المدير المباشر للمُسند إليه؛ المستوى 2 بعد 96 ساعة → المديرون التنفيذيون + مدير النظام.
- **تصعيد فوري** للمستوى 2: نوع حساس، أو اعتراض مقدّم الشكوى، أو طلب تصعيد يدوي يذكر السبب.
- **إغلاق تلقائي**: مهمة الخلفية تغلق `Resolved` بعد `ObjectionDeadline` بـ `CloseReason = AutoClosed`.

### السرية والصلاحيات (ثوابت)
1. **الشكوى بدون اسم**: الاسم والرقم الوظيفي والإدارة وبيانات التواصل **لا تظهر لأي أحد** غير مقدّمها — ولا لمدير النظام — في DTO والسجل والإشعارات والبحث. التواصل يتم عبر رسائل الشكوى.
2. **الشكوى العادية**: هوية المقدّم يراها موظف فرز الشكاوى والمُسند إليه والمحققون والمدير التنفيذي ومدير النظام فقط.
3. **المشتكى عليه** (`SubjectUserId`) لا يرى الشكوى ولا يُسند إليه ولا يحقق فيها، مهما كان دوره.
4. لا يُسند ولا يحقق في الشكوى مقدّمها. وفي النوع الحساس لا يُسند لأحد من إدارة المقدّم (الرفض برسالة عامة «لا يمكن إسناد الشكوى لهذا الموظف» دون ذكر السبب).
5. مقدّم الشكوى يرى: حالتها، رسائل `VisibleToComplainant`، الحل والإجراء المتخذ. **لا يرى** الملاحظات الداخلية ولا تفاصيل التحقيق.
6. من يرى ماذا: المقدّم ← شكاواه · فرز الشكاوى + تنفيذي + مدير نظام ← الكل · المُسند إليه ← المسندة له · المحققون ← المحالة لهم.
7. كل إجراء يُسجَّل في التدقيق ويُرسل إشعاراً داخل المنصة لمن يعنيه.

### عقد API (`/api/complaints`)
| الطلب | من | الانتقال |
|---|---|---|
| `POST /api/complaints` `{ typeId, description, isAnonymous, contactPhone?, contactEmail?, subjectUserId? }` | موظف | → `New`، يرد `{ complaintCode, ... }` لرسالة التأكيد |
| `GET /api/complaints?mine=&queue=&assignedToMe=&investigating=&status=&priority=&typeId=&overdue=&search=` | حسب الصلاحية | |
| `GET /api/complaints/{id}` | من يحق له | DTO فيه `actions` (ما يحق للمشاهد فعله) |
| `POST /{id}/return-for-info` `{ message }` | فرز | `New` → `ReturnedForInfo` |
| `POST /{id}/complete-info` `{ message, description? }` | المقدّم | `ReturnedForInfo` → `New` |
| `POST /{id}/assign` `{ typeId, priority, handlingDepartment, assigneeId }` | فرز | `New` → `Assigned` (يبدأ العداد) — يُستخدم أيضاً لإعادة الإسناد |
| `POST /{id}/start` | المُسند إليه | `Assigned` → `InProgress` |
| `POST /{id}/entries` `{ kind: "Note"|"ActionTaken", text }` | المُسند إليه/المحقق | تسجيل إجراء |
| `POST /{id}/request-info` `{ message }` | المُسند إليه | → `AwaitingReply` (إيقاف العداد) |
| `POST /{id}/reply` `{ message }` | المقدّم | `AwaitingReply` → `InProgress` (استئناف) |
| `POST /{id}/refer-investigation` `{ investigatorIds: [...], chairId?, note }` | المُسند إليه/فرز | → `UnderInvestigation` (واحد = محقق، أكثر = لجنة) |
| `POST /{id}/investigation-findings` `{ findings, recommendation }` | المحقق/رئيس اللجنة | → `InProgress` (`HandlingPath = Investigation`) |
| `POST /{id}/resolve` `{ resolution, actionTaken }` | المُسند إليه | → `Resolved` + `ObjectionDeadline` |
| `POST /{id}/accept` `{ rating?, comment? }` | المقدّم | `Resolved` → `Closed` (Accepted) |
| `POST /{id}/object` `{ reason }` | المقدّم قبل الموعد | `Resolved` → `InProgress` + تصعيد |
| `POST /{id}/escalate` `{ note }` | المقدّم/فرز/المُسند إليه | تصعيد يدوي |
| `/{id}/attachments` (GET/POST/DELETE/download) | كالمقترحات | الإضافة للمقدّم في `New`/`ReturnedForInfo`/`AwaitingReply`، وللمعالج دائماً قبل الإغلاق |
| `GET/POST/PUT /api/complaint-types` | قراءة للجميع، تعديل لمدير النظام | |
| `GET /api/complaints/report` | تنفيذي/مدير نظام | الأعداد حسب الحالة والنوع والإدارة والأولوية، متوسط زمن الحل، نسبة الالتزام بالمهلة، نسبة إعادة الفتح، متوسط الرضا |

أنواع الإشعارات الجديدة: `ComplaintSubmitted`, `ComplaintAssigned`, `ComplaintInfoRequested`, `ComplaintReplied`, `ComplaintReferred`, `ComplaintResolved`, `ComplaintReopened`, `ComplaintClosed`, `ComplaintEscalation`, `ComplaintSlaBreach`.

### شاشات الواجهة
1. **تقديم شكوى** (مع خيار «بدون اسم» وشرح واضح لما يعنيه) ← شاشة تأكيد بالرقم المرجعي.
2. **شكاواي**: القائمة + التفاصيل + الرد على الاستفسارات + القبول/الاعتراض + تقييم الرضا.
3. **طابور فرز الشكاوى**: التصنيف والأولوية والإدارة والإسناد أو الإعادة.
4. **الشكاوى المسندة لي** و **التحقيقات المحالة لي**.
5. **تفاصيل الشكوى للمعالج**: خط زمني للإجراءات، المهلة المتبقية (مع «متوقف» عند الانتظار)، الأزرار حسب `actions`.
6. **إعدادات أنواع الشكاوى** (مدير النظام).
7. **لوحة مؤشرات الشكاوى** (تنفيذي/مدير نظام).
8. القائمة الجانبية: قسم «الشكاوى» منفصل عن «المقترحات».

---

## د) توزيع العمل

| المرحلة | Claude | Codex |
|---|---|---|
| 1. الأدوار + الدخول | الخادم: الحقول، LDAPS، `/api/auth/windows`، الرسالة العامة، الإيقاف، إلغاء الكلمات الافتراضية، المدير الأول، الاختبارات | الواجهة: شاشة الدخول، وضع التطبيق المكتبي، تغيير الكلمة الإجباري، إدارة المستخدمين (نوع الحساب واسم الدومين والأدوار الجديدة) |
| 2. التطبيق المكتبي | مراجعة | بناء `desktop/SuggestionsDesktop` |
| 3. الشكاوى | الخادم كاملاً + الاختبارات | الشاشات 1–8 |
| كل مرحلة | مراجعة commits الطرف الآخر | مراجعة commits الطرف الآخر |

**الاختبارات المطلوبة (معايير القبول):**
1. موظف داخلي يدخل من الموقع بكلمة مرور الويندوز.
2. نفس الموظف يدخل تلقائياً من التطبيق المكتبي بدون كلمة مرور، وبنفس الصلاحيات.
3. حساب محلي يدخل من الموقع ولا يستطيع الدخول من التطبيق.
4. كلمة مرور خاطئة تعطي الرسالة العامة نفسها لمستخدم موجود وغير موجود.
5. بعد 5 محاولات خاطئة للحساب المحلي يتوقف مؤقتاً (والرسالة تبقى عامة).
6. دورة شكوى كاملة: فتح ← فرز ← إسناد ← معالجة ← بانتظار رد (توقف العداد) ← حل ← قبول/إغلاق تلقائي ← اعتراض وإعادة فتح.
7. شكوى بدون اسم لا تكشف هوية مقدّمها لأي دور.

> الدومين في الاختبارات الآلية بديل وهمي (`IDirectoryAuthenticator`)؛ التحقق الحقيقي من البنود 1 و 2 يتم على شبكة الجمعية.
