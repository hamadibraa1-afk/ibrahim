import { redirect } from 'next/navigation';

// Paths outside a locale prefix go to the default (Arabic) start page; the middleware handles most cases.
export default function RootNotFound() {
  redirect('/ar');
}
