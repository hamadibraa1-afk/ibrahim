/**
 * الواجهة تخاطب الخادم عبر مسار نسبي دائماً (وكيل ng serve في التطوير، والخادم نفسه في الإنتاج)
 * حتى تبقى كعكة الجلسة من نفس المصدر (SameSite=Strict) ويعمل رمز الحماية XSRF تلقائياً.
 */
export const environment = {
  production: false,
  apiUrl: '/api',
  hubUrl: '/hubs/notifications',
};
