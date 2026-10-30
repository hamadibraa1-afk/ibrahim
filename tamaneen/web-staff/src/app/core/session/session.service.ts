import { Injectable, computed, signal } from '@angular/core';
import { type StaffRole, canUseCrisisConsole, homeRoute, menuFor, visibleNavigation } from './staff-role';

/**
 * The signed-in staff member's roles and the role they are working as now ("تعمل الآن كـ").
 * Roles are set from GET /v1/staff/me at sign-in (S06). A person may hold more than one role,
 * e.g. a therapist who also takes on-call shifts as a supervisor.
 */
@Injectable({ providedIn: 'root' })
export class SessionService {
  private readonly heldRoles = signal<StaffRole[]>([]);
  private readonly activeRole = signal<StaffRole | null>(null);
  private readonly onCallNow = signal(false);
  private readonly name = signal<string | null>(null);
  /**
   * Unsaved clinical drafts (S15 plan), by key. Kept in this tab's memory only, never in browser storage: the
   * portal runs on shared clinic computers, and a copy on disk (even encrypted under a key stored beside it)
   * would outlive the sign-out. Cleared with the session.
   */
  private readonly drafts = new Map<string, unknown>();

  readonly roles = this.heldRoles.asReadonly();
  readonly role = this.activeRole.asReadonly();
  readonly onCall = this.onCallNow.asReadonly();
  readonly displayName = this.name.asReadonly();
  /** Menu of the active role, limited to screens that exist. */
  readonly navigation = computed(() => {
    const role = this.activeRole();
    return role ? visibleNavigation(menuFor(role, this.onCallNow())) : [];
  });
  readonly crisisAccess = computed(() => canUseCrisisConsole(this.activeRole(), this.onCallNow()));
  readonly home = computed(() => homeRoute(this.activeRole(), this.onCallNow()));

  setRoles(roles: readonly StaffRole[]): void {
    const unique = [...new Set(roles)];
    this.heldRoles.set(unique);
    const active = this.activeRole();
    this.activeRole.set(active && unique.includes(active) ? active : unique[0] ?? null);
  }

  setProfile(profile: { displayName: string; onCall: boolean }): void {
    this.name.set(profile.displayName);
    this.onCallNow.set(profile.onCall);
  }

  switchTo(role: StaffRole): void {
    if (!this.heldRoles().includes(role)) {
      throw new Error(`Role ${role} is not held by this user.`);
    }
    this.activeRole.set(role);
  }

  keepDraft(key: string, value: unknown): void {
    this.drafts.set(key, value);
  }

  draft<T>(key: string): T | null {
    return (this.drafts.get(key) as T | undefined) ?? null;
  }

  dropDraft(key: string): void {
    this.drafts.delete(key);
  }

  clear(): void {
    this.drafts.clear();
    this.heldRoles.set([]);
    this.activeRole.set(null);
    this.onCallNow.set(false);
    this.name.set(null);
  }
}
