// Local development configuration. No environment-specific secrets live in
// this file — the API base URL is the only environment-specific value, and
// the API itself runs with its own configuration (connection string, JWT
// signing key) outside of source control.
export const environment = {
  apiBaseUrl: 'http://localhost:5295/api'
};
