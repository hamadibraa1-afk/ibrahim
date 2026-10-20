import { TestBed } from '@angular/core/testing';
import { SessionService } from './session.service';

describe('SessionService', () => {
  it('starts with the first held role and exposes its menu', () => {
    const session = TestBed.inject(SessionService);
    session.setRoles(['therapist', 'supervisor', 'therapist']);
    expect(session.roles()).toEqual(['therapist', 'supervisor']);
    expect(session.role()).toBe('therapist');
    expect(session.navigation()[0].items.map((i) => i.key)).toContain('patients');
  });

  it('switches only to a role the user holds', () => {
    const session = TestBed.inject(SessionService);
    session.setRoles(['therapist', 'supervisor']);
    session.switchTo('supervisor');
    expect(session.navigation()[0].items.map((i) => i.key)).toContain('crisis');
    expect(() => session.switchTo('admin')).toThrowError(/not held/);
  });

  it('gives admins grouped menus and no clinical pages', () => {
    const session = TestBed.inject(SessionService);
    session.setRoles(['admin']);
    const keys = session.navigation().flatMap((g) => g.items.map((i) => i.key));
    expect(session.navigation().length).toBe(5);
    expect(keys).toContain('modules');
    expect(keys).not.toContain('patients');
    expect(keys).not.toContain('crisis');
  });

  it('shows nothing when signed out', () => {
    const session = TestBed.inject(SessionService);
    session.setRoles(['lead']);
    session.clear();
    expect(session.role()).toBeNull();
    expect(session.navigation()).toEqual([]);
  });
});
