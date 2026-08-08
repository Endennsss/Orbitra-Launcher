import { corsHeaders } from "../_shared/cors.ts";
import { signSession } from "../_shared/session.ts";
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { ...corsHeaders, "Content-Type": "application/json" } });
Deno.serve(async req => {
  if (req.method === "OPTIONS") return new Response("ok", { headers: corsHeaders });
  try {
    const body = await req.json();
    if (!body.username || !body.password) return json({ error: "Заполните имя и пароль" }, 400);
    const endpoint = body.action === "register" ? "register" : "authenticate";
    const payload = body.action === "register" ? { username: body.username, email: body.email, password: body.password } : { username: body.username, userId: null, password: body.password, tfaCode: body.tfaCode };
    const response = await fetch(`https://auth.spacestation14.com/api/auth/${endpoint}`, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(payload) });
    const result = await response.json().catch(() => ({}));
    if (!response.ok) return json({ error: result.errors?.join("\n") || "SS14 отклонил запрос", code: result.code }, response.status);
    if (body.action === "register") return json({ registered: true });
    const exp = Math.min(new Date(result.expireTime).getTime(), Date.now() + 12 * 60 * 60 * 1000);
    return json({ session: await signSession({ userId: result.userId, username: result.username, ss14Token: result.token, exp }), userId: result.userId, username: result.username, expiresAt: new Date(exp).toISOString() });
  } catch (error) { return json({ error: error.message || "Внутренняя ошибка" }, 500); }
});
