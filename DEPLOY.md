# دليل النشر

## الصورة الكاملة

| الجزء | أين يُنشر | السبب |
| --- | --- | --- |
| الواجهة (Angular) | Cloudflare Pages | ملفات ثابتة، مجانية وسريعة عالميًا |
| الخادم (.NET) | خادمك أو خدمة سحابية تدعم .NET | Cloudflare لا تشغّل .NET |
| قاعدة البيانات (SQL Server) | نفس مكان الخادم أو خدمة SQL مُدارة | تحتاج شبكة خاصة مع الخادم |

**الخيار الأنسب لكم:** الواجهة على Cloudflare Pages، والخادم وقاعدة البيانات يبقيان داخل شبكة الجمعية، ويُنشران للإنترنت عبر **Cloudflare Tunnel**. بذلك لا تفتح أي منفذ في جدار الحماية ولا تخرج بيانات الموظفين من مركز بياناتكم.

## قبل النشر — إلزامي

1. **استبدال إنشاء قاعدة البيانات بـ EF Migrations.** حاليًا `EnsureCreated` ينشئ الهيكل ولا يحدّثه، فأي تعديل لاحق يتطلب حذف البيانات. أخبرني لأحوّلها.
2. **مفتاح توقيع جديد.** ولّد مفتاحًا عشوائيًا 32 خانة فأكثر ولا تضعه في الملفات:
   `setx ASPNETCORE_Jwt__Key "<مفتاح عشوائي طويل>"`
3. **إيقاف البيانات التجريبية:** `SeedDemoData` = false في `appsettings.Production.json`، وحذف الحسابات التجريبية.
4. **إعادة دقة الموقع إلى 100 متر** (مضبوطة في ملف الإنتاج).
5. **تحديد النطاق في CORS:** استبدل `https://REPLACE-WITH-YOUR-DOMAIN` بنطاق الواجهة.
6. **نسخ احتياطي يومي** لقاعدة البيانات قبل أي استخدام حقيقي.

## 1. نشر الواجهة على Cloudflare Pages

```
cd web
npm install
npx ng build
npx wrangler login
npx wrangler pages deploy dist/web/browser --project-name=field-attendance
```

`wrangler login` يفتح متصفحك لتسجيل الدخول بحسابك؛ لا أستطيع تنفيذها عنك.

بعد النشر، عدّل `config.json` في المشروع ليشير إلى عنوان الخادم العام ثم أعد النشر:

```json
{ "apiBase": "https://api.your-domain.ae" }
```

ملف `public/_redirects` موجود مسبقًا ليعمل التنقل بين الصفحات بشكل صحيح.

## 2. نشر الخادم عبر Cloudflare Tunnel (يبقى داخل شبكتكم)

على الخادم الذي يشغّل الـ API:

```
winget install --id Cloudflare.cloudflared
cloudflared tunnel login
cloudflared tunnel create field-attendance-api
cloudflared tunnel route dns field-attendance-api api.your-domain.ae
cloudflared tunnel run --url http://localhost:5000 field-attendance-api
```

ثم شغّل الـ API كخدمة دائمة:

```
dotnet publish -c Release -o C:\Apps\FieldAttendanceApi
sc create FieldAttendanceApi binPath= "C:\Apps\FieldAttendanceApi\FieldAttendance.Api.exe" start= auto
sc start FieldAttendanceApi
```

وسجّل cloudflared كخدمة حتى يعمل بعد إعادة التشغيل:

```
cloudflared service install
```

## 3. بديل: استضافة الخادم سحابيًا

إن رغبتم بعدم استضافة أي شيء داخليًا، الخيار الطبيعي لـ .NET و SQL Server هو Azure App Service مع Azure SQL. أخبرني لأجهّز ملفات النشر.

## بعد النشر — تحقق

- فتح `https://<نطاق الواجهة>` يعرض شاشة الدخول.
- تسجيل الدخول يعمل (يعني أن `apiBase` و CORS مضبوطان).
- فتح رابط تقييم من جوال خارج الشبكة يعمل.
- تسجيل الحضور من الجوال يعمل (يتطلب HTTPS، وهو متوفر تلقائيًا عبر Cloudflare).
- **أعد توليد رموز QR وطباعتها من النطاق النهائي**، لأن الرموز القديمة تحمل عنوان localhost.
