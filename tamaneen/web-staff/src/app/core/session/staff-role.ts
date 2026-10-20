export type StaffRole = 'therapist' | 'supervisor' | 'lead' | 'admin';

export interface NavItem {
  /** Route path under the portal, also the key of the label in the translation files (nav.<key>). */
  key: string;
}

export interface NavGroup {
  key: string;
  items: NavItem[];
}

/** Side menu per role, as in the handoff file (StaffNav). Admin pages are grouped. */
export const NAVIGATION: Record<StaffRole, NavGroup[]> = {
  therapist: [
    { key: 'workspace', items: ['dashboard', 'patients', 'messages', 'schedule', 'alerts', 'notifications', 'availability', 'leave', 'wellbeing', 'cpd', 'profile'].map((key) => ({ key })) },
  ],
  supervisor: [
    { key: 'oncall', items: ['triage', 'crisis', 'reviews', 'noshow', 'oncall', 'rota', 'reassign', 'qa', 'metrics'].map((key) => ({ key })) },
  ],
  lead: [
    { key: 'clinical', items: ['rules', 'reporting', 'reviews', 'rota', 'qa', 'metrics'].map((key) => ({ key })) },
  ],
  admin: [
    { key: 'settings', items: ['settings-platform', 'ai', 'calendar', 'integrations', 'modules', 'flags'].map((key) => ({ key })) },
    { key: 'content', items: ['content', 'templates', 'tests', 'catalogs', 'surveys', 'announce'].map((key) => ({ key })) },
    { key: 'users', items: ['users', 'import', 'verifications', 'roles', 'notifrules'].map((key) => ({ key })) },
    { key: 'operations', items: ['capacity', 'special', 'datareq', 'complaints', 'security'].map((key) => ({ key })) },
    { key: 'monitoring', items: ['aieval', 'audit', 'rules', 'health', 'dr', 'metrics', 'reports'].map((key) => ({ key })) },
  ],
};
