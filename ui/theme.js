'use strict';
/* Appearance is independent of the USB backend and never changes device data. */
(() => {
  const media = window.matchMedia('(prefers-color-scheme: dark)');
  const allowed = new Set(['system', 'light', 'dark']);
  let preference = 'system';

  function apply() {
    const effective = preference === 'system' ? (media.matches ? 'dark' : 'light') : preference;
    document.documentElement.dataset.effectiveTheme = effective;
    document.documentElement.dataset.theme = preference;
    const select = document.getElementById('appearance');
    if (select) {
      select.value = preference;
      select.title = preference === 'system' ? `Following OS appearance (${effective})` : `Appearance: ${preference}`;
    }
  }

  async function save(value) {
    if (!allowed.has(value)) throw new Error('Unsupported appearance option.');
    const previous = preference;
    preference = value;
    apply();
    try {
      await window.amagAppearanceSave(value);
    } catch (error) {
      preference = previous;
      apply();
      throw error;
    }
  }

  window.AmagAppearance = {
    load(value) { preference = allowed.has(value) ? value : 'system'; apply(); },
    save,
    get preference() { return preference; },
    get effective() { return document.documentElement.dataset.effectiveTheme; }
  };

  if (media.addEventListener) media.addEventListener('change', apply);
  else if (media.addListener) media.addListener(apply);
  const initializeControl = () => {
    apply();
    const select = document.getElementById('appearance');
    select?.addEventListener('change', async () => {
      try { await save(select.value); }
      catch (error) {
        const target = document.getElementById('error');
        if (target) { target.textContent = 'Could not save appearance: ' + error.message; target.hidden = false; }
      }
    });
  };
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', initializeControl);
  else initializeControl();
  apply();
})();
