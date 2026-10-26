'use client';

import { useLocale, useTranslations } from 'next-intl';
import { useState, type ReactNode } from 'react';
import { Link, useRouter } from '@/i18n/navigation';
import { isLocale, routing } from '@/i18n/routing';
import { api } from '@/lib/api/client';
import type { CrisisFollowUp } from '@/lib/api/types';
import { formatDateTime } from '@/lib/format';
import { RequestCallButton } from '@/components/crisis/RequestCallButton';
import { Button } from '@/components/ui/Button';
import { Card } from '@/components/ui/Card';

function Item({ children }: { children: ReactNode }) {
  return (
    <li className="flex items-start gap-3">
      <span aria-hidden="true" className="mt-2 h-2 w-2 shrink-0 rounded-full bg-primary" />
      <span className="leading-relaxed">{children}</span>
    </li>
  );
}

/**
 * P38 after a crisis. Warm, without the conversation, the risk level or any diagnostic word: who the patient spoke
 * with, what follows (the therapist knows, the next appointment), a check-in, the safety plan, and a way to reach a
 * person now (the same Help now request, joined to an open episode by the server). «العودة للرئيسية» marks it seen.
 */
export function AfterCrisisView({ followUp, timeZone, resources }: { followUp: CrisisFollowUp; timeZone: string; resources: ReactNode }) {
  const t = useTranslations('afterCrisis');
  const current = useLocale();
  const locale = isLocale(current) ? current : routing.defaultLocale;
  const router = useRouter();
  const [leaving, setLeaving] = useState(false);

  async function home() {
    setLeaving(true);
    try {
      await api('crisis/follow-up/seen', { method: 'POST' });
    } catch {
      // Going home must never be blocked.
    }
    router.push('/home');
  }

  const items: ReactNode[] = [];
  if (followUp.therapistInformed) {
    items.push(followUp.therapistName ? t('therapistInformed', { name: followUp.therapistName }) : t('therapistInformedPlain'));
  }
  if (followUp.nextAppointmentAt) {
    items.push(t('nextAppointment', { when: formatDateTime(followUp.nextAppointmentAt, locale, timeZone) }));
  }

  return (
    <div className="flex flex-col gap-5 pt-4">
      <div className="flex flex-col items-center gap-3 text-center">
        <span aria-hidden="true" className="flex h-16 w-16 items-center justify-center rounded-full bg-tint-teal text-primary">
          <svg width="30" height="30" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
            <path d="M20.8 4.6a5.5 5.5 0 00-7.8 0L12 5.7l-1-1.1a5.5 5.5 0 00-7.8 7.8l1 1.1L12 21l7.8-7.5 1-1.1a5.5 5.5 0 000-7.8z" />
          </svg>
        </span>
        <p className="text-lg font-semibold text-primary">{t('glad')}</p>
        <h1 className="text-2xl font-bold leading-snug">{followUp.talked && followUp.responder ? t('talkedWith', { name: followUp.responder }) : t('teamWithYou')}</h1>
        <p className="leading-relaxed text-muted">{t('brave')}</p>
      </div>

      {items.length > 0 ? (
        <Card>
          <h2 className="mb-3 font-bold">{t('agreed')}</h2>
          <ul className="flex flex-col gap-3">
            {items.map((item, i) => (
              <Item key={i}>{item}</Item>
            ))}
          </ul>
        </Card>
      ) : null}

      <Card tone="teal" className="flex items-center justify-between gap-3">
        <div>
          <h2 className="font-bold">{t('planReady')}</h2>
          <p className="text-sm">{t('planBody')}</p>
        </div>
        <Link href="/safety-plan" className="min-h-touch inline-flex items-center font-semibold text-primary underline underline-offset-4">
          {t('open')}
        </Link>
      </Card>

      <Card className="flex items-center justify-between gap-3">
        <div>
          <h2 className="font-bold">{t('checkInTitle')}</h2>
          <p className="text-sm text-muted">{t('checkInBody')}</p>
        </div>
        <Link href="/check-in" className="min-h-touch inline-flex items-center font-semibold text-primary underline underline-offset-4">
          {t('checkIn')}
        </Link>
      </Card>

      <section aria-labelledby="after-resources" className="flex flex-col gap-3">
        <h2 id="after-resources" className="font-bold">{t('resources')}</h2>
        {resources}
      </section>

      <Button full busy={leaving} onClick={home}>
        {t('home')}
      </Button>
      <RequestCallButton label={t('talkNow')} />
    </div>
  );
}
