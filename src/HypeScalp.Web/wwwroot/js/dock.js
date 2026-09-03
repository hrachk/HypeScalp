/**
 * HypeScalp — Window Manager & Layout Engine v2
 * Drag / resize / minimize / z-order / workspace tabs / layout persistence
 */

(function (global) {
  'use strict';

  /* ─── Z-order ──────────────────────────────────────── */
  let zTop = 20;
  function bringToFront(win) {
    document.querySelectorAll('.win').forEach(w => w.classList.remove('active'));
    win.classList.add('active');
    win.style.zIndex = ++zTop;
  }

  /* ─── Drag ─────────────────────────────────────────── */
  function makeDraggable(win) {
    const header = win.querySelector('.win-header');
    if (!header) return;
    let ox = 0, oy = 0, dragging = false;

    header.addEventListener('mousedown', e => {
      if (e.target.closest('.win-btn')) return;
      bringToFront(win);
      const r = win.getBoundingClientRect();
      ox = e.clientX - r.left;
      oy = e.clientY - r.top;
      dragging = true;
      header.style.cursor = 'grabbing';
      e.preventDefault();
    });

    document.addEventListener('mousemove', e => {
      if (!dragging) return;
      const ws = document.getElementById('workspace');
      const wsr = ws ? ws.getBoundingClientRect() : { left: 0, top: 0, width: window.innerWidth, height: window.innerHeight };
      const nx = Math.max(0, Math.min(e.clientX - ox, wsr.width - 60));
      const ny = Math.max(0, Math.min(e.clientY - oy, wsr.height - 30));
      win.style.left = nx + 'px';
      win.style.top  = ny + 'px';
    });

    document.addEventListener('mouseup', () => {
      if (!dragging) return;
      dragging = false;
      header.style.cursor = 'grab';
      Layout.save();
    });
  }

  /* ─── Resize ───────────────────────────────────────── */
  function makeResizable(win) {
    const handle = win.querySelector('.win-resize');
    if (!handle) return;
    let resizing = false, sx = 0, sy = 0, sw = 0, sh = 0;

    handle.addEventListener('mousedown', e => {
      resizing = true;
      sx = e.clientX; sy = e.clientY;
      sw = win.offsetWidth; sh = win.offsetHeight;
      e.preventDefault(); e.stopPropagation();
    });

    document.addEventListener('mousemove', e => {
      if (!resizing) return;
      const minW = parseInt(win.style.minWidth) || 220;
      const minH = parseInt(win.style.minHeight) || 140;
      win.style.width  = Math.max(minW, sw + e.clientX - sx) + 'px';
      win.style.height = Math.max(minH, sh + e.clientY - sy) + 'px';
    });

    document.addEventListener('mouseup', () => {
      if (!resizing) return;
      resizing = false;
      Layout.save();
    });
  }

  /* ─── Minimize ─────────────────────────────────────── */
  function makeMinimizable(win) {
    const minBtn = win.querySelector('.win-btn.min');
    if (!minBtn) return;
    minBtn.addEventListener('click', () => {
      win.classList.toggle('minimized');
    });
  }

  /* ─── Close ────────────────────────────────────────── */
  function makeClosable(win) {
    const closeBtn = win.querySelector('.win-btn.close');
    if (!closeBtn) return;
    closeBtn.addEventListener('click', () => {
      win.remove();
      Layout.save();
    });
  }

  /* ─── Focus on click ───────────────────────────────── */
  function makeFocusable(win) {
    win.addEventListener('mousedown', () => bringToFront(win));
  }

  /* ─── Factory ──────────────────────────────────────── */
  function applyAll(win) {
    makeFocusable(win);
    makeDraggable(win);
    makeResizable(win);
    makeMinimizable(win);
    makeClosable(win);
  }

  /* ─── Layout persistence ───────────────────────────── */
  const LAYOUT_KEY = 'hs2.layout';

  const Layout = {
    save() {
      const wins = [];
      document.querySelectorAll('.win[data-wid]').forEach(w => {
        wins.push({
          id:     w.dataset.wid,
          left:   w.style.left,
          top:    w.style.top,
          width:  w.style.width,
          height: w.style.height,
          mini:   w.classList.contains('minimized'),
        });
      });
      try { localStorage.setItem(LAYOUT_KEY, JSON.stringify(wins)); } catch {}
    },

    restore() {
      try {
        const raw = localStorage.getItem(LAYOUT_KEY);
        if (!raw) return;
        const wins = JSON.parse(raw);
        wins.forEach(s => {
          const el = document.querySelector(`.win[data-wid="${s.id}"]`);
          if (!el) return;
          if (s.left)   el.style.left   = s.left;
          if (s.top)    el.style.top    = s.top;
          if (s.width)  el.style.width  = s.width;
          if (s.height) el.style.height = s.height;
          if (s.mini)   el.classList.add('minimized');
        });
      } catch {}
    },

    reset() {
      try { localStorage.removeItem(LAYOUT_KEY); } catch {}
    }
  };

  /* ─── createWin ────────────────────────────────────── */
  function createWin({ id, title, x, y, w, h, minW, minH, bodyHtml }) {
    const el = document.createElement('div');
    el.className = 'win';
    el.dataset.wid = id;
    el.style.left    = x + 'px';
    el.style.top     = y + 'px';
    el.style.width   = w + 'px';
    el.style.height  = h + 'px';
    if (minW) el.style.minWidth  = minW + 'px';
    if (minH) el.style.minHeight = minH + 'px';

    el.innerHTML = `
      <div class="win-header">
        <div class="win-title">${title}</div>
        <div class="win-controls">
          <button class="win-btn min" title="Minimize">─</button>
          <button class="win-btn close" title="Close">✕</button>
        </div>
      </div>
      <div class="win-body">${bodyHtml || ''}</div>
      <div class="win-resize"></div>`;

    document.getElementById('workspace').appendChild(el);
    applyAll(el);
    return el;
  }

  /* ─── Toast ────────────────────────────────────────── */
  function showToast(msg, isErr) {
    let t = document.getElementById('toast');
    if (!t) { t = document.createElement('div'); t.id = 'toast'; t.className = 'toast'; document.body.appendChild(t); }
    t.textContent = msg;
    t.className = 'toast show' + (isErr ? ' err' : '');
    clearTimeout(t._tm);
    t._tm = setTimeout(() => { t.className = 'toast' + (isErr ? ' err' : ''); }, 2600);
  }

  /* ─── Exports ──────────────────────────────────────── */
  global.Dock = { createWin, applyAll, bringToFront, Layout };
  global.showToast = showToast;

})(window);
