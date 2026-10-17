import { INetworkModule, NetworkResponse, PublicClientApplication } from '@azure/msal-browser';
import { authConfig, msalConfiguration } from './auth.config';

const tenantId = 'a228e3ce-b747-42f6-9092-db0b378d4da0';

/**
 * The tenant's real OpenID configuration (trimmed). Its issuer uses the tenant id as host, not "plumy":
 * MSAL 5 rejects the authority unless the configuration accounts for that (issuer_validation_failed,
 * reported in the browser as endpoints_resolution_error).
 */
const discoveryDocument = {
  issuer: `https://${tenantId}.ciamlogin.com/${tenantId}/v2.0`,
  authorization_endpoint: `https://plumy.ciamlogin.com/${tenantId}/oauth2/v2.0/authorize`,
  token_endpoint: `https://plumy.ciamlogin.com/${tenantId}/oauth2/v2.0/token`,
  end_session_endpoint: `https://plumy.ciamlogin.com/${tenantId}/oauth2/v2.0/logout`,
  jwks_uri: `https://plumy.ciamlogin.com/${tenantId}/discovery/v2.0/keys`,
  response_modes_supported: ['query', 'fragment', 'form_post'],
};

/** Answers MSAL's discovery request like Entra does, without going to the network. */
const fakeEntra: INetworkModule = {
  sendGetRequestAsync: async <T>(url: string) => {
    expect(url).toContain('/.well-known/openid-configuration');
    return { status: 200, body: discoveryDocument, headers: {} } as NetworkResponse<T>;
  },
  sendPostRequestAsync: () => Promise.reject(new Error('Not expected in this test')),
};

describe('msalConfiguration', () => {
  afterEach(() => window.sessionStorage.clear());

  it("is accepted by MSAL against the tenant's real discovery document and signs in on plumy.ciamlogin.com", async () => {
    let signInUrl: string | undefined;
    const config = msalConfiguration('https://app.getplumy.com');
    const msal = new PublicClientApplication({
      ...config,
      auth: {
        ...config.auth,
        // Capture where MSAL would send the browser instead of leaving the page
        onRedirectNavigate: (url) => {
          signInUrl = url;
          return false;
        },
      },
      system: { networkClient: fakeEntra },
    });
    await msal.initialize();

    await msal.loginRedirect({ scopes: [authConfig.apiScope] });

    const url = new URL(signInUrl!);
    expect(`${url.origin}${url.pathname}`).toBe(discoveryDocument.authorization_endpoint);
    expect(url.searchParams.get('client_id')).toBe(authConfig.clientId);
    expect(url.searchParams.get('redirect_uri')).toBe('https://app.getplumy.com');
    expect(url.searchParams.get('scope')).toContain(authConfig.apiScope);
    expect(url.searchParams.get('code_challenge_method')).toBe('S256');
  });
});
