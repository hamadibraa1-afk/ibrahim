import { createHmac } from 'node:crypto';

/** The development staff accounts all share this TOTP secret (20 bytes, UTF-8). */
export const DEV_TOTP_SECRET = Buffer.from('tamaneen-dev-totp-20', 'utf8');
export const TOTP_STEP_SECONDS = 30;

/** RFC 6238 TOTP: HMAC-SHA1, 6 digits, 30-second steps. */
export function totp(secret: Buffer = DEV_TOTP_SECRET, at: number = Date.now()): string {
  const counter = Buffer.alloc(8);
  counter.writeBigUInt64BE(BigInt(Math.floor(at / 1000 / TOTP_STEP_SECONDS)));
  const hmac = createHmac('sha1', secret).update(counter).digest();
  const offset = hmac[hmac.length - 1]! & 0x0f;
  const value = (hmac.readUInt32BE(offset) & 0x7fffffff) % 1_000_000;
  return value.toString().padStart(6, '0');
}

/** Milliseconds until the next 30-second step starts. */
export function msUntilNextStep(at: number = Date.now()): number {
  const stepMs = TOTP_STEP_SECONDS * 1000;
  return stepMs - (at % stepMs);
}
