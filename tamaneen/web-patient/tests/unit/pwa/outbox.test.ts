import { ApiError } from '@/lib/api/errors';
import {
  OUTBOX_LOCAL_KEY,
  OUTBOX_SENT_EVENT,
  clearOutbox,
  discardOutboxItem,
  flushOutbox,
  localStorageStore,
  outboxKeptOnDevice,
  outboxItems,
  resetOutbox,
  retryOutboxItem,
  setOutboxOwner,
  setOutboxStore,
  submitWrite,
  type OutboxItem,
  type OutboxStore,
} from '@/components/pwa/outbox';
import { clearLocalCopies } from '@/components/pwa/signOut';
import { clearLocalSafetyData } from '@/components/safety/clearOnSignOut';

const api = vi.fn();
vi.mock('@/lib/api/client', () => ({ api: (...args: unknown[]) => api(...args) }));

function memoryStore(): OutboxStore & { rows: Map<string, OutboxItem> } {
  const rows = new Map<string, OutboxItem>();
  return {
    rows,
    load: async () => [...rows.values()].map((r) => structuredClone(r)),
    put: async (item) => void rows.set(item.id, structuredClone(item)),
    remove: async (id) => void rows.delete(id),
    clear: async () => rows.clear(),
  };
}

const problem = (status: number, code: string, detail = 'رُفض الطلب') => new ApiError(status, { title: 't', status, code, detail });

function setOnline(value: boolean) {
  Object.defineProperty(window.navigator, 'onLine', { configurable: true, get: () => value });
}

const mood = { kind: 'mood' as const, method: 'POST' as const, path: 'mood', body: { score: 6, tags: [], note: 'يوم هادئ' } };

describe('patient outbox (P66)', () => {
  let store: ReturnType<typeof memoryStore>;

  beforeEach(() => {
    api.mockReset();
    store = memoryStore();
    setOutboxStore(store);
    resetOutbox();
    setOnline(true);
  });
  afterEach(() => {
    setOutboxStore(null);
    resetOutbox();
    window.localStorage.clear();
  });

  it('sends at once when online, with an Idempotency-Key, and keeps nothing', async () => {
    setOutboxOwner('P-AAAA');
    api.mockResolvedValueOnce({ crisis: false, entry: { id: 'e1' } });
    const result = await submitWrite(mood);
    expect(result).toEqual({ status: 'sent', response: { crisis: false, entry: { id: 'e1' } } });
    const [path, options] = api.mock.calls[0]!;
    expect(path).toBe('mood');
    expect(options).toMatchObject({ method: 'POST', body: mood.body, navigate: false });
    expect(options.idempotencyKey).toMatch(/^[0-9a-f-]{36}$/);
    expect(outboxItems()).toEqual([]);
    expect(store.rows.size).toBe(0);
  });

  it('stores the write on the device while offline and sends it later with the same key', async () => {
    setOutboxOwner('P-AAAA');
    setOnline(false);
    const queued = await submitWrite(mood);
    expect(queued.status).toBe('queued');
    expect(api).not.toHaveBeenCalled();
    expect([...store.rows.values()]).toHaveLength(1);

    const sent = vi.fn();
    window.addEventListener(OUTBOX_SENT_EVENT, sent);
    setOnline(true);
    api.mockRejectedValueOnce(new TypeError('Failed to fetch')).mockResolvedValueOnce({ crisis: true, entry: { id: 'e1' } });
    await flushOutbox({ force: true });
    expect(outboxItems()[0]).toMatchObject({ state: 'waiting', attempts: 1 });
    await flushOutbox({ force: true });
    window.removeEventListener(OUTBOX_SENT_EVENT, sent);

    const keys = api.mock.calls.map(([, o]) => o.idempotencyKey);
    expect(new Set(keys).size).toBe(1);
    expect(keys[0]).toBe(queued.status === 'queued' ? queued.item.id : '');
    expect(outboxItems()).toEqual([]);
    // The runner opens the crisis screen from this event when the gate answered crisis.
    expect(sent).toHaveBeenCalledTimes(1);
    expect((sent.mock.calls[0]![0] as CustomEvent).detail.response).toEqual({ crisis: true, entry: { id: 'e1' } });
  });

  it('waits on the retry timer (2, 10, 30 s) unless forced, and keeps the order', async () => {
    vi.useFakeTimers();
    try {
      setOutboxOwner('P-AAAA');
      api.mockRejectedValue(problem(503, 'error.crisis.unavailable'));
      await submitWrite(mood);
      await submitWrite({ ...mood, body: { score: 2 } });
      // The second write stopped behind the first; neither was lost.
      expect(outboxItems().map((i) => i.state)).toEqual(['waiting', 'waiting']);
      const calls = api.mock.calls.length;

      await flushOutbox();
      expect(api.mock.calls.length).toBe(calls);
      vi.advanceTimersByTime(10_001);
      await flushOutbox();
      expect(api.mock.calls.length).toBe(calls + 1);

      api.mockReset();
      api.mockResolvedValue({ ok: true });
      await flushOutbox({ force: true });
      expect(api.mock.calls.map(([, o]) => o.body)).toEqual([mood.body, { score: 2 }]);
      expect(outboxItems()).toEqual([]);
    } finally {
      vi.useRealTimers();
    }
  });

  it('shows a refusal on the form when the patient is there, and keeps a later refusal as "not sent" until discarded', async () => {
    setOutboxOwner('P-AAAA');
    api.mockRejectedValueOnce(problem(400, 'error.validation'));
    const rejected = await submitWrite(mood);
    expect(rejected.status).toBe('rejected');
    expect(outboxItems()).toEqual([]);

    setOnline(false);
    await submitWrite({ ...mood, kind: 'homework-checkin', path: 'homework/h1/checkins', label: 'تمرين التنفس' });
    setOnline(true);
    api.mockRejectedValueOnce(problem(409, 'error.homework.ended', 'انتهى هذا التمرين'));
    await flushOutbox({ force: true });
    const [failed] = outboxItems();
    expect(failed).toMatchObject({ state: 'failed', error: 'انتهى هذا التمرين', label: 'تمرين التنفس' });

    // Failed items are not retried on their own.
    await flushOutbox({ force: true });
    expect(api).toHaveBeenCalledTimes(2);

    api.mockResolvedValueOnce({});
    await retryOutboxItem(failed!.id);
    expect(api.mock.calls[2]![1].idempotencyKey).toBe(failed!.id);
    expect(outboxItems()).toEqual([]);

    setOnline(false);
    await submitWrite(mood);
    await discardOutboxItem(outboxItems()[0]!.id);
    expect(outboxItems()).toEqual([]);
    expect(store.rows.size).toBe(0);
  });

  it('treats "still in progress" and an expired sign-in as retryable', async () => {
    setOutboxOwner('P-AAAA');
    api.mockRejectedValueOnce(problem(409, 'error.idempotency.in_progress'));
    expect((await submitWrite(mood)).status).toBe('queued');
    api.mockRejectedValueOnce(problem(401, 'error.auth.session_revoked'));
    await flushOutbox({ force: true });
    expect(outboxItems()[0]).toMatchObject({ state: 'waiting', attempts: 2 });
  });

  it("never lists or sends another patient's queue on the same device", async () => {
    setOutboxOwner('P-AAAA');
    setOnline(false);
    await submitWrite(mood);
    setOutboxOwner('P-BBBB');
    expect(outboxItems()).toEqual([]);
    setOnline(true);
    await flushOutbox({ force: true });
    expect(api).not.toHaveBeenCalled();

    // Reloaded from storage, the owner still decides.
    resetOutbox();
    setOutboxOwner('P-BBBB');
    await flushOutbox({ force: true });
    expect(api).not.toHaveBeenCalled();
    setOutboxOwner('P-AAAA');
    api.mockResolvedValueOnce({});
    await flushOutbox({ force: true });
    expect(api).toHaveBeenCalledTimes(1);
  });

  it('sends nothing without a signed-in owner', async () => {
    setOnline(false);
    setOutboxOwner('P-AAAA');
    await submitWrite(mood);
    setOutboxOwner(null);
    setOnline(true);
    await flushOutbox({ force: true });
    expect(api).not.toHaveBeenCalled();
  });

  it('is cleared by sign-out and by account deletion', async () => {
    setOutboxOwner('P-AAAA');
    setOnline(false);
    await submitWrite(mood);
    vi.stubGlobal('caches', undefined);
    await clearLocalCopies();
    vi.unstubAllGlobals();
    expect(outboxItems()).toEqual([]);
    expect(store.rows.size).toBe(0);

    await submitWrite(mood);
    await clearLocalSafetyData();
    expect(outboxItems()).toEqual([]);
    expect(store.rows.size).toBe(0);
  });
});

