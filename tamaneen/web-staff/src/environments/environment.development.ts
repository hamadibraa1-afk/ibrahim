export const environment = {
  production: false,
  apiBaseUrl: 'http://localhost:5080',
  /** Development only: lets the portal be explored before staff sign-in (S06) is connected. */
  devRoles: ['therapist', 'supervisor', 'lead', 'admin'] as ('therapist' | 'supervisor' | 'lead' | 'admin')[],
};
