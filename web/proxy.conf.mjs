// Dev server proxy: the browser calls /api on the Angular origin and the dev server forwards it to the
// API, so the API needs no CORS. Under Aspire, WithReference(api) injects the API URL; standalone
// (`pnpm start` without Aspire) it falls back to the API's https launch profile.
const target =
  process.env['services__api__https__0'] ??
  process.env['services__api__http__0'] ??
  'https://localhost:7062';

export default {
  '/api': {
    target,
    // The ASP.NET Core dev certificate is not trusted by Node
    secure: false,
    changeOrigin: true,
  },
};
