import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { Router, provideRouter } from '@angular/router';
import { API_BASE_URL } from '../../core/api';
import type { Inbox } from '../../core/api/types';
import { LanguageService } from '../../core/i18n/language.service';
import { API } from '../../testing/fixtures';
import { provideTestTranslations } from '../../testing/translate-testing';
import { NotificationsComponent } from './notifications.component';

const inbox: Inbox = {
  items: [
    { id: 'e1', kind: 'crisis.alert', category: 'risk', text: 'High risk alert · P-3HX9-14QA · not acknowledged yet', link: '/alerts', createdAt: '2026-10-25T10:00:00Z', read: false, pinned: true, data: { eventId: 'e1' } },
    { id: 'n1', kind: 'staff.appointment.booked', category: 'appointments', text: 'New booking: P-7Q4K-82XM', link: '/patients/P-7Q4K-82XM', createdAt: '2026-10-25T09:00:00Z', read: false, pinned: false, data: {} },
    { id: 'n2', kind: 'security.new_sign_in', category: 'security', text: 'New sign-in from Chrome · Windows', link: '/settings', createdAt: '2026-10-24T09:00:00Z', read: true, pinned: false, data: { sessionId: 's1' } },
  ],
  unreadCount: 2,
  nextCursor: null,
  counts: { all: 2, risk: 1, appointments: 1, messages: 0, system: 0 },
};

describe('NotificationsComponent (S41)', () => {
  let backend: HttpTestingController;

  async function render(language: 'ar' | 'en' = 'en') {
    TestBed.configureTestingModule({
      imports: [NotificationsComponent],
      providers: [provideTestTranslations(), provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideNoopAnimations(), { provide: API_BASE_URL, useValue: API }],
    });
    await TestBed.inject(LanguageService).use(language);
    backend = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(NotificationsComponent);
    fixture.detectChanges();
    backend.expectOne(`${API}/v1/staff/inbox?category=all`).flush(inbox);
    fixture.detectChanges();
    return { fixture, el: fixture.nativeElement as HTMLElement };
  }

  afterEach(() => backend.verify());

  it('puts the unacknowledged risk alert first and opening it does not mark it read', async () => {
    const { el } = await render();
    const rows = el.querySelectorAll('[data-testid="inbox-item"]');
    expect(rows.length).toBe(3);
    expect(rows[0].classList).toContain('risk');
    expect(rows[0].textContent).toContain('not acknowledged yet');
    const navigate = spyOn(TestBed.inject(Router), 'navigateByUrl').and.resolveTo(true);
    (rows[0].querySelector('button.open') as HTMLButtonElement).click();
    backend.expectNone(`${API}/v1/staff/inbox/e1/read`);
    expect(navigate).toHaveBeenCalledWith('/alerts');
  });

  it('marks an ordinary entry read and follows its link', async () => {
    const { el } = await render();
    const navigate = spyOn(TestBed.inject(Router), 'navigateByUrl').and.resolveTo(true);
    (el.querySelectorAll('[data-testid="inbox-item"]')[1].querySelector('button.open') as HTMLButtonElement).click();
    backend.expectOne(`${API}/v1/staff/inbox/n1/read`).flush(null);
    backend.expectOne(`${API}/v1/staff/inbox/unread-count`).flush({ unreadCount: 1 });
    expect(navigate).toHaveBeenCalledWith('/patients/P-7Q4K-82XM');
  });

  it('marks all read but keeps the risk alert, and filters by category in Arabic', async () => {
    const { fixture, el } = await render('ar');
    expect(el.textContent).toContain('الكل · 2');
    (el.querySelector('[data-testid="read-all"]') as HTMLButtonElement).click();
    backend.expectOne(`${API}/v1/staff/inbox/read-all`).flush(null);
    backend.expectOne(`${API}/v1/staff/inbox/unread-count`).flush({ unreadCount: 1 });
    fixture.detectChanges();
    const rows = el.querySelectorAll('[data-testid="inbox-item"]');
    expect(rows[0].classList).toContain('unread');
    expect(rows[1].classList).not.toContain('unread');

    (el.querySelector('[data-testid="filter-messages"] button') as HTMLButtonElement).click();
    backend.expectOne(`${API}/v1/staff/inbox?category=messages`).flush({ ...inbox, items: [] });
    fixture.detectChanges();
    expect(el.querySelector('[data-testid="inbox-empty"]')!.textContent).toContain('لا شيء في هذا التصنيف');
  });

  it('sends "not you?" to the account settings with the session', async () => {
    const { el } = await render();
    const navigate = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);
    (el.querySelector('.not-you') as HTMLButtonElement).click();
    expect(navigate).toHaveBeenCalledWith(['/settings'], { queryParams: { session: 's1' } });
  });
});
