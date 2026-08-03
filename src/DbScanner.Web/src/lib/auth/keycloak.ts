import Keycloak, { type KeycloakTokenParsed } from "keycloak-js";

type DbScannerToken = KeycloakTokenParsed & {
  preferred_username?: string;
  roles?: string[];
};

const keycloak = new Keycloak({
  url: process.env.NEXT_PUBLIC_KEYCLOAK_URL ?? "http://localhost:18080",
  realm: process.env.NEXT_PUBLIC_KEYCLOAK_REALM ?? "dbscanner",
  clientId: process.env.NEXT_PUBLIC_KEYCLOAK_CLIENT_ID ?? "dbscanner-web",
});

let initialization: Promise<boolean> | null = null;

export function initializeKeycloak(): Promise<boolean> {
  if (!initialization) {
    initialization = keycloak.init({
      onLoad: "login-required",
      pkceMethod: "S256",
      checkLoginIframe: false,
    });
  }

  return initialization;
}

export async function getAccessToken(): Promise<string> {
  const authenticated = await initializeKeycloak();
  if (!authenticated) {
    await keycloak.login();
    throw new Error("Keycloak authentication is required.");
  }

  await keycloak.updateToken(30);
  if (!keycloak.token) {
    throw new Error("Keycloak access token is unavailable.");
  }

  return keycloak.token;
}

export function getCurrentIdentity() {
  const token = keycloak.tokenParsed as DbScannerToken | undefined;
  return {
    username: token?.preferred_username ?? "Kullanıcı",
    roles: token?.roles ?? [],
  };
}

export async function logoutFromKeycloak(): Promise<void> {
  await keycloak.logout({ redirectUri: window.location.origin });
}
