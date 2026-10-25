import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { clearCachedPlan, LEGACY_PLAN_KEY, readCachedPlan, SAFETY_PLAN_KEY, setPlanKeyStore, writeCachedPlan, type PlanKeyStore } from '@/components/safety/planCache';
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
  contacts: [{ name: 'أمي', phone: '+966551112233' }],
};

const numbers = (
  <a href="tel:997" data-testid="tel-emergency">
    الطوارئ 997
  </a>
);

/** Stands in for IndexedDB: holds the same non-extractable WebCrypto key the browser would. */
function memoryKeyStore(): PlanKeyStore & { key: CryptoKey | null } {
  const store = {
    key: null as CryptoKey | null,
    get: async () => store.key,
    put: async (key: CryptoKey) => {
      store.key = key;
    },
    clear: async () => {
      store.key = null;
    },
  };
  return store;
}

let keys = memoryKeyStore();
beforeEach(() => {
  resetMocks();
  keys = memoryKeyStore();
  setPlanKeyStore(keys);
});

describe('SafetyPlanView (P37)', () => {
  it('renders the fetched plan and stores it on the device with the locale', async () => {
    routeApi({ 'GET safety-plan': () => plan });
    renderWithIntl(<SafetyPlanView numbers={numbers} />, 'ar');
    expect(await screen.findByText('أسهر كثيرًا، وأنعزل في غرفتي.')).toBeInTheDocument();
    expect(screen.getByText('محفوظة على جهازك · تعمل بلا إنترنت')).toBeInTheDocument();
    expect(screen.getByTestId('plan-byline')).toHaveTextContent('كتبناها معًا أنا وأ. سارة · آخر تحديث 18 أكتوبر');
    expect(screen.getByText('أختي').tagName).toBe('LI');
    expect(screen.getByTestId('plan-contacts').querySelector('a[href="tel:+966551112233"]')).toHaveTextContent('أمي');
    expect(screen.getByText('صديقي خالد')).toBeInTheDocument();
    expect(screen.getByTestId('plan-professionals')).toContainElement(screen.getByTestId('tel-emergency'));
    expect(screen.getByTestId('plan-reasonToLive')).toHaveTextContent('سببي للاستمرار');
    await waitFor(() => expect(window.localStorage.getItem(SAFETY_PLAN_KEY)).not.toBeNull());
    // Encrypted on the device: no plan text in local storage, and the key cannot be exported.
    const raw = window.localStorage.getItem(SAFETY_PLAN_KEY)!;
    expect(raw).not.toContain('أسهر');
    expect(raw).not.toContain('966551112233');
    expect(keys.key?.extractable).toBe(false);
    const stored = await readCachedPlan();
    expect(stored?.locale).toBe('ar');
    expect(stored?.plan).toEqual(plan);
  });

  it('falls back to the copy saved on the device when offline', async () => {
    await writeCachedPlan(plan, 'ar', new Date('2026-10-19T08:00:00Z'));
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
    expect(puts[0]).toEqual({ sections: { ...plan.sections, reasonToLive: 'My niece.' }, contacts: plan.contacts });
    expect(screen.getByText('My niece.')).toBeInTheDocument();
  });

  it('opens the crisis screen when the plan edit is gated', async () => {
    routeApi({ 'GET safety-plan': () => plan, 'PUT safety-plan': () => ({ ...plan, crisis: true }) });
    renderWithIntl(<SafetyPlanView numbers={numbers} />, 'ar');
    await userEvent.click(await screen.findByRole('button', { name: 'تعديل الخطة' }));
    await userEvent.click(screen.getByRole('button', { name: 'حفظ الخطة' }));
    await waitFor(() => expect(router.replace).toHaveBeenCalledWith('/crisis'));
  });

  it('adds a personal contact with a phone number in edit mode', async () => {
    const puts: unknown[] = [];
    routeApi({
      'GET safety-plan': () => ({ ...plan, contacts: [] }),
      'PUT safety-plan': (body) => {
        puts.push(body);
        return { ...plan, contacts: (body as { contacts: unknown[] }).contacts };
      },
    });
    renderWithIntl(<SafetyPlanView numbers={numbers} />, 'en');
    await userEvent.click(await screen.findByRole('button', { name: 'Edit plan' }));
    await userEvent.click(screen.getByRole('button', { name: 'Add a contact' }));
    await userEvent.type(screen.getByRole('textbox', { name: 'Contact 1 name' }), 'Khalid');
    await userEvent.type(screen.getByRole('textbox', { name: 'Contact 1 mobile number' }), '+966550001111');
    await userEvent.click(screen.getByRole('button', { name: 'Save plan' }));
    expect(await screen.findByText('Your plan is saved.')).toBeInTheDocument();
    expect((puts[0] as { contacts: unknown }).contacts).toEqual([{ name: 'Khalid', phone: '+966550001111' }]);
    expect(screen.getByRole('link', { name: /Khalid/ })).toHaveAttribute('href', 'tel:+966550001111');
  });
});

describe('planCache', () => {
  beforeEach(() => {
    keys = memoryKeyStore();
    setPlanKeyStore(keys);
    window.localStorage.clear();
  });

  it('removes the old plaintext copy and clears the plan and its key at sign-out', async () => {
    window.localStorage.setItem(LEGACY_PLAN_KEY, JSON.stringify({ plan }));
    await writeCachedPlan(plan, 'en');
    expect(window.localStorage.getItem(LEGACY_PLAN_KEY)).toBeNull();
    await clearCachedPlan();
    expect(window.localStorage.getItem(SAFETY_PLAN_KEY)).toBeNull();
    expect(keys.key).toBeNull();
    expect(await readCachedPlan()).toBeNull();
  });

  it('cannot read the copy once the device key is gone', async () => {
    await writeCachedPlan(plan, 'en');
    keys.key = null;
    expect(await readCachedPlan()).toBeNull();
  });

  it('caches nothing when the browser has no key store', async () => {
    setPlanKeyStore(null);
    await writeCachedPlan(plan, 'en');
    expect(window.localStorage.getItem(SAFETY_PLAN_KEY)).toBeNull();
  });
});
