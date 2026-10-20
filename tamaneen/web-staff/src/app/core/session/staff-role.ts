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
    { key: 'clinical', items: ['crisis', 'rules', 'reporting', 'reviews', 'rota', 'qa', 'metrics'].map((key) => ({ key })) },
  ],
  admin: [
    { key: 'settings', items: ['settings-platform', 'ai', 'calendar', 'integrations', 'modules', 'flags'].map((key) => ({ key })) },
    { key: 'content', items: ['content', 'templates', 'tests', 'catalogs', 'surveys', 'announce'].map((key) => ({ key })) },
    { key: 'users', items: ['users', 'import', 'verifications', 'roles', 'notifrules'].map((key) => ({ key })) },
    { key: 'operations', items: ['capacity', 'special', 'datareq', 'complaints', 'security'].map((key) => ({ key })) },
    { key: 'monitoring', items: ['aieval', 'audit', 'rules', 'health', 'dr', 'metrics', 'reports'].map((key) => ({ key })) },
  ],
};

/** Therapists see the crisis console while they are on call (StaffMe.onCall). */
export const ON_CALL_GROUP: NavGroup = { key: 'oncall', items: [{ key: 'crisis' }] };

/** Menu entries that have a screen. Anything else is left out of the menu instead of linking nowhere. */
export const IMPLEMENTED_ROUTES: ReadonlySet<string> = new Set(['dashboard', 'crisis']);

/** Keeps only the items with a screen, and drops groups left empty. */
export function visibleNavigation(groups: readonly NavGroup[], implemented: ReadonlySet<string> = IMPLEMENTED_ROUTES): NavGroup[] {
  return groups
    .map((group) => ({ key: group.key, items: group.items.filter((item) => implemented.has(item.key)) }))
    .filter((group) => group.items.length > 0);
}

/** The full menu for a role before filtering, including the on-call entry for on-call therapists. */
export function menuFor(role: StaffRole, onCall: boolean): NavGroup[] {
  const groups = NAVIGATION[role];
  return role === 'therapist' && onCall ? [...groups, ON_CALL_GROUP] : groups;
}

/** Supervisors, clinical leads, and therapists who are on call work the crisis console. */
export function canUseCrisisConsole(role: StaffRole | null, onCall: boolean): boolean {
  return role === 'supervisor' || role === 'lead' || (role === 'therapist' && onCall);
}

/** Where a role lands after sign-in. */
export function homeRoute(role: StaffRole | null, onCall: boolean): string {
  if (role === 'therapist') {
    return '/dashboard';
  }
  if (canUseCrisisConsole(role, onCall)) {
    return '/crisis';
  }
  return '/home';
}
