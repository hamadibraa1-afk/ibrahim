import type { ButtonHTMLAttributes, ComponentProps, ReactNode } from 'react';
import { Link } from '@/i18n/navigation';

export type ButtonVariant = 'primary' | 'secondary' | 'ghost' | 'danger' | 'danger-outline';

const base =
  'inline-flex items-center justify-center gap-2 rounded-full px-6 text-base font-semibold transition-colors disabled:cursor-not-allowed disabled:opacity-50';
const variants: Record<ButtonVariant, string> = {
  primary: 'bg-primary text-surface hover:bg-primary-strong',
  secondary: 'border-[1.5px] border-tint-teal bg-surface text-primary hover:bg-tint-teal',
  ghost: 'text-primary underline-offset-4 hover:underline',
  danger: 'bg-help text-surface',
  'danger-outline': 'border-[1.5px] border-help bg-surface text-help',
};

export function buttonClass(variant: ButtonVariant = 'primary', full = false, compact = false): string {
  return `${base} ${variants[variant]} ${compact ? 'min-h-touch text-sm px-4' : 'min-h-cta'} ${full ? 'w-full' : ''}`;
}

type ButtonProps = ButtonHTMLAttributes<HTMLButtonElement> & { variant?: ButtonVariant; full?: boolean; compact?: boolean; busy?: boolean };

/** Every action is at least 44px tall (52px for main actions). `busy` keeps the label and sets aria-busy. */
export function Button({ variant = 'primary', full, compact, busy, className = '', type = 'button', disabled, children, ...rest }: ButtonProps) {
  return (
    <button type={type} disabled={disabled || busy} aria-busy={busy || undefined} className={`${buttonClass(variant, full, compact)} ${className}`} {...rest}>
      {busy ? <span aria-hidden="true" className="h-4 w-4 animate-spin rounded-full border-2 border-current border-t-transparent" /> : null}
      {children}
    </button>
  );
}

type LinkProps = ComponentProps<typeof Link> & { variant?: ButtonVariant; full?: boolean; compact?: boolean; children: ReactNode };

export function ButtonLink({ variant = 'primary', full, compact, className = '', ...rest }: LinkProps) {
  return <Link className={`${buttonClass(variant, full, compact)} ${className}`} {...rest} />;
}
