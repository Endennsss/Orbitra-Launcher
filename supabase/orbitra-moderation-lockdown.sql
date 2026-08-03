-- URGENT 0.44.1 security hotfix. Run immediately in Supabase SQL Editor.
drop policy if exists "Moderation reports readable" on public.orbitra_moderation_reports;
drop policy if exists "Moderation reports updateable" on public.orbitra_moderation_reports;
revoke select, update on public.orbitra_moderation_reports from anon, authenticated;
grant insert on public.orbitra_moderation_reports to anon, authenticated;
