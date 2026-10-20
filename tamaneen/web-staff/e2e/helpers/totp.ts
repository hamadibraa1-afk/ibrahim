import { createHmac } from 'node:crypto';

/** Development-only TOTP secret shared by every seeded staff account (RFC 6238, SHA-1, 6 digits, 30 s). */
export const DEV_TOTP_SECRET = Buffer.from('tamaneen-dev-totp-20', 'utf8');
export const STEP_SECONDS = 30;

export function totpStep(now = Date.now()): number {
  return Math.floor(now / 1000 / STEP_SECONDS);
}

export function totp(step = totpStep(), secret: Buffer = DEV_TOTP_SECRET): string {
  const counter = Buffer.alloc(8);
  counter.writeBigUInt64BE(BigInt(step));
  const hmac = createHmac('sha1', secret).update(counter).digest();
  const offset = hmac[hmac.length - 1] & 0x0f;
  const binary = (hmac.readUInt32BE(offset) & 0x7fffffff) % 1_000_000;
  return binary.toString().padStart(6, '0');
}

/**
 * Each account accepts a step only once. Tracks the last step used per account and waits for a fresh one when needed,
 * so a test never resubmits a code the server already consumed.
 */
const lastUsed = new Map<string, number>();

export async function freshTotp(account: string): Promise<string> {
  for (;;) {
    const step = totpStep();
    const last = lastUsed.get(account.toLowerCase());
    // Leave a little room before the step ends so the code is still valid when it reaches the server.
    const msLeft = (step + 1) * STEP_SECONDS * 1000 - Date.now();
    if ((last === undefined || step > last) && msLeft > 3000) {
      lastUsed.set(account.toLowerCase(), step);
      return totp(step);
    }
    await new Promise((r) => setTimeout(r, Math.min(msLeft + 200, 31_000)));
  }
}
