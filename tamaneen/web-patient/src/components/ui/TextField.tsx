'use client';

import { useId, type InputHTMLAttributes, type ReactNode, type TextareaHTMLAttributes } from 'react';

interface FieldShellProps {
  label: ReactNode;
  hint?: ReactNode;
  error?: string | null;
  id: string;
  children: ReactNode;
  optional?: ReactNode;
}

function FieldShell({ label, hint, error, id, children, optional }: FieldShellProps) {
  return (
    <div className="flex flex-col gap-1.5">
      <label htmlFor={id} className="text-sm font-semibold">
        {label}
        {optional ? <span className="ms-1 font-normal text-muted">{optional}</span> : null}
      </label>
      {children}
      {hint && !error ? (
        <p id={`${id}-hint`} className="text-xs text-muted">
          {hint}
        </p>
      ) : null}
      {error ? (
        <p id={`${id}-error`} role="alert" className="text-sm font-semibold text-help">
          {error}
        </p>
      ) : null}
    </div>
  );
}

const inputClass =
  'min-h-[48px] w-full rounded-[14px] border border-line bg-surface px-4 text-base text-ink placeholder:text-muted/70 focus:border-primary aria-[invalid=true]:border-help';

type TextFieldProps = Omit<InputHTMLAttributes<HTMLInputElement>, 'id'> & { label: ReactNode; hint?: ReactNode; error?: string | null; optional?: ReactNode };

/**
 * Labelled input. Phone numbers, codes and e-mail addresses should pass dir="ltr" so digits keep their
 * order inside Arabic pages.
 */
export function TextField({ label, hint, error, optional, className = '', ...rest }: TextFieldProps) {
  const id = useId();
  const describedBy = error ? `${id}-error` : hint ? `${id}-hint` : undefined;
  return (
    <FieldShell label={label} hint={hint} error={error} id={id} optional={optional}>
      <input id={id} aria-invalid={error ? true : undefined} aria-describedby={describedBy} className={`${inputClass} ${className}`} {...rest} />
    </FieldShell>
  );
}

type TextAreaProps = Omit<TextareaHTMLAttributes<HTMLTextAreaElement>, 'id'> & { label: ReactNode; hint?: ReactNode; error?: string | null; optional?: ReactNode };

export function TextArea({ label, hint, error, optional, className = '', rows = 4, ...rest }: TextAreaProps) {
  const id = useId();
  const describedBy = error ? `${id}-error` : hint ? `${id}-hint` : undefined;
  return (
    <FieldShell label={label} hint={hint} error={error} id={id} optional={optional}>
      <textarea id={id} rows={rows} aria-invalid={error ? true : undefined} aria-describedby={describedBy} className={`${inputClass} py-3 leading-relaxed ${className}`} {...rest} />
    </FieldShell>
  );
}
