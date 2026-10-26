import { TestBed } from '@angular/core/testing';
import { SessionService } from './session.service';
import { IMPLEMENTED_ROUTES, NAVIGATION, menuFor, visibleNavigation } from './staff-role';

describe('SessionService', () => {
  it('starts with the first held role and exposes its menu', () => {
    const session = TestBed.inject(SessionService);
    session.setRoles(['therapist', 'supervisor', 'therapist']);
    expect(session.roles()).toEqual(['therapist', 'supervisor']);
    expect(session.role()).toBe('therapist');
    expect(session.navigation()[0].items.map((i) => i.key)).toEqual(['dashboard', 'patients', 'schedule', 'alerts', 'notifications', 'availability', 'profile']);
    expect(session.home()).toBe('/dashboard');
  });

  it('switches only to a role the user holds', () => {
    const session = TestBed.inject(SessionService);
    session.setRoles(['therapist', 'supervisor']);
    session.switchTo('supervisor');
    expect(session.navigation()[0].items.map((i) => i.key)).toEqual(['triage', 'crisis']);
    expect(session.home()).toBe('/crisis');
    expect(() => session.switchTo('admin')).toThrowError(/not held/);
  });

  it('gives on-call therapists the crisis console, and others not', () => {
    const session = TestBed.inject(SessionService);
    session.setRoles(['therapist']);
    session.setProfile({ displayName: 'Sara', onCall: false });
    expect(session.crisisAccess()).toBeFalse();
    session.setProfile({ displayName: 'Sara', onCall: true });
    expect(session.crisisAccess()).toBeTrue();
    expect(session.navigation().flatMap((g) => g.items.map((i) => i.key))).toEqual(['dashboard', 'patients', 'schedule', 'alerts', 'notifications', 'availability', 'profile', 'crisis']);
  });

  it('gives admins no clinical pages and no links to screens that do not exist', () => {
    const session = TestBed.inject(SessionService);
    session.setRoles(['admin']);
    expect(session.navigation()).toEqual([]);
    expect(session.crisisAccess()).toBeFalse();
    expect(session.home()).toBe('/home');
  });

  it('gives the clinical lead the triage queue to co-sign', () => {
    const session = TestBed.inject(SessionService);
    session.setRoles(['lead']);
    expect(session.navigation()[0].items.map((i) => i.key)).toEqual(['triage', 'crisis']);
  });

  it('shows nothing when signed out', () => {
    const session = TestBed.inject(SessionService);
    session.setRoles(['lead']);
    session.setProfile({ displayName: 'Huda', onCall: false });
    session.clear();
    expect(session.role()).toBeNull();
    expect(session.displayName()).toBeNull();
    expect(session.navigation()).toEqual([]);
  });
});

describe('navigation filter', () => {
  it('keeps only implemented items and drops empty groups', () => {
    const groups = [
      { key: 'a', items: [{ key: 'dashboard' }, { key: 'messages' }] },
      { key: 'b', items: [{ key: 'modules' }] },
      { key: 'c', items: [{ key: 'crisis' }] },
    ];
    expect(visibleNavigation(groups)).toEqual([
      { key: 'a', items: [{ key: 'dashboard' }] },
      { key: 'c', items: [{ key: 'crisis' }] },
    ]);
    expect(visibleNavigation(groups, new Set(['modules']))).toEqual([{ key: 'b', items: [{ key: 'modules' }] }]);
  });

  it('never renders an item that has no screen, for any role', () => {
    for (const role of Object.keys(NAVIGATION) as (keyof typeof NAVIGATION)[]) {
      for (const onCall of [false, true]) {
        for (const item of visibleNavigation(menuFor(role, onCall)).flatMap((g) => g.items)) {
          expect(IMPLEMENTED_ROUTES.has(item.key)).withContext(`${role}:${item.key}`).toBeTrue();
        }
      }
    }
  });

  it('keeps the full role menus as designed, so new screens only need adding to the list', () => {
    expect(NAVIGATION.therapist[0].items.map((i) => i.key)).toContain('patients');
    expect(NAVIGATION.admin.length).toBe(5);
  });
});
