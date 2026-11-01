"""Graba una captura de cada pantalla de cada mockup para las miniaturas del índice (docs/design/thumbs).

El script pone la pantalla en el propio archivo del concepto solo para la captura (atributo data-screen en <html> o el
ancla #2 / #3), nunca modifica los mockups. Necesita Microsoft Edge (viene con Windows).

    python scripts/shoot_design_thumbs.py
"""

from __future__ import annotations

import re
import subprocess
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DESIGN = ROOT / "docs" / "design"
THUMBS = DESIGN / "thumbs"

EDGE_CANDIDATES = [
    Path(r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"),
    Path(r"C:\Program Files\Microsoft\Edge\Application\msedge.exe"),
]

CONCEPTS = {
    1: "mockup-native-01-familia-windows.html",
    2: "mockup-native-02-estudio-oscuro.html",
    3: "mockup-native-03-consola-de-juego.html",
    4: "mockup-native-04-minimal-blanco.html",
    5: "mockup-native-05-profesional-selecto.html",
    6: "mockup-webui-06-sistema-webui2.html",
    7: "mockup-webui-07-sistema-webui3.html",
    8: "mockup-webui-08-tablero-bento.html",
    9: "mockup-webui-09-editorial-demanding.html",
    10: "mockup-webui-10-monospace-dev.html",
}

SCREENS = {1: "combos", 2: "editor", 3: "config"}

HTML_TAG = re.compile(r"<html\b[^>]*>", re.IGNORECASE)


def find_edge() -> Path:
    for candidate in EDGE_CANDIDATES:
        if candidate.exists():
            return candidate
    raise SystemExit("Microsoft Edge was not found; install it or edit EDGE_CANDIDATES.")


def stage(html: str, screen: int, folder: Path, name: str) -> Path:
    """Writes a copy of a concept with the wanted screen selected from the first paint."""
    tagged = HTML_TAG.sub(lambda match: match.group(0).replace("<html", f'<html data-screen="{screen}"', 1), html, count=1)
    path = folder / name
    path.write_text(tagged, encoding="utf-8")
    return path


def main() -> None:
    edge = find_edge()
    THUMBS.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory() as scratch:
        scratch_dir = Path(scratch)
        for number, file_name in CONCEPTS.items():
            html = (DESIGN / file_name).read_text(encoding="utf-8")
            for screen, label in SCREENS.items():
                staged = stage(html, screen, scratch_dir, f"{number}-{screen}.html")
                out = THUMBS / f"{number:02d}-{label}.png"
                subprocess.run(
                    [
                        str(edge),
                        "--headless=new",
                        "--disable-gpu",
                        "--hide-scrollbars",
                        "--window-size=1500,1000",
                        f"--screenshot={out}",
                        "file:///" + staged.as_posix(),
                    ],
                    check=False,
                    capture_output=True,
                    timeout=120,
                )
                print(f"{out.name}: {'ok' if out.exists() else 'failed'}")


if __name__ == "__main__":
    main()
