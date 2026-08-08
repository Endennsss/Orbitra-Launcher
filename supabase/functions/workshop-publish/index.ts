import { createClient } from "https://esm.sh/@supabase/supabase-js@2";
import { corsHeaders } from "../_shared/cors.ts";
import { readSession } from "../_shared/session.ts";
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { ...corsHeaders, "Content-Type": "application/json" } });
Deno.serve(async req => {
  if (req.method === "OPTIONS") return new Response("ok", { headers: corsHeaders });
  try {
    const form = await req.formData(); const session = await readSession(String(form.get("session") || ""));
    const ping = await fetch("https://auth.spacestation14.com/api/auth/ping", { headers: { Authorization: `SS14Auth ${session.ss14Token}` } });
    if (!ping.ok) return json({ error: "Сессия SS14 больше не действует" }, 401);
    const archive = form.get("archive"), preview = form.get("preview");
    if (!(archive instanceof File) || !(preview instanceof File)) return json({ error: "Не выбраны файлы темы" }, 400);
    if (archive.size > 20 * 1024 * 1024 || preview.size > 2 * 1024 * 1024 || archive.type !== "application/zip" || preview.type !== "image/png") return json({ error: "Недопустимый формат или размер файлов" }, 400);
    const magic = new Uint8Array(await archive.slice(0, 4).arrayBuffer()); if (magic[0] !== 0x50 || magic[1] !== 0x4b) return json({ error: "Архив не является ZIP" }, 400);
    const id = crypto.randomUUID(), archivePath = `themes/${id}/theme.zip`, previewPath = `previews/${id}/preview.png`;
    const db = createClient(Deno.env.get("SUPABASE_URL")!, Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!);
    let upload = await db.storage.from("theme-workshop").upload(archivePath, archive, { contentType: "application/zip" }); if (upload.error) throw upload.error;
    upload = await db.storage.from("theme-previews").upload(previewPath, preview, { contentType: "image/png" }); if (upload.error) { await db.storage.from("theme-workshop").remove([archivePath]); throw upload.error; }
    const clean = (key: string, max: number) => String(form.get(key) || "").trim().slice(0, max);
    const { error } = await db.from("workshop_themes").insert({ id, author_user_id: session.userId, author_name: session.username, name: clean("name", 60), description: clean("description", 2000), version: clean("version", 20) || "1.0", archive_path: archivePath, preview_path: previewPath, background: clean("background", 9), surface: clean("surface", 9), accent: clean("accent", 9), text_color: clean("textColor", 9), blur: Math.max(0, Math.min(40, Number(form.get("blur")) || 0)) });
    if (error) { await db.storage.from("theme-workshop").remove([archivePath]); await db.storage.from("theme-previews").remove([previewPath]); throw error; }
    return json({ id });
  } catch (error) { return json({ error: error.message || "Внутренняя ошибка" }, 400); }
});
