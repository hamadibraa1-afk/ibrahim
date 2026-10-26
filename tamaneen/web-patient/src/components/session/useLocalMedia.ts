'use client';

import { useCallback, useEffect, useRef, useState } from 'react';

export type MediaStatus = 'idle' | 'checking' | 'ok' | 'audio_only' | 'denied' | 'unavailable';

/**
 * The device's own camera and microphone, for the lobby check (P41) and the test room (P42). Nothing leaves the
 * device: the stream is only shown in a local <video>. Every track is stopped when the component goes away.
 */
export function useLocalMedia() {
  const [status, setStatus] = useState<MediaStatus>('idle');
  const [stream, setStream] = useState<MediaStream | null>(null);
  const [micOn, setMicOn] = useState(true);
  const [cameraOn, setCameraOn] = useState(true);
  const current = useRef<MediaStream | null>(null);

  const stop = useCallback(() => {
    current.current?.getTracks().forEach((t) => t.stop());
    current.current = null;
    setStream(null);
  }, []);

  const start = useCallback(async (audioOnly = false) => {
    if (typeof navigator === 'undefined' || !navigator.mediaDevices?.getUserMedia) {
      setStatus('unavailable');
      return;
    }
    setStatus('checking');
    stop();
    try {
      const media = await navigator.mediaDevices.getUserMedia(audioOnly ? { audio: true } : { audio: true, video: true });
      current.current = media;
      setStream(media);
      setMicOn(true);
      setCameraOn(!audioOnly);
      setStatus(audioOnly ? 'audio_only' : 'ok');
    } catch (e) {
      const name = e instanceof DOMException ? e.name : '';
      setStatus(name === 'NotFoundError' || name === 'NotReadableError' ? 'unavailable' : 'denied');
    }
  }, [stop]);

  const toggleMic = useCallback(() => {
    const next = !micOn;
    current.current?.getAudioTracks().forEach((t) => (t.enabled = next));
    setMicOn(next);
  }, [micOn]);

  const toggleCamera = useCallback(() => {
    const next = !cameraOn;
    current.current?.getVideoTracks().forEach((t) => (t.enabled = next));
    setCameraOn(next);
  }, [cameraOn]);

  useEffect(() => stop, [stop]);

  return { status, stream, micOn, cameraOn, start, stop, toggleMic, toggleCamera };
}
