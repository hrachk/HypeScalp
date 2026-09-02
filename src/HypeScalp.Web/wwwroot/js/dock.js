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

// Multi-exchange overlay chart
window.hypeChart = {
  drawOverlay: function (canvas, series) {
    if (!canvas) return;
    const parent = canvas.parentElement;
    if (!parent) return;
    const dpr = window.devicePixelRatio || 1;
    const W = parent.clientWidth;
    const H = parent.clientHeight;
    if (W < 8 || H < 8) return;
    canvas.width = W * dpr;
    canvas.height = H * dpr;
    const ctx = canvas.getContext('2d');
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    ctx.clearRect(0, 0, W, H);
    if (!series || !series.length) return;

    let minP = Infinity, maxP = -Infinity, minT = Infinity, maxT = -Infinity;
    series.forEach(s => {
      s.points.forEach(pt => {
        if (pt.p < minP) minP = pt.p;
        if (pt.p > maxP) maxP = pt.p;
        if (pt.t < minT) minT = pt.t;
        if (pt.t > maxT) maxT = pt.t;
      });
    });
    const rangeP = maxP - minP || 1;
    const rangeT = maxT - minT || 1;
    const pad = 8;

    // grid
    ctx.strokeStyle = 'rgba(128,128,128,0.08)';
    ctx.lineWidth = 1;
    for (let i = 1; i < 4; i++) {
      const y = pad + ((H - pad * 2) / 4) * i;
      ctx.beginPath(); ctx.moveTo(0, y); ctx.lineTo(W, y); ctx.stroke();
    }

    series.forEach(s => {
      ctx.beginPath();
      ctx.strokeStyle = s.Color || s.color;
      ctx.lineWidth = 1.5;
      ctx.globalAlpha = 0.9;
      s.points.forEach((pt, i) => {
        const x = ((pt.t - minT) / rangeT) * (W - pad * 2) + pad;
        const y = pad + (1 - (pt.p - minP) / rangeP) * (H - pad * 2);
        if (i === 0) ctx.moveTo(x, y); else ctx.lineTo(x, y);
      });
      ctx.stroke();
    });
    ctx.globalAlpha = 1;

    // legend
    let lx = 10;
    series.forEach(s => {
      ctx.fillStyle = s.Color || s.color;
      ctx.fillRect(lx, 6, 12, 3);
      ctx.fillStyle = '#8b92a5';
      ctx.font = '10px sans-serif';
      ctx.fillText(s.Name || s.name, lx + 16, 10);
      lx += ctx.measureText(s.Name || s.name).width + 36;
    });
  }
};
