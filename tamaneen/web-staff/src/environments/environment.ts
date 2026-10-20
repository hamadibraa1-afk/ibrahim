export const environment = {
  production: true,
  /** Same-origin reverse proxy to Tamaneen.Api in deployed environments. */
  apiBaseUrl: '/api',
  /** Roles come from the access token after sign-in. */
  devRoles: [] as ('therapist' | 'supervisor' | 'lead' | 'admin')[],
};
