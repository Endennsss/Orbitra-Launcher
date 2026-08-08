# Web workshop deployment

The website uses Supabase Edge Functions so SS14 passwords and the service-role key never enter GitHub Pages.

```powershell
supabase link --project-ref lvhysaqgxynjcfavrvui
supabase secrets set WEB_SESSION_SECRET="<a-long-random-secret>"
supabase functions deploy ss14-web-auth --no-verify-jwt
supabase functions deploy workshop-publish --no-verify-jwt
```

Generate `WEB_SESSION_SECRET` with a cryptographically secure password generator (at least 32 random bytes). Supabase automatically provides `SUPABASE_URL` and `SUPABASE_SERVICE_ROLE_KEY` to Edge Functions.

The public site can browse and download themes immediately. Login, registration, and publishing become active after both functions are deployed.
