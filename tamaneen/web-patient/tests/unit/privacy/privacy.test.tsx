import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ApiError } from '@/lib/api/errors';
import type { AccessLogEntry, Consent } from '@/lib/api/types';
import { renderWithIntl } from '../intl';

const mocks = vi.hoisted(() => ({ api: vi.fn(), authApi: vi.fn(), assign: vi.fn() }));
vi.mock('@/lib/api/client', () => ({ api: mocks.api, authApi: mocks.authApi }));
vi.mock('@/i18n/navigation', () => ({ Link: 'a', useRouter: () => ({ replace: vi.fn(), refresh: vi.fn() }), usePathname: () => '/privacy' }));

import { AccessLog, actionKey } from '@/components/privacy/AccessLog';
import { ConsentsSection } from '@/components/privacy/ConsentsSection';
import { MyData } from '@/components/privacy/MyData';

const realLocation = window.location;
beforeAll(() => {
  Object.defineProperty(window, 'location', { configurable: true, value: { ...realLocation, assign: mocks.assign } });
});
afterAll(() => {
  Object.defineProperty(window, 'location', { configurable: true, value: realLocation });
});
beforeEach(() => {
  for (const m of Object.values(mocks)) m.mockReset();
  mocks.authApi.mockResolvedValue(undefined);
});

const consent = (key: Consent['key'], over: Partial<Consent> = {}): Consent => ({ key, required: false, granted: true, version: '1', updatedAt: null, withdrawable: true, ...over });
const consents = [consent('platform', { required: true, withdrawable: false }), consent('ai', { required: true }), consent('research', { granted: false }), consent('emergency_location')];

describe('ConsentsSection', () => {
  it('shows required consents as on, locked and labelled required', () => {
    renderWithIntl(<ConsentsSection initial={consents} />, 'ar');
    const platform = screen.getByRole('switch', { name: /شروط الخدمة وسياسة الخصوصية/ });
    expect(platform).toHaveAttribute('aria-checked', 'true');
    expect(platform).toBeDisabled();
    expect(within(screen.getByTestId('consent-platform')).getByText('مطلوبة')).toBeInTheDocument();
    expect(screen.getByRole('switch', { name: /المساعدة بالذكاء الاصطناعي/ })).toBeEnabled();
    expect(screen.getByRole('switch', { name: 'تحسين الخدمة (بيانات مجهولة)' })).toHaveAttribute('aria-checked', 'false');
  });

  it('saves other changes at once', async () => {
    mocks.api.mockResolvedValue([consents[0], consents[1], consent('research', { granted: true }), consents[3]]);
    renderWithIntl(<ConsentsSection initial={consents} />, 'en');
    await userEvent.click(screen.getByRole('switch', { name: 'Improving the service (anonymous data)' }));
    expect(mocks.api).toHaveBeenCalledWith('me/consents', { method: 'PUT', body: { items: [{ key: 'research', granted: true }] } });
    await waitFor(() => expect(screen.getByRole('switch', { name: 'Improving the service (anonymous data)' })).toHaveAttribute('aria-checked', 'true'));
  });

  it('explains what stops before withdrawing AI assistance', async () => {
    mocks.api.mockResolvedValue([consents[0], consent('ai', { granted: false }), consents[2], consents[3]]);
    renderWithIntl(<ConsentsSection initial={consents} />, 'ar');
    await userEvent.click(screen.getByRole('switch', { name: /المساعدة بالذكاء الاصطناعي/ }));
    const dialog = screen.getByRole('dialog', { name: 'إيقاف المساعدة بالذكاء الاصطناعي؟' });
    expect(dialog).toHaveTextContent('ستتوقف الميزات المدعومة بالذكاء الاصطناعي');
    expect(mocks.api).not.toHaveBeenCalled();

    await userEvent.click(within(dialog).getByRole('button', { name: 'إلغاء' }));
    expect(screen.queryByRole('dialog')).toBeNull();
    expect(screen.getByRole('switch', { name: /المساعدة بالذكاء الاصطناعي/ })).toHaveAttribute('aria-checked', 'true');

    await userEvent.click(screen.getByRole('switch', { name: /المساعدة بالذكاء الاصطناعي/ }));
    await userEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'إيقاف المساعدة' }));
    expect(mocks.api).toHaveBeenCalledWith('me/consents', { method: 'PUT', body: { items: [{ key: 'ai', granted: false }] } });
    await waitFor(() => expect(screen.getByRole('switch', { name: /المساعدة بالذكاء الاصطناعي/ })).toHaveAttribute('aria-checked', 'false'));
  });

  it('offers the delete path from the AI dialog (PB-56)', async () => {
    const opened = vi.fn();
    window.addEventListener('tamaneen:open-delete-account', opened);
    renderWithIntl(<ConsentsSection initial={consents} />, 'en');
    await userEvent.click(screen.getByRole('switch', { name: /AI assistance/ }));
    await userEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'I would rather delete my account' }));
    expect(opened).toHaveBeenCalled();
    expect(mocks.api).not.toHaveBeenCalled();
    window.removeEventListener('tamaneen:open-delete-account', opened);
  });

  it('asks before withdrawing the emergency location consent', async () => {
    mocks.api.mockResolvedValue([consents[0], consents[1], consents[2], consent('emergency_location', { granted: false })]);
    renderWithIntl(<ConsentsSection initial={consents} />, 'ar');
    await userEvent.click(screen.getByRole('switch', { name: 'موقعي عند الطوارئ' }));
    const dialog = screen.getByRole('dialog', { name: 'إيقاف مشاركة موقعك عند الطوارئ؟' });
    expect(mocks.api).not.toHaveBeenCalled();
    await userEvent.click(within(dialog).getByRole('button', { name: 'إيقاف مشاركة الموقع' }));
    expect(mocks.api).toHaveBeenCalledWith('me/consents', { method: 'PUT', body: { items: [{ key: 'emergency_location', granted: false }] } });
  });
});

