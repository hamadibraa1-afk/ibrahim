import 'server-only';

/**
 * Values the platform admin manages in Platform Settings (S43). Until the public settings endpoint is
 * wired in phase 4, the deployment provides the safety snapshot through environment variables, which is
 * also the documented fallback when the settings service is unreachable.
 */
export interface PublicPlatformConfig {
  emergencyNumber: string | null;
  matchWithinHours: number;
}

const PHONE = /^\+?[0-9]{2,15}$/;

export function publicPlatformConfig(env: NodeJS.ProcessEnv = process.env): PublicPlatformConfig {
  const rawNumber = env.TAMANEEN_EMERGENCY_NUMBER?.trim();
  const hours = Number.parseInt(env.TAMANEEN_MATCH_WITHIN_HOURS ?? '', 10);
  return {
    emergencyNumber: rawNumber && PHONE.test(rawNumber) ? rawNumber : null,
    matchWithinHours: Number.isFinite(hours) && hours > 0 && hours <= 720 ? hours : 72,
  };
}
