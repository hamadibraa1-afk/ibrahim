'use client';

import { useTranslations } from 'next-intl';
import { useId, useState } from 'react';
import { MIN_PASSWORD_LENGTH } from './contact';

type Strength = 0 | 1 | 2 | 3 | 4;

/** A rough meter for the hint only; the API enforces the real rules (length and the breach list). */
export function passwordStrength(value: string): Strength {
  if (!value) return 0;
  const classes = [/[a-z]/, /[A-Z]/, /[0-9]/, /[^A-Za-z0-9]/].filter((r) => r.test(value)).length;
  if (value.length < MIN_PASSWORD_LENGTH) return 1;
  if (value.length >= 16 || classes >= 3) return 4;
  return classes >= 2 ? 3 : 2;
}

interface PasswordFieldProps {
  label: string;
  value: string;
  onChange: (value: string) => void;
  autoComplete: 'current-password' | 'new-password';
  error?: string | null;
  /** Shows the strength bar and the "at least 12 characters" hint (sign-up, new password). */
  showStrength?: boolean;
  name?: string;
}

/** Password input with a show/hide toggle. Typed text stays left-to-right in Arabic pages. */
export function PasswordField({ label, value, onChange, autoComplete, error, showStrength, name }: PasswordFieldProps) {
  const t = useTranslations('ui');
  const ts = useTranslations('signup');
  const id = useId();
  const [visible, setVisible] = useState(false);
  const strength = passwordStrength(value);
  const strong = value.length >= MIN_PASSWORD_LENGTH;
  const hintId = `${id}-hint`;
  const errorId = `${id}-error`;
  const describedBy = [error ? errorId : null, showStrength ? hintId : null].filter(Boolean).join(' ') || undefined;

  return (
    <div className="flex flex-col gap-1.5">
      <label htmlFor={id} className="text-sm font-semibold">
        {label}
      </label>
      <div className="relative">
        <input
          id={id}
          name={name}
          type={visible ? 'text' : 'password'}
          dir="ltr"
          value={value}
          onChange={(e) => onChange(e.target.value)}
          autoComplete={autoComplete}
          autoCapitalize="none"
          autoCorrect="off"
          spellCheck={false}
          aria-invalid={error ? true : undefined}
          aria-describedby={describedBy}
          className="min-h-[48px] w-full rounded-[14px] border border-line bg-surface pe-14 ps-4 text-start text-base text-ink focus:border-primary aria-[invalid=true]:border-help"
        />
        <button
          type="button"
          onClick={() => setVisible((v) => !v)}
          aria-label={visible ? t('hidePassword') : t('showPassword')}
          aria-pressed={visible}
          aria-controls={id}
          className="absolute inset-y-0 end-0 flex min-h-touch w-12 items-center justify-center text-muted hover:text-primary"
        >
          <svg aria-hidden="true" width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
            <path d="M2 12s3.6-7 10-7 10 7 10 7-3.6 7-10 7S2 12 2 12z" />
            <circle cx="12" cy="12" r="3" />
            {visible ? <path d="M4 4l16 16" /> : null}
          </svg>
        </button>
      </div>
      {showStrength ? (
        <>
          <div aria-hidden="true" className="flex gap-1.5" data-testid="password-strength" data-strength={strength}>
            {[1, 2, 3, 4].map((i) => (
              <span key={i} className={`h-1 flex-1 rounded-full ${i <= strength ? (strong ? 'bg-primary' : 'bg-help') : 'bg-line'}`} />
            ))}
          </div>
          <p id={hintId} aria-live="polite" className={`text-sm font-semibold ${!value ? 'font-normal text-muted' : strong ? 'text-primary' : 'text-help'}`}>
            {!value ? ts('passwordHint') : strong ? ts('passwordStrong') : ts('passwordShort')}
          </p>
        </>
      ) : null}
      {error ? (
        <p id={errorId} role="alert" className="text-sm font-semibold text-help">
          {error}
        </p>
      ) : null}
    </div>
  );
}
