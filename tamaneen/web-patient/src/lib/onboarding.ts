import type { Me } from './api/types';

/**
 * Where a signed-in patient should be, given what they have finished. Returns null once onboarding is
 * done (the home screen takes over). Order: consents (P03) → safety info (P21, skippable) → path (P22) →
 * safety check (P23, direct path) or intake (P04) → summary (P05) → waiting for review (P36) → matches (P07).
 */
export function onboardingStep(me: Me): string | null {
  const o = me.onboarding;
  if (me.chatLocked) return '/crisis';
  if (!o.consentsDone) return '/start/consents';
  if (!o.safetyInfoDone) return '/start/safety-info';
  if (o.path === null) return '/start/path';
  if (o.path === 'direct') return o.screeningDone ? null : '/start/safety-check';
  switch (o.intakeStatus) {
    case 'none':
    case 'active':
    case 'paused':
    case 'expired':
      return '/intake';
    case 'summarizing':
    case 'review':
      return '/intake/summary';
    default:
      break;
  }
  if (o.triageStatus === 'pending') return '/waiting';
  return null;
}

/** First screen after sign-in. Patients who have finished onboarding but never booked go to their matches or the directory. */
export function landingPath(me: Me): string {
  return onboardingStep(me) ?? '/home';
}
