-- Orbitra Theme Workshop v2. Run once after theme-workshop.sql.
alter table public.workshop_themes add column if not exists preview_path text;
alter table public.workshop_themes add column if not exists updated_at timestamptz not null default now();

create table if not exists public.theme_favorites (
  theme_id uuid not null references public.workshop_themes(id) on delete cascade,
  user_id uuid not null,
  created_at timestamptz not null default now(),
  primary key (theme_id, user_id)
);
alter table public.theme_favorites enable row level security;
drop policy if exists "Favorites are public" on public.theme_favorites;
create policy "Favorites are public" on public.theme_favorites for select using (true);
drop policy if exists "SS14 users can favorite" on public.theme_favorites;
create policy "SS14 users can favorite" on public.theme_favorites for insert with check (true);
drop policy if exists "SS14 users can unfavorite" on public.theme_favorites;
create policy "SS14 users can unfavorite" on public.theme_favorites for delete using (true);
grant select, insert, delete on public.theme_favorites to anon, authenticated;

drop policy if exists "Authors can update themes" on public.workshop_themes;
create policy "Authors can update themes" on public.workshop_themes for update using (true) with check (true);
drop policy if exists "Authors can delete themes" on public.workshop_themes;
create policy "Authors can delete themes" on public.workshop_themes for delete using (true);
grant update, delete on public.workshop_themes to anon, authenticated;

drop policy if exists "Authors can delete comments" on public.theme_comments;
create policy "Authors can delete comments" on public.theme_comments for delete using (true);
grant delete on public.theme_comments to anon, authenticated;

insert into storage.buckets (id, name, public, file_size_limit, allowed_mime_types)
values ('theme-previews', 'theme-previews', true, 2097152, array['image/png'])
on conflict (id) do update set public = true, file_size_limit = excluded.file_size_limit,
  allowed_mime_types = excluded.allowed_mime_types;

drop policy if exists "Theme previews are public" on storage.objects;
create policy "Theme previews are public" on storage.objects for select using (bucket_id = 'theme-previews');
drop policy if exists "Anyone can upload a theme preview" on storage.objects;
create policy "Anyone can upload a theme preview" on storage.objects for insert
  with check (bucket_id = 'theme-previews' and (storage.foldername(name))[1] = 'previews');
drop policy if exists "Theme authors can replace archives" on storage.objects;
create policy "Theme authors can replace archives" on storage.objects for update
  using (bucket_id in ('theme-workshop', 'theme-previews'))
  with check (bucket_id in ('theme-workshop', 'theme-previews'));
drop policy if exists "Theme authors can remove files" on storage.objects;
create policy "Theme authors can remove files" on storage.objects for delete
  using (bucket_id in ('theme-workshop', 'theme-previews'));

create index if not exists workshop_themes_author_idx on public.workshop_themes(author_user_id);
create index if not exists workshop_themes_updated_idx on public.workshop_themes(updated_at desc);
