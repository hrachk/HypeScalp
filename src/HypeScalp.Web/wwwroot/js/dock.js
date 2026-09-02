// Simple drag for floating windows
document.addEventListener('DOMContentLoaded', () => {
  let drag = null;
  document.addEventListener('mousedown', e => {
    const header = e.target.closest('.win-header');
    if (!header || e.target.closest('.win-btn')) return;
    const win = header.closest('.win');
    if (!win) return;
    document.querySelectorAll('.win').forEach(w => w.classList.remove('active'));
    win.classList.add('active');
    const rect = win.getBoundingClientRect();
    drag = { el: win, ox: e.clientX - rect.left, oy: e.clientY - rect.top };
  });
  document.addEventListener('mousemove', e => {
    if (!drag) return;
    drag.el.style.left = Math.max(0, e.clientX - drag.ox) + 'px';
    drag.el.style.top  = Math.max(0, e.clientY - drag.oy) + 'px';
  });
  document.addEventListener('mouseup', () => { drag = null; });
});

window.showToast = function(msg) {
  let t = document.getElementById('toast');
  if (!t) {
    t = document.createElement('div');
    t.id = 'toast';
    t.className = 'toast';
    document.body.appendChild(t);
  }
  t.textContent = msg;
  t.classList.add('show');
  setTimeout(() => t.classList.remove('show'), 2200);
};
