import { notFound } from 'next/navigation';

// Unknown paths under a locale render the localized not-found page, not Next's default English one.
export default function CatchAll() {
  notFound();
}
