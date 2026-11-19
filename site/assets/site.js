// Progressive enhancement: all content and navigation work without JavaScript.
(() => {
  // Both editions use the same chapter IDs. Keep real links for keyboard,
  // middle-click and no-JS readers; only add the current fragment here.
  const languageLink = document.querySelector('[data-language-link]');
  if (languageLink) {
    const syncFragment = () => { languageLink.hash = window.location.hash; };
    syncFragment();
    window.addEventListener('hashchange', syncFragment);
  }

  const english = document.documentElement.lang === 'en';
  const matchStatus = count => {
    if (!count) return english ? 'No matches. Try a shorter search.' : '没找到，试试更短的关键词。';
    return english ? `${count} ${count === 1 ? 'match' : 'matches'} in the contents.` : `目录里有 ${count} 项匹配。`;
  };
  const search = document.querySelector('#guide-search');
  if (!search) return;
  const box = document.querySelector('.search-box');
  const status = document.querySelector('.search-status');
  const links = [...document.querySelectorAll('.guide-sidebar nav a')];
  const sections = links.map(link => document.querySelector(link.hash));
  box.hidden = false;

  search.addEventListener('input', () => {
    const terms = search.value.trim().toLocaleLowerCase().split(/\s+/).filter(Boolean);
    let matches = 0;
    links.forEach((link, index) => {
      const section = sections[index];
      const text = `${section.textContent} ${section.dataset.keywords || ''}`.toLocaleLowerCase();
      const match = terms.every(term => text.includes(term));
      link.hidden = !match;
      if (match) matches++;
    });
    status.textContent = terms.length ? matchStatus(matches) : '';
  });

  if ('IntersectionObserver' in window) {
    const visible = new Set();
    const observer = new IntersectionObserver(entries => {
      entries.forEach(entry => entry.isIntersecting ? visible.add(entry.target) : visible.delete(entry.target));
      const current = sections.find(section => visible.has(section));
      if (!current) return;
      links.forEach(link => {
        if (link.hash === `#${current.id}`) link.setAttribute('aria-current', 'location');
        else link.removeAttribute('aria-current');
      });
    }, { rootMargin: '-100px 0px -55% 0px', threshold: 0 });
    sections.forEach(section => observer.observe(section));
  }
})();
