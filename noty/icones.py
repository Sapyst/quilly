"""icones.py — ícones SVG de traço fino (estilo Lucide), monocromáticos: herdam a cor do texto.
Usados no lugar dos emojis das interfaces originais (a suíte não usa emoji em lugar nenhum)."""
P = {
    "novo": '<path d="M14 3H7a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8z"/><path d="M14 3v5h5"/><path d="M12 12v6M9 15h6"/>',
    "abrir": '<path d="M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v1"/><path d="M3 7v10a2 2 0 0 0 2 2h12.5a2 2 0 0 0 1.9-1.4L21.8 11H6.6a2 2 0 0 0-1.9 1.4L3 17"/>',
    "salvar": '<path d="M5 3h11l5 5v11a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2z"/><path d="M7 3v5h8V3"/><path d="M7 21v-7h10v7"/>',
    "salvar_como": '<path d="M5 3h11l5 5v5"/><path d="M3 5a2 2 0 0 1 2-2"/><path d="M3 5v14a2 2 0 0 0 2 2h6"/><path d="M7 3v5h8V3"/><path d="M18.5 14.5l2 2L15 22h-2v-2z"/>',
    "importar": '<path d="M12 3v12"/><path d="M7 10l5 5 5-5"/><path d="M5 21h14"/>',
    "imagem": '<rect x="3" y="3" width="18" height="18" rx="2"/><circle cx="9" cy="9" r="2"/><path d="M21 15l-5-5L5 21"/>',
    "css": '<path d="M14 3H7a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8z"/><path d="M14 3v5h5"/><path d="M10 13l-2 2 2 2M14 13l2 2-2 2"/>',
    "sumario": '<path d="M8 6h13M8 12h13M8 18h13"/><path d="M3 6h.01M3 12h.01M3 18h.01"/>',
    "validar": '<path d="M20 6L9 17l-5-5"/>',
    "exportar": '<path d="M21 8l-9-5-9 5v8l9 5 9-5z"/><path d="M3.3 7.6L12 12.6l8.7-5"/><path d="M12 22V12.6"/>',
    "livro": '<path d="M4 19.5A2.5 2.5 0 0 1 6.5 17H20V3H6.5A2.5 2.5 0 0 0 4 5.5z"/><path d="M4 19.5A2.5 2.5 0 0 0 6.5 22H20v-5"/>',
    "lista": '<path d="M9 6h12M9 12h12M9 18h12"/><circle cx="4" cy="6" r="1"/><circle cx="4" cy="12" r="1"/><circle cx="4" cy="18" r="1"/>',
    "link": '<path d="M10 13a5 5 0 0 0 7.5.5l3-3a5 5 0 0 0-7-7l-1.7 1.7"/><path d="M14 11a5 5 0 0 0-7.5-.5l-3 3a5 5 0 0 0 7 7l1.7-1.7"/>',
    "capitulo": '<path d="M14 3H7a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8z"/><path d="M14 3v5h5"/><path d="M9 13h6M9 17h6"/>',
    "fechar": '<path d="M18 6L6 18M6 6l12 12"/>',
    "erro": '<circle cx="12" cy="12" r="9"/><path d="M15 9l-6 6M9 9l6 6"/>',
    "aviso": '<path d="M10.3 3.9L1.8 18a2 2 0 0 0 1.7 3h17a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0z"/><path d="M12 9v4M12 17h.01"/>',
    "ok": '<circle cx="12" cy="12" r="9"/><path d="M8 12l3 3 5-6"/>',
    "pasta": '<path d="M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"/>',
    "lixeira": '<path d="M3 6h18M8 6V4h8v2M6 6l1 14h10l1-14"/>',
    "atualizar": '<path d="M21 12a9 9 0 1 1-2.6-6.4L21 8"/><path d="M21 3v5h-5"/>',
    "atalho": '<path d="M15 3h6v6M10 14L21 3M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6"/>',
    "mais": '<path d="M12 5v14M5 12h14"/>',
    "buscar": '<circle cx="11" cy="11" r="7"/><path d="M21 21l-4.3-4.3"/>',
}


def svg(nome, classe="ic"):
    return (f'<svg class="{classe}" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" '
            f'stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">{P[nome]}</svg>')


CSS = ".ic{width:15px;height:15px;vertical-align:-3px;margin-right:6px;flex:none}" \
      " button .ic:only-child,.ic.so{margin-right:0}"
