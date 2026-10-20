import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { SAFETY_PLAN_KEY } from '@/components/safety/planCache';
import type { SafetyPlan } from '@/lib/api/types';
import { renderWithIntl } from '../intl';
import { api, Link, resetMocks, routeApi, router } from '../intake/mocks';

vi.mock('@/lib/api/client', () => ({ api: (...args: unknown[]) => api(...args) }));
vi.mock('@/i18n/navigation', () => ({ useRouter: () => router, Link }));

import { SafetyPlanView } from '@/components/safety/SafetyPlanView';

const plan: SafetyPlan = {
  coAuthor: 'أ. سارة',
  updatedAt: '2026-10-18T09:00:00Z',
  sections: {
    warningSigns: 'أسهر كثيرًا، وأنعزل في غرفتي.',
    copingAlone: 'المشي 10 دقائق، والتنفس 4-4-6.',
    distractions: 'بيت أختي، والمقهى القريب.',
    peopleToAsk: 'أختي، صديقي خالد',
    professionals: '',
    safeEnvironment: 'أعطي الأدوية الزائدة لأختي تحتفظ بها.',
    reasonToLive: 'ابنة أختي الصغيرة، وحلمي أن أسافر.',
  },
};

const numbers = (
  <a href="tel:997" data-testid="tel-emergency">
    الطوارئ 997
  </a>
);

beforeEach(resetMocks);

describe('SafetyPlanView (P37)', () => {
  it('renders the fetched plan and stores it on the device with the locale', async () => {
    routeApi({ 'GET safety-plan': () => plan });
    renderWithIntl(<SafetyPlanView numbers={numbers} />, 'ar');
    expect(await screen.findByText('أسهر كثيرًا، وأنعزل في غرفتي.')).toBeInTheDocument();
    expect(screen.getByText('محفوظة على جهازك · تعمل بلا إنترنت')).toBeInTheDocument();
    expect(screen.getByTestId('plan-byline')).toHaveTextContent('كتبناها معًا أنا وأ. سارة · آخر تحديث 18 أكتوبر');
    expect(screen.getByText('أختي').tagName).toBe('LI');
    expect(screen.getByText('صديقي خالد')).toBeInTheDocument();
    expect(screen.getByTestId('plan-professionals')).toContainElement(screen.getByTestId('tel-emergency'));
    expect(screen.getByTestId('plan-reasonToLive')).toHaveTextContent('سببي للاستمرار');
    const stored = JSON.parse(window.localStorage.getItem(SAFETY_PLAN_KEY)!);
    expect(stored.locale).toBe('ar');
    expect(stored.plan).toEqual(plan);
  });

  it('falls back to the copy saved on the device when offline', async () => {
    window.localStorage.setItem(SAFETY_PLAN_KEY, JSON.stringify({ locale: 'ar', savedAt: '2026-10-19T08:00:00Z', plan }));
    routeApi({
      'GET safety-plan': () => {
        throw new TypeError('Failed to fetch');
      },
    });
    renderWithIntl(<SafetyPlanView numbers={numbers} />, 'ar');
    expect(await screen.findByTestId('plan-offline')).toHaveTextContent('لا يوجد اتصال الآن. هذه آخر نسخة محفوظة على جهازك');
    expect(screen.getByText('ابنة أختي الصغيرة، وحلمي أن أسافر.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'تعديل الخطة' })).toBeDisabled();
    expect(screen.getByText('يمكنك تعديل الخطة عند عودة الاتصال.')).toBeInTheDocument();
  });

  it('says so when there is neither a connection nor a saved copy, and still shows the numbers', async () => {
    routeApi({
      'GET safety-plan': () => {
        throw new TypeError('Failed to fetch');
      },
    });
    renderWithIntl(<SafetyPlanView numbers={numbers} />, 'en');
    expect(await screen.findByText(/We couldn't load your plan/)).toBeInTheDocument();
    expect(screen.getByTestId('tel-emergency')).toBeInTheDocument();
  });

  it('edits and saves the whole plan through the gate (English)', async () => {
    const puts: unknown[] = [];
    routeApi({
      'GET safety-plan': () => plan,
      'PUT safety-plan': (body) => {
        puts.push(body);
        return { ...plan, sections: { ...plan.sections, reasonToLive: 'My niece.' } };
      },
    });
    renderWithIntl(<SafetyPlanView numbers={numbers} />, 'en');
    await userEvent.click(await screen.findByRole('button', { name: 'Edit plan' }));
    const reason = screen.getByRole('textbox', { name: 'My reason to keep going' });
    await userEvent.clear(reason);
    await userEvent.type(reason, 'My niece.');
    await userEvent.click(screen.getByRole('button', { name: 'Save plan' }));
    expect(await screen.findByText('Your plan is saved.')).toBeInTheDocument();
    expect(puts[0]).toEqual({ sections: { ...plan.sections, reasonToLive: 'My niece.' } });
    expect(screen.getByText('My niece.')).toBeInTheDocument();
  });

  it('opens the crisis screen when the plan edit is gated', async () => {
    routeApi({ 'GET safety-plan': () => plan, 'PUT safety-plan': () => ({ ...plan, crisis: true }) });
    renderWithIntl(<SafetyPlanView numbers={numbers} />, 'ar');
    await userEvent.click(await screen.findByRole('button', { name: 'تعديل الخطة' }));
    await userEvent.click(screen.getByRole('button', { name: 'حفظ الخطة' }));
    await waitFor(() => expect(router.replace).toHaveBeenCalledWith('/crisis'));
  });
});