describe('localStorage fallback', () => {
  afterEach(() => {
    setOutboxStore(null);
    resetOutbox();
    window.localStorage.clear();
  });

  it('without IndexedDB keeps the queue in memory only, never in plain localStorage, and says so', async () => {
    setOutboxStore(null);
    resetOutbox();
    expect(typeof indexedDB).toBe('undefined');
    Object.defineProperty(window.navigator, 'onLine', { configurable: true, get: () => false });
    setOutboxOwner('P-AAAA');
    await submitWrite(mood);
    expect(window.localStorage.getItem(OUTBOX_LOCAL_KEY)).toBeNull();
    expect(outboxItems()).toHaveLength(1);
    expect(outboxKeptOnDevice()).toBe(false);

    await clearOutbox();
    expect(outboxItems()).toHaveLength(0);
    Object.defineProperty(window.navigator, 'onLine', { configurable: true, get: () => true });
  });

  it('moves items an earlier version left in plain localStorage into memory and removes them from the disk', async () => {
    setOutboxStore(null);
    resetOutbox();
    const legacy = { id: 'k-legacy', owner: 'P-AAAA', kind: 'mood', method: 'POST', path: 'mood', body: { score: 5 }, createdAt: new Date().toISOString(), attempts: 0 };
    window.localStorage.setItem(OUTBOX_LOCAL_KEY, JSON.stringify([legacy]));
    Object.defineProperty(window.navigator, 'onLine', { configurable: true, get: () => false });
    setOutboxOwner('P-AAAA');
    await flushOutbox();
    expect(outboxItems().map((i) => i.id)).toContain('k-legacy');
    expect(window.localStorage.getItem(OUTBOX_LOCAL_KEY)).toBeNull();
    await clearOutbox();
    Object.defineProperty(window.navigator, 'onLine', { configurable: true, get: () => true });
  });
});
