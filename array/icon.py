#!/usr/bin/env python3
"""icon.py — gera icones/array.png no estilo dos outros ícones da suíte (quadrado arredondado de cor
chapada, letra serifada itálica preta, sombra suave). Uso: python3 array/icon.py [cor] [letra]"""
import math
import os
import sys

import cairo
import gi

gi.require_version("Pango", "1.0")
gi.require_version("PangoCairo", "1.0")
from gi.repository import Pango, PangoCairo  # noqa: E402

COR = sys.argv[1] if len(sys.argv) > 1 else "#1fb1b8"
LETRA = sys.argv[2] if len(sys.argv) > 2 else "A"
S = 256
RAIO = 30
MARGEM = 10
SAIDA = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "icones", "array.png")


def quadrado(cr, x, y, w, h, r):
    cr.new_sub_path()
    cr.arc(x + w - r, y + r, r, -math.pi / 2, 0)
    cr.arc(x + w - r, y + h - r, r, 0, math.pi / 2)
    cr.arc(x + r, y + h - r, r, math.pi / 2, math.pi)
    cr.arc(x + r, y + r, r, math.pi, 3 * math.pi / 2)
    cr.close_path()


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
s.write_to_png(SAIDA)
print(SAIDA)
