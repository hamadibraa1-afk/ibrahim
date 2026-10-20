export const environment = {
  production: true,
  /** Same-origin reverse proxy to Tamaneen.Api in deployed environments. */
  apiBaseUrl: '/api',
  /** Same-origin reverse proxy to Tamaneen.Crisis.Worker (REST and the /hubs/crisis SignalR hub). */
  crisisBaseUrl: '/crisis',
};
