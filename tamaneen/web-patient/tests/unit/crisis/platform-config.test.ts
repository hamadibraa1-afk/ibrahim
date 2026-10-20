vi.mock('@/lib/api/server', () => ({ publicGet: vi.fn() }));

import { publicPlatformConfig, safetyNumbers } from '@/lib/platform-config';

describe('platform config and safety numbers', () => {
  const env = { TAMANEEN_EMERGENCY_NUMBER: '997', TAMANEEN_SUPPORT_LINE: '920 033 360' } as unknown as NodeJS.ProcessEnv;

  it('prefers the live PublicConfig', () => {
    expect(safetyNumbers({ emergencyNumber: '911', supportLine: '988', triageWithinHours: 12 }, env)).toEqual({ emergencyNumber: '911', supportLine: '988', triageWithinHours: 12 });
  });

  it('falls back to the environment when the API is unavailable or a value is malformed', () => {
    expect(safetyNumbers(null, env)).toEqual({ emergencyNumber: '997', supportLine: '920033360', triageWithinHours: 24 });
    expect(safetyNumbers({ emergencyNumber: 'call us', supportLine: '' }, env).emergencyNumber).toBe('997');
  });

  it('returns null numbers when nothing is configured', () => {
    expect(safetyNumbers(null, {} as NodeJS.ProcessEnv)).toEqual({ emergencyNumber: null, supportLine: null, triageWithinHours: 24 });
  });

  it('reads the maintenance end time and the matching window', () => {
    const cfg = publicPlatformConfig({ TAMANEEN_MAINTENANCE_UNTIL: '2026-10-20T18:00:00Z', TAMANEEN_MATCH_WITHIN_HOURS: '48' } as unknown as NodeJS.ProcessEnv);
    expect(cfg.maintenanceUntil).toBe('2026-10-20T18:00:00.000Z');
    expect(cfg.matchWithinHours).toBe(48);
    expect(publicPlatformConfig({ TAMANEEN_MAINTENANCE_UNTIL: 'soon' } as unknown as NodeJS.ProcessEnv).maintenanceUntil).toBeNull();
  });
});
