'use client';

import { useLocale, useTranslations } from 'next-intl';
import { useEffect, useState, type ReactNode } from 'react';
import { useRouter } from '@/i18n/navigation';
import { isLocale, routing } from '@/i18n/routing';
import { api } from '@/lib/api/client';
import { ApiError } from '@/lib/api/errors';
import { OUTBOX_SENT_EVENT, submitWrite, useOutbox, type OutboxSentDetail } from '@/components/pwa/outbox';
import type { PlanContact, SafetyPlan, SafetyPlanSection } from '@/lib/api/types';
import { formatDate } from '@/lib/format';
import { apiMessage, isCrisis, isNetworkError } from '@/components/pwa/errors';
import { Button } from '@/components/ui/Button';
import { Notice } from '@/components/ui/Notice';
import { Spinner } from '@/components/ui/Spinner';
import { TextArea, TextField } from '@/components/ui/TextField';
import { CallLink } from '@/components/crisis/CallLink';
import { readCachedPlan, writeCachedPlan, type CachedSafetyPlan } from './planCache';

const NUMBERED: SafetyPlanSection[] = ['warningSigns', 'copingAlone', 'distractions', 'peopleToAsk', 'professionals', 'safeEnvironment'];
const ALL: SafetyPlanSection[] = [...NUMBERED, 'reasonToLive'];
const BADGE: Record<SafetyPlanSection, string> = {
  warningSigns: 'bg-tint-sand text-accent',
  copingAlone: 'bg-tint-sand text-accent',
  distractions: 'bg-tint-teal text-primary-strong',
  peopleToAsk: 'bg-tint-teal text-primary-strong',
  professionals: 'bg-help/10 text-help',
  safeEnvironment: 'bg-tint-teal text-primary-strong',
  reasonToLive: '',
};
/** Same limit as the server and the therapist's S35 builder («نصوص حتى 1000 حرف»). */
const MAX_SECTION = 1000;
const MAX_CONTACTS = 5;
const MAX_CONTACT_NAME = 100;

type Source = 'network' | 'cache' | 'offline-cache';

function people(text: string): string[] {
  return text
    .split(/[\n،,؛;]+/)
    .map((p) => p.trim())
    .filter(Boolean);
}

function emptyPlan(plan: SafetyPlan): boolean {
  return ALL.every((s) => !(plan.sections[s] ?? '').trim()) && (plan.contacts ?? []).length === 0;
}

/**
 * P37 Safety plan. Shows the last copy saved on this device at once, refreshes it from the API, and keeps
 * working from the saved copy when offline or when the API fails. `numbers` are the server-rendered
 * emergency tel: links, so they show even before (or without) JavaScript. Edits work offline too: they go
 * through the outbox (P66) and the plan shows them, marked as waiting to send, until the server has them.
 */