const nowIso = '2026-10-20T12:00:00Z';
const log: AccessLogEntry[] = [
  { actor: 'therapist', action: 'patient.file_opened', atUtc: '2026-10-20T06:12:00Z' },
  { actor: 'supervisor', action: 'triage.reviewed', atUtc: '2026-10-19T12:40:00Z' },
  { actor: 'system', action: 'intake.summary_generated', atUtc: '2026-10-19T11:05:00Z' },
  { actor: 'crisis_service', action: 'billing.something_new', atUtc: '2026-10-01T11:05:00Z' },
];

describe('AccessLog', () => {
  it('maps known action codes and falls back for unknown ones', () => {
    expect(actionKey('crisis.contact_revealed')).toBe('contactRevealed');
    expect(actionKey('crisis.acknowledged')).toBe('crisisAcknowledged');
    expect(actionKey('anything.else')).toBe('unknown');
  });

  it('shows the latest three in Arabic, with the full log on request', async () => {
    renderWithIntl(<AccessLog entries={log} nowIso={nowIso} timeZone="Asia/Riyadh" />, 'ar');
    expect(screen.getByText('أخصائيتك')).toBeInTheDocument();
    expect(screen.getByText('فتحت الملف السريري')).toBeInTheDocument();
    expect(screen.getByText('اليوم 9:12 ص')).toBeInTheDocument();
    expect(screen.getByText('راجع ملف الفرز')).toBeInTheDocument();
    expect(screen.getByText('أمس 3:40 م')).toBeInTheDocument();
    expect(screen.getByText('أعدّ ملخص المحادثة')).toBeInTheDocument();
    expect(screen.queryByText('فريق الطوارئ')).toBeNull();

    const more = screen.getByRole('button', { name: 'السجل كاملًا' });
    expect(more).toHaveAttribute('aria-expanded', 'false');
    await userEvent.click(more);
    expect(screen.getByText('فريق الطوارئ')).toBeInTheDocument();
    expect(screen.getByText('اطّلع على ملفك')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'عرض أقل' })).toHaveAttribute('aria-expanded', 'true');
  });

  it('reads in English', () => {
    renderWithIntl(<AccessLog entries={log} nowIso={nowIso} timeZone="Asia/Riyadh" />, 'en');
    expect(screen.getByText('Your specialist')).toBeInTheDocument();
    expect(screen.getByText('Opened your clinical file')).toBeInTheDocument();
    expect(screen.getByText('Today 9:12 AM')).toBeInTheDocument();
  });
});

