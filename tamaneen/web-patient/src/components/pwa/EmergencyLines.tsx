import { getTranslations } from 'next-intl/server';
import { PhoneNumber } from '@/components/crisis/PhoneNumber';
import { Link } from '@/i18n/navigation';
import { loadSafetyNumbers } from '@/lib/platform-config';

/**
 * P35 rule «أرقام الطوارئ ظاهرة في كل حالة»: the emergency and support numbers as tel: links, from the live
 * config with the environment snapshot as fallback, or a link to the emergency resources when neither is set.
 * Used by the offline, session-expired and 404 pages.
 */
export async function EmergencyLines() {
  const [t, { emergencyNumber, supportLine }] = await Promise.all([getTranslations('systemStates.offline'), loadSafetyNumbers()]);
  const lines = [emergencyNumber, supportLine].filter((n): n is string => Boolean(n));

  return (
    <div className="flex flex-col items-center gap-1 rounded-[16px] bg-help/10 px-4 py-3 text-sm text-help">
      {lines.length > 0 ? (
        <>
          <span>{t('numbers')}</span>
          <span className="flex flex-wrap items-center justify-center gap-x-3 font-bold">
            {lines.map((n) => (
              <a key={n} href={`tel:${n}`} className="inline-flex min-h-touch items-center underline underline-offset-4">
                <PhoneNumber number={n} />
              </a>
            ))}
          </span>
        </>
      ) : (
        <Link href="/crisis-resources" className="inline-flex min-h-touch items-center font-bold underline underline-offset-4">
          {t('noNumbers')}
        </Link>
      )}
    </div>
  );
}