export function SafetyPlanView({ numbers }: { numbers: ReactNode }) {
  const t = useTranslations('safetyPlan');
  const tErrors = useTranslations('errors');
  const current = useLocale();
  const locale = isLocale(current) ? current : routing.defaultLocale;
  const router = useRouter();

  const [plan, setPlan] = useState<SafetyPlan | null>(null);
  const [source, setSource] = useState<Source | null>(null);
  const [savedAt, setSavedAt] = useState<string | null>(null);
  const [state, setState] = useState<'loading' | 'ready' | 'unavailable'>('loading');
  const [editing, setEditing] = useState(false);
  const [drafts, setDrafts] = useState<Record<SafetyPlanSection, string> | null>(null);
  const [contactDrafts, setContactDrafts] = useState<PlanContact[]>([]);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);
  // The newest edit still waiting on the phone is what the plan will be once it is sent: show that.
  const pendingEdit = useOutbox().filter((i) => i.kind === 'safety-plan' && i.state === 'waiting').at(-1);
  const pendingBody = pendingEdit?.body as { sections?: Partial<Record<SafetyPlanSection, string>>; contacts?: PlanContact[] } | undefined;

  useEffect(() => {
    let cancelled = false;
    let fresh = false;
    let cached: CachedSafetyPlan | null = null;
    // The device copy is decrypted while the network request runs; the network answer always wins.
    const fromDevice = readCachedPlan().then((copy) => {
      cached = copy;
      if (cancelled || fresh || !copy) return;
      setPlan(copy.plan);
      setSavedAt(copy.savedAt);
      setSource('cache');
      setState('ready');
    });
    api<SafetyPlan>('safety-plan')
      .then((response) => {
        if (cancelled) return;
        fresh = true;
        const latest = { ...response, contacts: response.contacts ?? [] };
        void writeCachedPlan(latest, locale);
        setPlan(latest);
        setSource('network');
        setState('ready');
      })
      .catch(async () => {
        await fromDevice;
        if (cancelled) return;
        if (cached) setSource('offline-cache');
        else setState('unavailable');
      });
    return () => {
      cancelled = true;
    };
  }, [locale]);

  // A queued edit arrives: the server's answer becomes the plan and the saved copy.
  useEffect(() => {
    const sent = (event: Event) => {
      const { item, response } = (event as CustomEvent<OutboxSentDetail>).detail;
      if (item.kind !== 'safety-plan' || isCrisis(response) || !response) return;
      const updated = { ...(response as SafetyPlan), contacts: (response as SafetyPlan).contacts ?? [] };
      void writeCachedPlan(updated, locale);
      setPlan(updated);
      setSource('network');
    };
    window.addEventListener(OUTBOX_SENT_EVENT, sent);
    return () => window.removeEventListener(OUTBOX_SENT_EVENT, sent);
  }, [locale]);

  const shown: SafetyPlan | null = plan && pendingBody ? { ...plan, sections: { ...plan.sections, ...(pendingBody.sections ?? {}) }, contacts: pendingBody.contacts ?? plan.contacts } : plan;

  function startEdit() {
    if (!shown) return;
    setDrafts(Object.fromEntries(ALL.map((s) => [s, shown.sections[s] ?? ''])) as Record<SafetyPlanSection, string>);
    setContactDrafts((shown.contacts ?? []).map((c) => ({ ...c })));
    setEditing(true);
    setSaved(false);
    setError(null);
  }

  async function save() {
    if (!drafts) return;
    setBusy(true);
    setError(null);
    try {
      const sections = Object.fromEntries(ALL.map((s) => [s, drafts[s].trim()])) as Record<SafetyPlanSection, string>;
      const contacts = contactDrafts.map((c) => ({ name: c.name.trim(), phone: c.phone.trim() })).filter((c) => c.name || c.phone);
      // The version this edit is based on (shared with the therapist's S35): a stale one is refused with 412.
      const ifMatch = plan?.version ? `"${plan.version}"` : undefined;
      const result = await submitWrite<SafetyPlan>({ kind: 'safety-plan', method: 'PUT', path: 'safety-plan', body: { sections, contacts }, ifMatch });
      if (result.status === 'rejected') {
        const e = result.error;
        setError(apiMessage(e) ?? (isNetworkError(e) ? tErrors('network') : tErrors('generic')));
        if (e instanceof ApiError && e.status === 412) {
          // The therapist saved a newer version: show it behind the patient's draft, which stays for them to re-save.
          api<SafetyPlan>('safety-plan')
            .then((latest) => {
              const updated = { ...latest, contacts: latest.contacts ?? [] };
              void writeCachedPlan(updated, locale);
              setPlan(updated);
            })
            .catch(() => undefined);
        }
        return;
      }
      if (result.status === 'queued') {
        setEditing(false);
        return;
      }
      const response = result.response;
      if (isCrisis(response)) {
        router.replace('/crisis');
        return;
      }
      const updated = { ...response, contacts: response.contacts ?? [] };
      void writeCachedPlan(updated, locale);
      setPlan(updated);
      setEditing(false);
      setSaved(true);
    } finally {
      setBusy(false);
    }
  }

  const header = (
    <div className="flex flex-col gap-2">
      <span className="inline-flex items-center gap-2 self-start rounded-full bg-tint-teal px-4 py-1.5 text-sm font-semibold text-primary-strong">
        <svg aria-hidden="true" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round">
          <path d="M5 12l4 4 10-10" />
        </svg>
        {t('badge')}
      </span>
      <h1 className="text-3xl font-bold">{t('title')}</h1>
      {plan?.updatedAt ? (
        <p className="text-sm text-muted" data-testid="plan-byline">
          {plan.coAuthor ? t('byline', { coAuthor: plan.coAuthor, date: formatDate(plan.updatedAt, locale, undefined, { day: 'numeric', month: 'long' }) }) : t('updated', { date: formatDate(plan.updatedAt, locale, undefined, { day: 'numeric', month: 'long' }) })}
        </p>
      ) : null}
    </div>
  );

  if (state !== 'ready' || !plan) {
    return (
      <div className="flex flex-col gap-4 pt-2">
        {header}
        {state === 'loading' ? <Spinner label={t('loading')} /> : <Notice tone="warning">{t('unavailable')}</Notice>}
        <section aria-labelledby="plan-numbers" className="flex flex-col gap-3 rounded-card bg-surface p-4">
          <h2 id="plan-numbers" className="font-bold">
            {t('sections.professionals')}
          </h2>
          {numbers}
        </section>
      </div>
    );
  }

  if (editing && drafts) {
    return (
      <div className="flex flex-col gap-4 pt-2">
        {header}
        <form
          className="flex flex-col gap-4"
          onSubmit={(e) => {
            e.preventDefault();
            void save();
          }}
        >
          {ALL.map((section) => (
            <TextArea
              key={section}
              label={t(`sections.${section}`)}
              value={drafts[section]}
              rows={3}
              maxLength={MAX_SECTION}
              dir="auto"
              onChange={(e) => setDrafts({ ...drafts, [section]: e.target.value })}
            />
          ))}
          <fieldset className="flex flex-col gap-3 rounded-card border-[1.5px] border-line p-4">
            <legend className="px-1 font-bold">{t('contacts.legend')}</legend>
            {contactDrafts.map((contact, i) => (
              <div key={i} className="flex flex-col gap-2" data-testid="plan-contact-row">
                <TextField
                  label={t('contacts.name', { n: i + 1 })}
                  value={contact.name}
                  maxLength={MAX_CONTACT_NAME}
                  dir="auto"
                  required
                  onChange={(e) => setContactDrafts(contactDrafts.map((c, j) => (j === i ? { ...c, name: e.target.value } : c)))}
                />
                <TextField
                  label={t('contacts.phone', { n: i + 1 })}
                  value={contact.phone}
                  type="tel"
                  inputMode="tel"
                  dir="ltr"
                  maxLength={32}
                  required
                  onChange={(e) => setContactDrafts(contactDrafts.map((c, j) => (j === i ? { ...c, phone: e.target.value } : c)))}
                />
                <Button variant="ghost" compact className="self-start" onClick={() => setContactDrafts(contactDrafts.filter((_, j) => j !== i))}>
                  {t('contacts.remove', { n: i + 1 })}
                </Button>
              </div>
            ))}
            {contactDrafts.length < MAX_CONTACTS ? (
              <Button variant="secondary" compact className="self-start" onClick={() => setContactDrafts([...contactDrafts, { name: '', phone: '' }])}>
                {t('contacts.add')}
              </Button>
            ) : null}
          </fieldset>
          {error ? (
            <p role="alert" className="text-sm font-semibold text-help">
              {error}
            </p>
          ) : null}
          <div className="flex gap-3">
            <Button type="submit" busy={busy}>
              {t('save')}
            </Button>
            <Button variant="secondary" disabled={busy} onClick={() => setEditing(false)}>
              {t('cancel')}
            </Button>
          </div>
        </form>
      </div>
    );
  }

  const view = shown ?? plan;
  const reason = (view.sections.reasonToLive ?? '').trim();

  return (
    <div className="flex flex-col gap-3 pt-2">
      {header}

      {source === 'offline-cache' && savedAt ? (
        <Notice tone="warning">
          <span data-testid="plan-offline">{t('offlineNotice', { date: formatDate(savedAt, locale) })}</span>
        </Notice>
      ) : null}
      <div aria-live="polite" className="empty:hidden">
        {saved && !pendingEdit ? <Notice tone="success">{t('saved')}</Notice> : null}
        {pendingEdit ? (
          <Notice tone="warning">
            <span data-testid="plan-pending">{t('pending')}</span>
          </Notice>
        ) : null}
      </div>
      {emptyPlan(view) ? <p className="leading-relaxed text-muted">{t('emptyPlan')}</p> : null}

      <ol className="flex flex-col gap-3">
        {NUMBERED.map((section, i) => {
          const text = (view.sections[section] ?? '').trim();
          return (
            <li key={section} className="flex items-start gap-3 rounded-card bg-surface p-4 shadow-[0_1px_0_rgb(var(--line))]" data-testid={`plan-${section}`}>
              <span aria-hidden="true" className={`flex h-10 w-10 shrink-0 items-center justify-center rounded-full font-bold ${BADGE[section]}`}>
                {i + 1}
              </span>
              <div className="flex min-w-0 flex-1 flex-col gap-2">
                <h2 className="text-base font-bold">{t(`sections.${section}`)}</h2>
                {section === 'peopleToAsk' ? (
                  <>
                    {(view.contacts ?? []).length > 0 ? (
                      <ul className="flex flex-wrap gap-2" data-testid="plan-contacts">
                        {(view.contacts ?? []).map((contact) => (
                          <li key={`${contact.name}-${contact.phone}`}>
                            {contact.phone ? (
                              <CallLink number={contact.phone} variant="chip">
                                <bdi dir="auto">{contact.name}</bdi>
                              </CallLink>
                            ) : (
                              // S35 allows a person without a phone: shown, but not as a call button.
                              <span dir="auto" className="inline-flex min-h-touch items-center rounded-full bg-tint-teal px-4 py-2 text-sm font-semibold text-primary-strong" data-testid="plan-contact-no-phone">
                                {contact.name}
                              </span>
                            )}
                          </li>
                        ))}
                      </ul>
                    ) : null}
                    {text ? (
                      <ul className="flex flex-wrap gap-2">
                        {people(text).map((person) => (
                          <li key={person} dir="auto" className="rounded-full bg-tint-teal px-4 py-2 text-sm font-semibold text-primary-strong">
                            {person}
                          </li>
                        ))}
                      </ul>
                    ) : null}
                    {!text && (view.contacts ?? []).length === 0 ? <p className="leading-relaxed text-muted">{t('empty')}</p> : null}
                  </>
                ) : section === 'professionals' ? (
                  <>
                    {text ? (
                      <p dir="auto" className="whitespace-pre-line leading-relaxed">
                        {text}
                      </p>
                    ) : null}
                    {numbers}
                  </>
                ) : (
                  <p dir="auto" className={`whitespace-pre-line leading-relaxed ${text ? '' : 'text-muted'}`}>
                    {text || t('empty')}
                  </p>
                )}
              </div>
            </li>
          );
        })}
      </ol>

      <section aria-labelledby="plan-reason" className="flex items-start gap-3 rounded-card bg-primary p-5 text-surface" data-testid="plan-reasonToLive">
        <svg aria-hidden="true" width="26" height="26" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinejoin="round" className="mt-0.5 shrink-0">
          <path d="M12 20s-7-4.4-7-10a4 4 0 017-2.6A4 4 0 0119 10c0 5.6-7 10-7 10z" />
        </svg>
        <div className="flex flex-col gap-1">
          <h2 id="plan-reason" className="text-lg font-bold">
            {t('sections.reasonToLive')}
          </h2>
          <p dir="auto" className="whitespace-pre-line leading-relaxed">
            {reason || t('empty')}
          </p>
        </div>
      </section>

      <div className="flex flex-col gap-2 pt-2">
        <Button variant="secondary" full onClick={startEdit}>
          {t('edit')}
        </Button>
      </div>
    </div>
  );
}
