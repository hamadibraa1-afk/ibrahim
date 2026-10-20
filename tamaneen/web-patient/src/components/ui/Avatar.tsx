/** Initials avatar. Therapist photos are not stored; initials keep the directory consistent. */
export function Avatar({ initials, size = 48, label }: { initials: string; size?: number; label?: string }) {
  return (
    <span
      role={label ? 'img' : undefined}
      aria-label={label}
      aria-hidden={label ? undefined : true}
      style={{ width: size, height: size, fontSize: size * 0.34 }}
      className="inline-flex shrink-0 items-center justify-center rounded-full bg-tint-teal font-bold text-primary-strong"
    >
      {initials}
    </span>
  );
}
