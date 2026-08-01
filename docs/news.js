const list = document.querySelector('#news-list');
const escapeHtml = value => String(value ?? '').replace(/[&<>'"]/g, char => ({'&':'&amp;','<':'&lt;','>':'&gt;',"'":'&#39;','"':'&quot;'})[char]);

fetch('./news.json', { cache: 'no-cache' })
  .then(response => { if (!response.ok) throw new Error(`HTTP ${response.status}`); return response.json(); })
  .then(items => {
    list.innerHTML = items.slice(0, 6).map((item, index) => `
      <article class="news-card ${item.important ? 'important' : ''}">
        <div class="news-meta"><span>${escapeHtml(item.version)}</span><time>${escapeHtml(item.date)}</time></div>
        <h3>${escapeHtml(item.title)}</h3><p>${escapeHtml(item.summary)}</p>
        <a href="${escapeHtml(item.url)}">Подробнее <span>↗</span></a><b>${String(index + 1).padStart(2, '0')}</b>
      </article>`).join('');
  })
  .catch(() => { list.innerHTML = '<div class="news-loading error">Не удалось загрузить новости. Попробуйте обновить страницу.</div>'; });
