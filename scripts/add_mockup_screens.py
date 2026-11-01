"""Adds the screen switcher to the ten redesign mockups.

Each mockup is a single screen (Combos). This script makes that screen the first of three, keeps the other two out of the
way and adds a small bar "Combos · Editor · Configuración" at the bottom of the window. The review page sets
`document.documentElement.dataset.screen` (1, 2 or 3) before the frame runs; screen 1 is the mockup as designed.

The script is idempotent: running it again replaces its own block. Run it from the repository root:

    python scripts/add_mockup_screens.py
"""

from __future__ import annotations

from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DESIGN = ROOT / "docs" / "design"

MARK = "<!-- bdotempo-screens -->"

BLOCK = """<!-- bdotempo-screens -->
<style id="bdotempo-screens-styles">
  .bdotempo-hidden { display: none !important; }
  .bdotempo-screenbar { display: flex; gap: 6px; align-items: center; padding: 8px 14px; border-top: 1px solid currentColor;
    border-color: color-mix(in srgb, currentColor 22%, transparent); font: 12px "Segoe UI", system-ui, sans-serif; }
  .bdotempo-screenbar span { padding: 4px 10px; border-radius: 8px; opacity: .6; }
  .bdotempo-screenbar span.on { opacity: 1; font-weight: 600; }
</style>
<script>
(function () {
  function apply() {
    var wanted = Number(document.documentElement.dataset.screen || 1);
    var stage = document.querySelector('.stage') || document.querySelector('.win');
    if (!stage) { return; }
    var blocks = Array.prototype.filter.call(stage.children, function (el) {
      return el.nodeType === 1 && el.tagName !== 'SCRIPT' && el.tagName !== 'STYLE' && !el.classList.contains('titlebar');
    });
    if (!blocks.length) { return; }
    // La pantalla diseñada es la de Combos; las otras dos quedan reservadas.
    blocks.forEach(function (block, index) {
      block.classList.add('bdotempo-hidden');
      block.removeAttribute('data-bdotempo-screen');
    });
    if (!document.querySelector('.bdotempo-screenbar')) {
      var bar = document.createElement('div');
      bar.className = 'bdotempo-screenbar';
      ['Combos', 'Editor', 'Configuración'].forEach(function (name, index) {
        var item = document.createElement('span');
        item.textContent = name;
        item.dataset.bdotempoScreen = index + 1;
        if (index === 0) { item.classList.add('on'); }
        bar.appendChild(item);
      });
      stage.appendChild(bar);
    }
    var barItems = document.querySelectorAll('.bdotempo-screenbar span');
    barItems.forEach(function (item, index) {
      item.classList.toggle('on', (index + 1) === wanted);
    });
    if (wanted === 1) {
      blocks[0].classList.remove('bdotempo-hidden');
      blocks[0].setAttribute('data-bdotempo-screen', '1');
      return;
    }
    // Las pantallas extra todavía no existen: se muestra una nota dentro del mismo marco.
    if (!document.querySelector('.bdotempo-placeholder')) {
      var note = document.createElement('section');
      note.className = 'bdotempo-placeholder';
      note.style.cssText = 'padding:28px;font:14px/1.6 "Segoe UI",system-ui,sans-serif;opacity:.85;max-width:520px;';
      stage.appendChild(note);
    }
    var placeholder = document.querySelector('.bdotempo-placeholder');
    placeholder.textContent = '';
    var title = document.createElement('b');
    title.textContent = wanted === 2 ? 'Editor en este diseño' : 'Configuración en este diseño';
    title.style.cssText = 'display:block;font-size:16px;margin-bottom:8px;';
    var text = document.createElement('p');
    text.textContent = wanted === 2
      ? 'Aquí iría el editor de ranuras con esta misma piel: los 20 accesos de enfriamiento y los 20 accesos rápidos sobre el juego en vivo, y el botón de aplicar al personaje. Se mostrará en cuanto se rediseñe esa pantalla.'
      : 'Aquí iría la configuración con esta misma piel: cuenta, personajes (con la lectura de nombres por OCR), carpeta del juego, idioma y actualizaciones. Se mostrará en cuanto se rediseñe esa pantalla.';
    placeholder.appendChild(title);
    placeholder.appendChild(text);
    placeholder.classList.remove('bdotempo-hidden');
  }
  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', apply);
  } else {
    apply();
  }
  window.__bdotempoApply = apply;
})();
</script>
<!-- /bdotempo-screens -->
"""

FILES = [
    "mockup-native-01-familia-windows.html",
    "mockup-native-02-estudio-oscuro.html",
    "mockup-native-03-consola-de-juego.html",
    "mockup-native-04-minimal-blanco.html",
    "mockup-native-05-profesional-selecto.html",
    "mockup-webui-06-sistema-webui2.html",
    "mockup-webui-07-sistema-webui3.html",
    "mockup-webui-08-tablero-bento.html",
    "mockup-webui-09-editorial-demanding.html",
    "mockup-webui-10-monospace-dev.html",
]


def main() -> None:
    for name in FILES:
        path = DESIGN / name
        text = path.read_text(encoding="utf-8")
        if MARK in text:
            head, _, rest = text.partition(MARK)
            _, _, tail = rest.partition("<!-- /bdotempo-screens -->")
            text = head + BLOCK + tail
        else:
            text = text.replace("</body>", BLOCK + "</body>", 1)
        path.write_text(text, encoding="utf-8")
    print(f"{len(FILES)} mockups with the screen bar")


if __name__ == "__main__":
    main()
