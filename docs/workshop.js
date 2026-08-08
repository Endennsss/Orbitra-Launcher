(() => {
  const SUPABASE = "https://lvhysaqgxynjcfavrvui.supabase.co";
  const KEY = "sb_publishable_-MjoEbdhEVaP1QsIrPcbIA_BxqxLw5j";
  const api = (path, options = {}) => fetch(`${SUPABASE}${path}`, { ...options, headers: { apikey: KEY, ...(options.headers || {}) } });
  const $ = id => document.getElementById(id);
  const state = { themes: [], session: JSON.parse(sessionStorage.getItem("orbitra-session") || "null"), register: false };
  const escape = value => String(value ?? "").replace(/[&<>"']/g, c => ({"&":"&amp;","<":"&lt;",">":"&gt;",'"':"&quot;","'":"&#39;"}[c]));
  const publicFile = (bucket, path) => `${SUPABASE}/storage/v1/object/public/${bucket}/${path}`;

  function syncAccount() {
    $("account-open").textContent = state.session ? state.session.username : "Войти через SS14";
    $("publish-open").disabled = !state.session;
    $("logout-button").hidden = !state.session;
    $("auth-submit").hidden = !!state.session;
    if (state.session) $("auth-message").textContent = `Выполнен вход: ${state.session.username}`;
  }

  async function loadThemes() {
    $("theme-list").innerHTML = '<div class="news-loading">Загружаем темы…</div>';
    try {
      const response = await api("/rest/v1/workshop_themes?select=*,theme_likes(count),theme_comments(count)&order=updated_at.desc");
      if (!response.ok) throw new Error(`Supabase ${response.status}`);
      state.themes = await response.json();
      renderThemes();
    } catch (error) { $("theme-list").innerHTML = `<div class="news-loading error">${escape(error.message)}</div>`; }
  }

  function renderThemes() {
    const query = $("theme-search").value.trim().toLowerCase();
    const themes = state.themes.filter(x => `${x.name} ${x.author_name} ${x.description}`.toLowerCase().includes(query));
    $("theme-list").innerHTML = themes.length ? themes.map(theme => {
      const preview = theme.preview_path ? `style="background-image:url('${publicFile("theme-previews", encodeURI(theme.preview_path))}')"` : "";
      const likes = theme.theme_likes?.[0]?.count || 0, comments = theme.theme_comments?.[0]?.count || 0;
      return `<article class="theme-card"><div class="theme-preview" ${preview}><span>v${escape(theme.version)}</span></div><div class="theme-content"><div class="theme-author">${escape(theme.author_name)}</div><h3>${escape(theme.name)}</h3><p>${escape(theme.description || "Без описания")}</p><div class="theme-meta"><span>♡ ${likes}</span><span>◌ ${comments}</span><span>↓ ${theme.downloads || 0}</span></div><a class="primary theme-download" href="${publicFile("theme-workshop", encodeURI(theme.archive_path))}" data-id="${theme.id}" download>Скачать ZIP</a></div></article>`;
    }).join("") : '<div class="news-loading">Темы не найдены.</div>';
  }

  async function authenticate() {
    const body = { action: state.register ? "register" : "login", username: $("auth-username").value.trim(), email: $("auth-email").value.trim(), password: $("auth-password").value, tfaCode: $("auth-tfa").value.trim() || null };
    $("auth-message").textContent = "Проверяем данные…";
    try {
      const response = await api("/functions/v1/ss14-web-auth", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(body) });
      const result = await response.json();
      if (!response.ok) { if (result.code === "TfaRequired" || result.code === 3) $("tfa-row").hidden = false; throw new Error(result.error || "Ошибка авторизации"); }
      if (state.register) { $("auth-message").textContent = "Аккаунт SS14 создан. Подтвердите email и войдите."; setMode(false); return; }
      state.session = result; sessionStorage.setItem("orbitra-session", JSON.stringify(result)); syncAccount(); $("account-dialog").close();
    } catch (error) { $("auth-message").textContent = error.message; }
  }

  function setMode(register) {
    state.register = register; $("login-tab").classList.toggle("active", !register); $("register-tab").classList.toggle("active", register);
    $("email-row").hidden = !register; $("tfa-row").hidden = true; $("auth-submit").textContent = register ? "Создать аккаунт SS14" : "Войти"; $("auth-password").autocomplete = register ? "new-password" : "current-password";
  }

  async function publishTheme() {
    const archive = $("publish-archive").files[0], preview = $("publish-preview").files[0];
    if (!$("publish-name").value.trim()) return $("publish-message").textContent = "Укажите название темы.";
    if (!archive || !preview) return $("publish-message").textContent = "Выберите ZIP и PNG-превью.";
    if (archive.size > 20 * 1024 * 1024 || preview.size > 2 * 1024 * 1024) return $("publish-message").textContent = "ZIP — до 20 МБ, PNG — до 2 МБ.";
    const form = new FormData();
    form.append("session", state.session.session); form.append("name", $("publish-name").value.trim()); form.append("description", $("publish-description").value.trim()); form.append("version", $("publish-version").value.trim()); form.append("background", $("publish-background").value); form.append("surface", $("publish-surface").value); form.append("accent", $("publish-accent").value); form.append("textColor", $("publish-text").value); form.append("blur", $("publish-blur").value); form.append("archive", archive); form.append("preview", preview);
    $("publish-message").textContent = "Проверяем и загружаем тему…";
    try {
      const response = await api("/functions/v1/workshop-publish", { method: "POST", body: form }); const result = await response.json();
      if (!response.ok) throw new Error(result.error || "Публикация не удалась");
      $("publish-dialog").close(); $("publish-message").textContent = ""; await loadThemes();
    } catch (error) { $("publish-message").textContent = error.message; }
  }

  document.addEventListener("click", event => { const link = event.target.closest(".theme-download"); if (link) api("/rest/v1/rpc/increment_theme_download", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ theme: link.dataset.id }) }).catch(() => {}); });
  $("account-open").onclick = () => $("account-dialog").showModal(); $("publish-open").onclick = () => $("publish-dialog").showModal();
  $("login-tab").onclick = () => setMode(false); $("register-tab").onclick = () => setMode(true); $("auth-submit").onclick = authenticate;
  $("logout-button").onclick = () => { state.session = null; sessionStorage.removeItem("orbitra-session"); $("account-dialog").close(); syncAccount(); };
  $("publish-submit").onclick = publishTheme; $("themes-refresh").onclick = loadThemes; $("theme-search").oninput = renderThemes;
  $("publish-blur").oninput = event => $("blur-value").value = event.target.value;
  syncAccount(); loadThemes();
})();
