-- Improved Orbitra profiles: banner, bio and favorite server.
-- Run once in Supabase Dashboard -> SQL Editor.
alter table public.orbitra_profiles add column if not exists banner_path text;
alter table public.orbitra_profiles add column if not exists description text not null default '';
alter table public.orbitra_profiles add column if not exists favorite_server text;
alter table public.orbitra_profiles add column if not exists favorite_server_name text;
alter table public.orbitra_profiles drop constraint if exists orbitra_profiles_description_check;
alter table public.orbitra_profiles add constraint orbitra_profiles_description_check check (char_length(description) <= 240);

insert into storage.buckets (id, name, public, file_size_limit, allowed_mime_types)
values ('orbitra-profile-media', 'orbitra-profile-media', true, 4194304, array['image/png','image/jpeg'])
on conflict (id) do update set public=true, file_size_limit=4194304, allowed_mime_types=array['image/png','image/jpeg'];

drop policy if exists "Profile media readable" on storage.objects;
drop policy if exists "Profile media uploadable" on storage.objects;
create policy "Profile media readable" on storage.objects for select using (bucket_id='orbitra-profile-media');
create policy "Profile media uploadable" on storage.objects for insert with check (bucket_id='orbitra-profile-media');
