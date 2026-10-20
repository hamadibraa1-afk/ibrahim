export function ProgressBar({ value, label }: { value: number; label: string }) {
  const v = Math.max(0, Math.min(100, Math.round(value)));
  return (
    <div role="progressbar" aria-valuemin={0} aria-valuemax={100} aria-valuenow={v} aria-label={label} className="h-2 w-full overflow-hidden rounded-full bg-line">
      <div className="h-full rounded-full bg-primary transition-[width]" style={{ width: `${v}%` }} />
    </div>
  );
}
