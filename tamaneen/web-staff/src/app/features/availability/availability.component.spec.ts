import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { API_BASE_URL } from '../../core/api';
import type { Availability, AvailabilitySlot } from '../../core/api/types';
import { LanguageService } from '../../core/i18n/language.service';
import { API, problem, settle } from '../../testing/fixtures';
import { provideTestTranslations } from '../../testing/translate-testing';
import { AvailabilityComponent, dayError, toPlans, toRequest } from './availability.component';

const TZ = 'Asia/Riyadh';

function slot(overrides: Partial<AvailabilitySlot> = {}): AvailabilitySlot {
  return { slotId: 's1', startsAt: '2026-11-01T07:00:00Z', endsAt: '2026-11-01T07:50:00Z', status: 'open', source: 'template', ...overrides };
}

function availability(overrides: Partial<Availability> = {}): Availability {
  return {
    therapistId: 't1',
    timeZone: TZ,
    sessionMinutes: 50,
    sessionLengths: [30, 45, 50, 60, 90],
    bufferMinutes: 10,
    bufferOptions: [0, 5, 10, 15, 20, 30],
    weekly: [{ day: 0, start: '10:00', end: '13:00' }, { day: 0, start: '16:00', end: '19:00' }, { day: 1, start: '11:00', end: '18:00' }],
    exceptions: [{ id: 'e1', kind: 'leave', startsAt: '2026-11-01T21:00:00Z', endsAt: '2026-11-03T21:00:00Z' }],
    horizonDays: 56,
    licenseVerified: true,
    updatedAt: '2026-10-20T08:00:00Z',
    slots: [slot(), slot({ slotId: 's2', startsAt: '2026-11-01T08:00:00Z', endsAt: '2026-11-01T08:50:00Z', status: 'booked' })],
    affected: [],
    ...overrides,
  };
}

describe('availability rules', () => {
  it('flags periods that end before they start or overlap, and accepts touching ones', () => {
    expect(dayError([{ start: '10:00', end: '09:00' }])).toBe('end_before_start');
    expect(dayError([{ start: '10:00', end: '12:00' }, { start: '11:30', end: '13:00' }])).toBe('overlap');
    expect(dayError([{ start: '10:00', end: '12:00' }, { start: '12:00', end: '13:00' }])).toBeNull();
  });

  it('sends only the days that are switched on', () => {
    const plans = toPlans(availability());
    expect(plans.length).toBe(7);
    expect(plans[0].periods.length).toBe(2);
    expect(plans[5].enabled).toBeFalse();
    plans[1] = { ...plans[1], enabled: false };
    expect(toRequest(plans, 50, 10, []).weekly).toEqual([{ day: 0, start: '10:00', end: '13:00' }, { day: 0, start: '16:00', end: '19:00' }]);
  });
});

