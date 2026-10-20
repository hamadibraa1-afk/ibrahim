import { DRAFT_TTL_MS, clearDraft, loadDraft, saveDraft } from '@/components/pwa/drafts';

beforeEach(() => window.localStorage.clear());

describe('drafts', () => {
  it('keeps unsent text for 10 minutes', () => {
    saveDraft('intake', 'نص لم يُرسل', 1000);
    expect(loadDraft('intake', 1000 + DRAFT_TTL_MS)).toBe('نص لم يُرسل');
    expect(loadDraft('intake', 1001 + DRAFT_TTL_MS)).toBe('');
    expect(window.localStorage.length).toBe(0);
  });

  it('removes empty drafts and clears on demand', () => {
    saveDraft('x', 'a');
    clearDraft('x');
    expect(loadDraft('x')).toBe('');
    saveDraft('x', 'a');
    saveDraft('x', '   ');
    expect(window.localStorage.length).toBe(0);
  });
});
