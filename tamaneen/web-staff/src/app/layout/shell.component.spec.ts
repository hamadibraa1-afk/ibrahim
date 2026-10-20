import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { LanguageService } from '../core/i18n/language.service';
import { SessionService } from '../core/session/session.service';
import { provideTestTranslations } from '../testing/translate-testing';
import { ShellComponent } from './shell.component';

describe('ShellComponent', () => {
  async function render(roles: ('therapist' | 'supervisor' | 'lead' | 'admin')[]) {
    TestBed.configureTestingModule({
      imports: [ShellComponent],
      providers: [provideTestTranslations(), provideRouter([]), provideNoopAnimations(), provideHttpClient(), provideHttpClientTesting()],
    });
    TestBed.inject(SessionService).setRoles(roles);
    const language = TestBed.inject(LanguageService);
    await language.use('ar');
    const fixture = TestBed.createComponent(ShellComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return { fixture, language, el: fixture.nativeElement as HTMLElement };
  }

  afterEach(() => localStorage.clear());

  it('renders right-to-left in Arabic with Arabic menu labels', async () => {
    const { el } = await render(['therapist']);
    expect(el.querySelector('[data-testid="shell"]')!.getAttribute('dir')).toBe('rtl');
    expect(el.textContent).toContain('مرضاي');
    expect(el.querySelector('[data-testid="language-toggle"]')!.textContent).toContain('English');
  });

  it('flips to left-to-right with English labels when the toggle is pressed', async () => {
    const { fixture, el } = await render(['therapist']);
    (el.querySelector('[data-testid="language-toggle"]') as HTMLButtonElement).click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(el.querySelector('[data-testid="shell"]')!.getAttribute('dir')).toBe('ltr');
    expect(el.textContent).toContain('My patients');
    expect(el.textContent).not.toContain('مرضاي');
    expect(el.querySelector('[data-testid="language-toggle"]')!.textContent).toContain('العربية');
  });

  it('shows the role switch only to people who hold more than one role', async () => {
    const single = await render(['therapist']);
    expect(single.el.querySelector('[data-testid="role-switch"]')).toBeNull();
    TestBed.resetTestingModule();
    const multi = await render(['therapist', 'supervisor']);
    expect(multi.el.querySelector('[data-testid="role-switch"]')).not.toBeNull();
  });

  it('labels the toggle for screen readers in the current language', async () => {
    const { fixture, el, language } = await render(['admin']);
    const toggle = el.querySelector('[data-testid="language-toggle"]')!;
    expect(toggle.getAttribute('aria-label')).toBe('التبديل إلى الإنجليزية');
    expect(toggle.querySelector('[lang]')!.getAttribute('lang')).toBe('en');
    await language.use('en');
    fixture.detectChanges();
    expect(toggle.getAttribute('aria-label')).toBe('Switch to Arabic');
  });
});
