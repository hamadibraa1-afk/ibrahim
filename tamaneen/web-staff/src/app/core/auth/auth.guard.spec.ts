import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { SessionService } from '../session/session.service';
import { authGuard, crisisAccessGuard, therapistGuard } from './auth.guard';
import { AuthService } from './auth.service';

@Component({ template: '' })
class BlankComponent {}

describe('route guards', () => {
  const signedIn = signal(false);

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: { signedIn } },
        provideRouter([
          { path: 'login', component: BlankComponent },
          {
            path: '',
            canActivate: [authGuard],
            children: [
              { path: 'dashboard', canMatch: [therapistGuard], component: BlankComponent },
              { path: 'crisis', canMatch: [crisisAccessGuard], component: BlankComponent },
              { path: 'home', component: BlankComponent },
            ],
          },
        ]),
      ],
    });
  });

  async function navigate(url: string): Promise<string> {
    const router = TestBed.inject(Router);
    await router.navigateByUrl(url);
    return router.url;
  }

  afterEach(() => signedIn.set(false));

  it('sends signed-out visitors to sign-in and keeps where they were going', async () => {
    expect(await navigate('/crisis')).toBe('/login?returnUrl=%2Fcrisis');
  });

  it('keeps the crisis console to clinical staff: a therapist off call reaches it for her own patients', async () => {
    signedIn.set(true);
    const session = TestBed.inject(SessionService);
    session.setRoles(['therapist']);
    expect(await navigate('/crisis')).toBe('/crisis');
    session.setRoles(['admin']);
    expect(await navigate('/crisis')).toBe('/home');
  });

  it('lets an on-call therapist in, and sends non-therapists away from the dashboard', async () => {
    signedIn.set(true);
    const session = TestBed.inject(SessionService);
    session.setRoles(['therapist']);
    session.setProfile({ displayName: 'Sara', onCall: true });
    expect(await navigate('/crisis')).toBe('/crisis');
    session.setRoles(['admin']);
    expect(await navigate('/dashboard')).toBe('/home');
  });
});
