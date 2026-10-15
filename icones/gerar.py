#!/usr/bin/env python3
"""gerar.py — gera os ícones da suíte, todos no mesmo desenho: quadrado arredondado de cor chapada,
a inicial do app em minúscula serifada itálica preta, centrada, sombra suave. Cada app tem uma
inicial diferente (q n a v f t).
Uso: python3 icones/gerar.py            (todos)
     python3 icones/gerar.py nome cor letra"""
import math
import os
import sys

import cairo
import gi

gi.require_version("Pango", "1.0")
gi.require_version("PangoCairo", "1.0")
from gi.repository import Pango, PangoCairo  # noqa: E402

APPS = {"quilly": ("#ff0000", "q"), "noty": ("#88c03d", "n"), "artry": ("#fff200", "a"),
        "volury": ("#605ca8", "v"), "fotry": ("#1fb1b8", "f"), "takery": ("#f7931e", "t")}
S = 256
RAIO = 30
MARGEM = 10
PASTA = os.path.dirname(os.path.abspath(__file__))


def quadrado(cr, x, y, w, h, r):
    cr.new_sub_path()
    cr.arc(x + w - r, y + r, r, -math.pi / 2, 0)
    cr.arc(x + w - r, y + h - r, r, 0, math.pi / 2)
    cr.arc(x + r, y + h - r, r, math.pi / 2, math.pi)
    cr.arc(x + r, y + r, r, math.pi, 3 * math.pi / 2)
    cr.close_path()


def gerar(nome, COR, LETRA):
    saida = os.path.join(PASTA, nome + ".png")
    s = cairo.ImageSurface(cairo.FORMAT_ARGB32, S, S)
    cr = cairo.Context(s)
    # sombra: várias camadas deslocadas, cada vez mais transparentes
    for i in range(8, 0, -1):
        quadrado(cr, MARGEM + i * 0.6, MARGEM + i * 0.9, S - 2 * MARGEM, S - 2 * MARGEM, RAIO)
        cr.set_source_rgba(0, 0, 0, 0.045)
        cr.fill()
    r, g, b = (int(COR[i:i + 2], 16) / 255 for i in (1, 3, 5))
    quadrado(cr, MARGEM, MARGEM, S - 2 * MARGEM, S - 2 * MARGEM, RAIO)
    cr.set_source_rgb(r, g, b)
    cr.fill()
    # letra
    lay = PangoCairo.create_layout(cr)
    lay.set_font_description(Pango.FontDescription("Source Serif 4 Display Bold Italic 215px"))
    lay.set_text(LETRA, -1)
    ink, logical = lay.get_pixel_extents()
    cr.set_source_rgb(0.05, 0.05, 0.05)
    cr.move_to((S - ink.width) / 2 - ink.x, (S - ink.height) / 2 - ink.y + 2)
    PangoCairo.show_layout(cr, lay)
    s.write_to_png(saida)
    print(saida)


if __name__ == "__main__":
    if len(sys.argv) == 4:
        gerar(sys.argv[1], sys.argv[2], sys.argv[3])
    else:
        for nome, (cor, letra) in APPS.items():
            gerar(nome, cor, letra)
