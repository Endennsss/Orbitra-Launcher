-- Fix for 403 AccessDenied when uploading/updating Orbitra profile avatars.
-- Run once in Supabase Dashboard -> SQL Editor.
drop policy if exists "Avatars readable" on storage.objects;
drop policy if exists "Avatars uploadable" on storage.objects;
drop policy if exists "Avatars updateable" on storage.objects;
drop policy if exists "Avatars removable" on storage.objects;

create policy "Avatars readable"
on storage.objects for select
using (bucket_id = 'orbitra-avatars');

create policy "Avatars uploadable"
on storage.objects for insert
with check (bucket_id = 'orbitra-avatars');

create policy "Avatars updateable"
on storage.objects for update
using (bucket_id = 'orbitra-avatars')
with check (bucket_id = 'orbitra-avatars');

create policy "Avatars removable"
on storage.objects for delete
using (bucket_id = 'orbitra-avatars');
