-- Run once in Supabase Dashboard -> SQL Editor after deploying a launcher
-- version that no longer contains the reporting feature.
drop table if exists public.orbitra_moderation_reports;
drop table if exists public.orbitra_reports;
