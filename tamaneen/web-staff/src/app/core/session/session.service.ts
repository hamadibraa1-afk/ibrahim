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

  clear(): void {
    this.heldRoles.set([]);
    this.activeRole.set(null);
    this.onCallNow.set(false);
    this.name.set(null);
  }
}