describe('MyData', () => {
  const none = { exportRequestedAt: null, exportReadyBy: null, exportReady: false };

  it('requests a copy of the data and shows when it will be ready', async () => {
    mocks.api.mockResolvedValue({ exportRequestedAt: nowIso, exportReadyBy: '2026-10-27T12:00:00Z', exportReady: false });
    renderWithIntl(<MyData initialExport={none} timeZone="Asia/Riyadh" />, 'ar');
    expect(screen.getByText('جاهزة خلال 7 أيام')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: /تنزيل نسخة من بياناتي/ }));
    const dialog = screen.getByRole('dialog', { name: 'تنزيل نسخة من بياناتك' });
    await userEvent.click(within(dialog).getByRole('button', { name: 'اطلب النسخة' }));
    expect(mocks.api).not.toHaveBeenCalled();
    await userEvent.type(within(dialog).getByLabelText('كلمة المرور للتأكيد'), 'my-secret-password');
    await userEvent.click(within(dialog).getByRole('button', { name: 'اطلب النسخة' }));
    expect(mocks.api).toHaveBeenCalledWith('me/data-export', { method: 'POST', body: { password: 'my-secret-password' } });
    expect(await screen.findByText('طلبتها في 20 أكتوبر · جاهزة بحلول 27 أكتوبر')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /تنزيل نسخة من بياناتي/ })).toBeDisabled();
  });

  it('deletes the account only with the password and the typed confirmation, then signs out', async () => {
    mocks.api.mockResolvedValue(undefined);
    renderWithIntl(<MyData initialExport={none} timeZone="Asia/Riyadh" />, 'ar');
    expect(screen.getByText('نحتفظ فقط بما يلزمه القانون')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: /حذف حسابي/ }));
    const dialog = screen.getByRole('dialog', { name: 'حذف حسابك؟' });
    await userEvent.click(within(dialog).getByRole('button', { name: 'حذف حسابي نهائيًا' }));
    expect(within(dialog).getAllByRole('alert')[0]).toHaveTextContent('أدخل كلمة المرور.');
    expect(mocks.api).not.toHaveBeenCalled();

    await userEvent.type(within(dialog).getByLabelText('كلمة المرور للتأكيد'), 'my-secret-password');
    await userEvent.click(within(dialog).getByRole('button', { name: 'حذف حسابي نهائيًا' }));
    expect(within(dialog).getByRole('alert')).toHaveTextContent('اكتب «حذف» كما هي للتأكيد.');
    expect(mocks.api).not.toHaveBeenCalled();
    await userEvent.type(within(dialog).getByLabelText('اكتب «حذف» للتأكيد'), 'حذف');
    await userEvent.click(within(dialog).getByRole('button', { name: 'حذف حسابي نهائيًا' }));
    expect(mocks.api).toHaveBeenCalledWith('me/delete-account', { method: 'POST', body: { password: 'my-secret-password' } });
    await waitFor(() => expect(mocks.assign).toHaveBeenCalledWith('/ar?deleted=1'));
    expect(mocks.authApi).toHaveBeenCalledWith('logout');
  });

  it('shows a wrong password in the dialog and stays signed in (English)', async () => {
    mocks.api.mockRejectedValue(new ApiError(400, { title: 'The password is incorrect.', status: 400 }));
    renderWithIntl(<MyData initialExport={none} timeZone="Asia/Riyadh" />, 'en');
    await userEvent.click(screen.getByRole('button', { name: /Delete my account/ }));
    const dialog = screen.getByRole('dialog', { name: 'Delete your account?' });
    await userEvent.type(within(dialog).getByLabelText('Password to confirm'), 'wrong');
    await userEvent.type(within(dialog).getByLabelText('Type “DELETE” to confirm'), 'delete');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Delete my account for good' }));
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('The password is incorrect.');
    expect(mocks.authApi).not.toHaveBeenCalled();
    expect(mocks.assign).not.toHaveBeenCalled();
  });

  it('explains that deletion waits while a crisis event is open (EC-55)', async () => {
    mocks.api.mockRejectedValue(new ApiError(409, { title: 'Conflict', status: 409, code: 'error.privacy.deletion_crisis_open', detail: 'open request' }));
    renderWithIntl(<MyData initialExport={none} timeZone="Asia/Riyadh" />, 'ar');
    await userEvent.click(screen.getByRole('button', { name: /حذف حسابي/ }));
    const dialog = screen.getByRole('dialog', { name: 'حذف حسابك؟' });
    await userEvent.type(within(dialog).getByLabelText('كلمة المرور للتأكيد'), 'my-secret-password');
    await userEvent.type(within(dialog).getByLabelText('اكتب «حذف» للتأكيد'), 'حذف');
    await userEvent.click(within(dialog).getByRole('button', { name: 'حذف حسابي نهائيًا' }));
    const blocked = await within(dialog).findByTestId('delete-blocked');
    expect(blocked).toHaveTextContent('لا يمكن حذف الحساب الآن');
    expect(within(blocked).getByText('افتح شاشة الطوارئ')).toHaveAttribute('href', '/crisis');
    expect(mocks.authApi).not.toHaveBeenCalled();
  });
});
