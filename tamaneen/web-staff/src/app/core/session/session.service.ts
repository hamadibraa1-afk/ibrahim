import { Injectable, computed, signal } from '@angular/core';
import { NAVIGATION, type StaffRole } from './staff-role';

/**
 * The signed-in staff member's roles and the role they are working as now ("تعمل الآن كـ").
 * Roles are set from the validated token at sign-in (S06). A person may hold more than one role,
 * e.g. a therapist who also takes on-call shifts as a supervisor.
 */
@Injectable({ providedIn: 'root' })
export class SessionService {
  private readonly heldRoles = signal<StaffRole[]>([]);
  private readonly activeRole = signal<StaffRole | null>(null);

  readonly roles = this.heldRoles.asReadonly();
  readonly role = this.activeRole.asReadonly();
  readonly navigation = computed(() => {
    const role = this.activeRole();
    return role ? NAVIGATION[role] : [];
  });

  setRoles(roles: readonly StaffRole[]): void {
    const unique = [...new Set(roles)];
    this.heldRoles.set(unique);
    const active = this.activeRole();
    this.activeRole.set(active && unique.includes(active) ? active : unique[0] ?? null);
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
  }
}