describe('AvailabilityComponent', () => {
  let backend: HttpTestingController;

  async function render(data: Availability = availability(), language: 'ar' | 'en' = 'en') {
    TestBed.configureTestingModule({
      imports: [AvailabilityComponent],
      providers: [provideTestTranslations(), provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations(), { provide: API_BASE_URL, useValue: API }],
    });
    await TestBed.inject(LanguageService).use(language);
    backend = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(AvailabilityComponent);
    fixture.detectChanges();
    backend.expectOne(`${API}/v1/staff/availability`).flush(data);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const update = async () => {
      fixture.detectChanges();
      await settle();
      fixture.detectChanges();
    };
    return { fixture, el, update };
  }

  afterEach(() => backend.verify());

  it('shows the usual week, the exceptions and the upcoming times with booked ones locked', async () => {
    const { el } = await render();
    expect(el.querySelector('h1')!.textContent).toContain('Availability');
    expect(el.querySelectorAll('[data-testid="day-0"] .period').length).toBe(2);
    expect(el.querySelector('[data-testid="day-5"]')!.textContent).toContain('Not available');
    expect(el.querySelector('[data-testid="exception-0"]')!.textContent).toContain('Leave');
    expect(el.querySelector('[data-testid="slot-s2"]')!.classList).toContain('locked');
    expect(el.querySelector('[data-testid="remove-s2"]')).toBeNull();
    expect(el.querySelector('[data-testid="remove-s1"]')).not.toBeNull();
    expect(el.querySelector('[data-testid="save"]')!.textContent).toContain('Save and update times for 8 weeks');
  });

  it('blocks saving while two periods overlap, with the message beside the day', async () => {
    const { el, update } = await render();
    const end = el.querySelector('[data-testid="end-0-0"]') as HTMLInputElement;
    end.value = '17:00';
    end.dispatchEvent(new Event('input'));
    await update();
    expect(el.querySelector('[data-testid="day-error-0"]')!.textContent).toContain('These periods overlap.');
    expect((el.querySelector('[data-testid="save"]') as HTMLButtonElement).disabled).toBeTrue();
  });

  it('lists the booked times a change leaves outside the pattern before saving, then saves', async () => {
    const { el, update } = await render();
    (el.querySelector('[data-testid="toggle-1"] button') as HTMLButtonElement).click();
    await update();
    (el.querySelector('[data-testid="save"]') as HTMLButtonElement).click();
    const preview = backend.expectOne(`${API}/v1/staff/availability/preview`);
    expect(preview.request.body.weekly.some((p: { day: number }) => p.day === 1)).toBeFalse();
    preview.flush({ added: 0, removed: 20, affected: [slot({ slotId: 's2', status: 'booked' })] });
    await update();
    expect(el.querySelector('[data-testid="affected"]')!.textContent).toContain("1 booked times fall outside the times you removed. They won't be cancelled automatically");
    expect(el.querySelector('[data-testid="save"]')!.textContent).toContain('Save anyway');

    (el.querySelector('[data-testid="save"]') as HTMLButtonElement).click();
    const put = backend.expectOne((r) => r.method === 'PUT' && r.url === `${API}/v1/staff/availability`);
    put.flush({ added: 0, removed: 20, affected: [slot({ slotId: 's2', status: 'booked' })], availability: availability({ affected: [slot({ slotId: 's2', status: 'booked' })] }) });
    await update();
    expect(el.querySelector('[data-testid="affected"]')).not.toBeNull();
  });

  it('keeps the edits when saving fails', async () => {
    const { el, update } = await render(availability({ weekly: [] }));
    (el.querySelector('[data-testid="toggle-2"] button') as HTMLButtonElement).click();
    await update();
    (el.querySelector('[data-testid="save"]') as HTMLButtonElement).click();
    backend.expectOne(`${API}/v1/staff/availability/preview`).flush({ added: 40, removed: 0, affected: [] });
    const { body, options } = problem(500, 'Server error');
    backend.expectOne((r) => r.method === 'PUT').flush(body, options);
    await update();
    expect(el.querySelector('[data-testid="problem"]')).not.toBeNull();
    expect(el.querySelectorAll('[data-testid="day-2"] .period').length).toBe(1);
  });

  it('adds a one-off time in the therapist zone and removes a free one', async () => {
    const { el, update } = await render();
    const date = el.querySelector('[data-testid="one-off-date"]') as HTMLInputElement;
    date.value = '2026-11-05';
    date.dispatchEvent(new Event('input'));
    const time = el.querySelector('[data-testid="one-off-time"]') as HTMLInputElement;
    time.value = '14:00';
    time.dispatchEvent(new Event('input'));
    await update();
    (el.querySelector('[data-testid="one-off-add"]') as HTMLButtonElement).click();
    const add = backend.expectOne(`${API}/v1/staff/availability/slots`);
    expect(add.request.body).toEqual({ startsAt: '2026-11-05T11:00:00.000Z' });
    add.flush(slot({ slotId: 's3', source: 'manual' }));
    backend.expectOne(`${API}/v1/staff/availability`).flush(availability());
    await update();

    (el.querySelector('[data-testid="remove-s1"]') as HTMLButtonElement).click();
    backend.expectOne((r) => r.method === 'DELETE' && r.url === `${API}/v1/staff/availability/slots/s1`).flush(null);
    backend.expectOne(`${API}/v1/staff/availability`).flush(availability({ slots: [slot({ slotId: 's2', status: 'booked' })] }));
    await update();
    expect(el.querySelector('[data-testid="slot-s1"]')).toBeNull();
  });

  it('renders in Arabic', async () => {
    const { el } = await render(availability(), 'ar');
    expect(el.querySelector('h1')!.textContent).toContain('أوقات التوفر');
    expect(el.querySelector('[data-testid="save"]')!.textContent).toContain('حفظ وتحديث المواعيد لـ 8 أسابيع');
  });
});
