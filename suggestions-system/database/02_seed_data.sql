-- =============================================================================
-- البيانات الأولية: حساب مدير النظام الأول فقط.
--
-- كلمات المرور تُجزَّأ بـ PBKDF2 ولا يمكن توليدها من SQL، لذلك يُترك PasswordHash
-- فارغاً ثم يُشغَّل مرة واحدة:
--     cd api/ProposalSystem.Api
--     dotnet run --seed-passwords
-- فيُعيَّن Passw0rd! لكل حساب بلا تجزئة صالحة. غيّرها فوراً من "الملف الشخصي".
--
-- حقول النموذج (بما فيها حقول قياس الأثر الفعلي) يضيفها الخادم تلقائياً عند الإقلاع.
-- بقية المستخدمين والأدوار والمدير المباشر تُدار من شاشة "إدارة المستخدمين".
-- =============================================================================

USE [ProposalSystem];
GO

IF NOT EXISTS (SELECT 1 FROM [Users] WHERE [UserCode] = N'EMP-1001')
INSERT INTO [Users]
    ([UserCode], [ArabicName], [EnglishName], [Email], [PhoneNumber], [Department], [JobTitle],
     [Role], [Status], [PasswordHash], [CreatedAt], [ManagerId], [FailedLoginCount], [LockoutEndAt])
VALUES
    (N'EMP-1001', N'إبراهيم عبدالجليل حمد', N'Ibrahim Abduljalil Hamad', N'emp-1001@shjcharity.ae', N'',
     N'إدارة التميز المؤسسي', N'مدير النظام', N'Admin', N'Active', N'', SYSUTCDATETIME(), NULL, 0, NULL);
GO
