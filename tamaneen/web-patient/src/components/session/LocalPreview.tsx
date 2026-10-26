'use client';

import { useEffect, useRef } from 'react';

/** The device's own camera, mirrored, muted (no echo). Shown only here; it is never sent anywhere. */
export function LocalPreview({ stream, label, className = '' }: { stream: MediaStream | null; label: string; className?: string }) {
  const ref = useRef<HTMLVideoElement>(null);
  useEffect(() => {
    const video = ref.current;
    if (!video) return;
    video.srcObject = stream;
    if (stream) void video.play?.()?.catch(() => undefined);
  }, [stream]);
  return <video ref={ref} aria-label={label} muted playsInline autoPlay className={`-scale-x-100 rounded-card bg-ink/80 object-cover ${className}`} />;
}
