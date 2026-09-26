/*
  فحص تخزين الأوقات.

  عمود datetimeoffset يحفظ اللحظة مع إزاحتها، فالقيمة المخزّنة بـ +04:00 تمثّل
  نفس اللحظة تمامًا التي تمثّلها بـ +00:00. لذلك لا تحتاج البيانات القديمة أي تحويل:
  المحوّل الجديد يقرأها ويحوّلها إلى UTC عند القراءة، واللحظة لا تتغير.

  هذا السكربت للاطمئنان فقط: يعرض عدد السجلات حسب الإزاحة المخزّنة،
  ويتأكد أن كل عمود وقت من نوع datetimeoffset لا datetime2.
*/

-- 1) أي عمود وقت ليس datetimeoffset يفقد الإزاحة وهو ما يجب الانتباه له
SELECT  t.name AS [الجدول], c.name AS [العمود], ty.name AS [النوع]
FROM    sys.columns c
JOIN    sys.tables  t ON t.object_id = c.object_id
JOIN    sys.types   ty ON ty.user_type_id = c.user_type_id
WHERE   ty.name IN ('datetime', 'datetime2', 'smalldatetime')
ORDER BY t.name, c.name;

-- 2) توزيع الإزاحات المخزّنة في سجل الحضور
SELECT  DATEPART(TZOFFSET, CheckInAt) / 60 AS [الإزاحة بالساعات],
        COUNT(*)                           AS [عدد السجلات]
FROM    AttendanceRecords
WHERE   CheckInAt IS NOT NULL
GROUP BY DATEPART(TZOFFSET, CheckInAt)
ORDER BY 1;

-- 3) تأكيد أن التحويل إلى UTC لا يغيّر اللحظة (يجب أن تكون النتيجة صفرًا)
SELECT  COUNT(*) AS [سجلات تغيّرت لحظتها بعد التحويل]
FROM    AttendanceRecords
WHERE   CheckInAt IS NOT NULL
  AND   SWITCHOFFSET(CheckInAt, 0) <> CheckInAt;
