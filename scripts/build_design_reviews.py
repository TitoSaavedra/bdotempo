"""Builds docs/design/revisiones.html: one self-contained page with the ten redesign mockups embedded as

srcdoc, a side rail to move between them and a voting/notes panel saved in the browser.

Run it from the repository root:  python scripts/build_design_reviews.py

The runtime (rail, voting panel) lives in the page itself; this script only embeds the ten mockups.
"""

from __future__ import annotations

from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DESIGN = ROOT / "docs" / "design"

CONCEPTS = [
    ("1", "mockup-native-01-familia-windows.html", "Familia Windows", "Nativo",
     "Fluent bien hecho: Mica, tarjetas y jerarquía correcta. La opción segura y «de Windows»."),
    ("2", "mockup-native-02-estudio-oscuro.html", "Estudio oscuro", "Nativo",
     "Negro cálido por capas y dorado; el combo como protagonista."),
    ("3", "mockup-native-03-consola-de-juego.html", "Consola de juego", "Nativo",
     "Tabla densa, cifras monoespaciadas, rail con tus ranuras y estado del juego abajo."),
    ("4", "mockup-native-04-minimal-blanco.html", "Minimal claro", "Nativo",
     "Tema claro limpio, mucho aire, máxima legibilidad."),
    ("5", "mockup-native-05-profesional-selecto.html", "Profesional selecto", "Nativo",
     "Pizarra profunda, emblemas de clase y acabado premium."),
    ("6", "mockup-webui-06-sistema-webui2.html", "WebUI 2", "WebUI",
     "Panel administrativo maduro: rejilla, tarjetas blancas y un azul primario."),
    ("7", "mockup-webui-07-sistema-webui3.html", "WebUI 3", "WebUI",
     "Aurora suave, cristal, píldoras y botón principal flotante."),
    ("8", "mockup-webui-08-tablero-bento.html", "Tablero bento", "WebUI",
     "Mosaico asimétrico de tarjetas con ritmo fuerte."),
    ("9", "mockup-webui-09-editorial-demanding.html", "Editorial digital", "WebUI",
     "Tipografía grande y reglas finas: jerarquía en vez de cajas."),
    ("10", "mockup-webui-10-monospace-dev.html", "Monospace dev", "WebUI",
     "Monoespaciado, gutter de líneas y pastillas de estado."),
]

START = "<!-- RUNTIME -->"
END = "<!-- /RUNTIME -->"
EMBED_START = "<!-- MOCKUPS -->"
EMBED_END = "<!-- /MOCKUPS -->"


def main() -> None:
    template = (DESIGN / "revisiones.html").read_text(encoding="utf-8")
    if START not in template or END not in template:
        raise SystemExit("The page lost its runtime markers; check revisiones.html before rebuilding.")

    embeds = []
    for number, file_name, name, _kind, _caption in CONCEPTS:
        html = (DESIGN / file_name).read_text(encoding="utf-8").replace("</script", "<\\/script")
        embeds.append(
            f'<script type="text/html" id="M{number}" data-name="{name}">\n{html}\n</script>'
        )

    before = template.split(EMBED_START)[0]
    after = template.split(EMBED_END)[1] if EMBED_END in template else ""
    rebuilt = before + EMBED_START + "\n" + "\n".join(embeds) + "\n" + EMBED_END + after

    (DESIGN / "revisiones.html").write_text(rebuilt, encoding="utf-8")
    print(f"revisiones.html: {len(embeds)} concepts embedded, {len(rebuilt) // 1024} KB")


if __name__ == "__main__":
    main()
