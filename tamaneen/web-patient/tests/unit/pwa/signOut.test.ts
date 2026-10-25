import { loadDraft, saveDraft } from '@/components/pwa/drafts';
import { clearLocalCopies } from '@/components/pwa/signOut';

describe('clearLocalCopies (sign-out, PB-61)', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    window.localStorage.clear();
  });

  it('deletes the runtime page cache and every draft, but keeps the public precache and other storage', async () => {
    const names = ['tamaneen-precache-v1', 'tamaneen-runtime-v1', 'tamaneen-runtime-v0', 'other-app'];
    const deleted: string[] = [];
    vi.stubGlobal('caches', { keys: async () => names, delete: async (n: string) => (deleted.push(n), true) });
    saveDraft('intake', 'نص لم يُرسل');
    saveDraft('triage-addendum', 'إضافة');
    window.localStorage.setItem('unrelated', 'x');

    await clearLocalCopies();

    expect(deleted.sort()).toEqual(['tamaneen-runtime-v0', 'tamaneen-runtime-v1']);
    expect(loadDraft('intake')).toBe('');
    expect(loadDraft('triage-addendum')).toBe('');
    expect(window.localStorage.getItem('unrelated')).toBe('x');
  });

  it('never throws when CacheStorage is missing or blocked', async () => {
    vi.stubGlobal('caches', undefined);
    await expect(clearLocalCopies()).resolves.toBeUndefined();
    vi.stubGlobal('caches', { keys: async () => { throw new Error('SecurityError'); } });
    await expect(clearLocalCopies()).resolves.toBeUndefined();
  });
});
