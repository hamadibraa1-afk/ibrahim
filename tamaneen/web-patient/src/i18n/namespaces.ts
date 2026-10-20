/**
 * Every message namespace has one file per language: messages/<lang>/<namespace>.json, holding a single
 * top-level key equal to the file name. Splitting by screen keeps translation reviews small; the request
 * config merges them. A unit test checks both languages have the same keys and placeholders.
 */
export const namespaces = [
  // shell and shared
  'meta', 'common', 'languageToggle', 'notFound', 'errors', 'nav', 'ui', 'specialties',
  // public and onboarding
  'welcome', 'howItWorks', 'legal', 'crisisResources', 'eligibility', 'signup', 'verify', 'recoveryCodes',
  'login', 'loginMfa', 'recover', 'consents', 'safetyInfo', 'choosePath', 'safetyCheck', 'systemStates',
  // intake, crisis, safety plan
  'intake', 'summary', 'crisis', 'waitingTriage', 'safetyPlan',
  // matching and booking
  'matching', 'therapists', 'therapistProfile', 'booking', 'bookConfirmed', 'appointments', 'appointmentDetail',
  // home and settings
  'home', 'homework', 'checkin', 'privacy', 'settings',
] as const;

export type Namespace = (typeof namespaces)[number];
