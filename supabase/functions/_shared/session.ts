const encoder = new TextEncoder();
const b64url = (bytes: Uint8Array) => btoa(String.fromCharCode(...bytes)).replaceAll("+", "-").replaceAll("/", "_").replaceAll("=", "");
const decode = (value: string) => { const normalized = value.replaceAll("-", "+").replaceAll("_", "/"); const padded = normalized + "=".repeat((4 - normalized.length % 4) % 4); return Uint8Array.from(atob(padded), c => c.charCodeAt(0)); };
async function key() { return crypto.subtle.importKey("raw", encoder.encode(Deno.env.get("WEB_SESSION_SECRET")!), { name: "HMAC", hash: "SHA-256" }, false, ["sign", "verify"]); }
export type WebSession = { userId: string; username: string; ss14Token: string; exp: number };
export async function signSession(payload: WebSession) { const body = b64url(encoder.encode(JSON.stringify(payload))); const signature = b64url(new Uint8Array(await crypto.subtle.sign("HMAC", await key(), encoder.encode(body)))); return `${body}.${signature}`; }
export async function readSession(token: string): Promise<WebSession> { const [body, signature] = token.split("."); if (!body || !signature || !await crypto.subtle.verify("HMAC", await key(), decode(signature), encoder.encode(body))) throw new Error("Недействительная сессия"); const payload = JSON.parse(new TextDecoder().decode(decode(body))); if (payload.exp < Date.now()) throw new Error("Сессия истекла — войдите снова"); return payload; }
