import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { AuthService } from './auth.service';
import { IDLE_TIMEOUT_MS, IdleService } from './idle.service';

describe('IdleService (S32 idle timeout)', () => {
  function setup(url: string) {
    const signedIn = signal(true);
    const auth = { signedIn, idleSignOut: jasmine.createSpy('idleSignOut').and.resolveTo() };
    TestBed.configureTestingModule({
      providers: [
        { provide: AuthService, useValue: auth },
        { provide: Router, useValue: { url } },
      ],
    });
    const idle = TestBed.inject(IdleService);
    TestBed.flushEffects();
    return { idle, auth, signedIn };
  }

  it('signs out after 15 minutes without input, not before', () => {
    const { idle, auth } = setup('/patients/P-1');
    const start = Date.now();
    idle.check(start + IDLE_TIMEOUT_MS - 1000);
    expect(auth.idleSignOut).not.toHaveBeenCalled();
    idle.check(start + IDLE_TIMEOUT_MS + 1000);
    expect(auth.idleSignOut).toHaveBeenCalledTimes(1);
  });

  it('counts any key or pointer input as activity', () => {
    const { idle, auth } = setup('/schedule');
    const later = Date.now() + IDLE_TIMEOUT_MS - 5000;
    jasmine.clock().install();
    jasmine.clock().mockDate(new Date(later));
    document.dispatchEvent(new KeyboardEvent('keydown'));
    jasmine.clock().uninstall();
    idle.check(later + 10_000);
    expect(auth.idleSignOut).not.toHaveBeenCalled();
  });

  it('never signs a therapist out in the middle of a live session', () => {
    const { idle, auth } = setup('/sessions/ap-1');
    idle.check(Date.now() + 2 * IDLE_TIMEOUT_MS);
    expect(auth.idleSignOut).not.toHaveBeenCalled();
  });

  it('does nothing once signed out', () => {
    const { idle, auth, signedIn } = setup('/schedule');
    signedIn.set(false);
    TestBed.flushEffects();
    idle.check(Date.now() + 2 * IDLE_TIMEOUT_MS);
    expect(auth.idleSignOut).not.toHaveBeenCalled();
  });
});
