import { HelpNow } from '@/components/crisis/HelpNowButton';

/**
 * Always visible. Signed in, it is the Help now button (PB-26): numbers first, then a Critical event and the
 * crisis screen (P06), queued while offline (EC-04). Without a session, and before the page is interactive,
 * it leads to the public emergency resources (P14), which the service worker keeps for offline use.
 */
export function HelpNowLink() {
  return <HelpNow />;
}
