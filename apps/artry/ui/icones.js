/* Ícones da barra do Artry: SVG desenhados como os do original (ToolbarIcons.cs), 18x18, traço fino,
   na cor do texto do tema (currentColor). Nenhum emoji. */
window.ICONES = {
  home: '<path d="M3 8.5L9 3.5l6 5"/><path d="M5 7.6V15h8V7.6"/><path d="M7.7 15v-3.6h2.6V15"/>',
  undo: '<path d="M4.5 7.5h6.2a3.8 3.8 0 0 1 0 7.6H7"/><path d="M4.5 7.5L7.6 4.4M4.5 7.5l3.1 3.1"/>',
  redo: '<path d="M13.5 7.5H7.3a3.8 3.8 0 0 0 0 7.6H11"/><path d="M13.5 7.5l-3.1-3.1M13.5 7.5l-3.1 3.1"/>',
  print: '<path d="M5 6.5V3h8v3.5"/><rect x="2.5" y="6.5" width="13" height="6" rx="1.2"/><path d="M5 10.5h8V15H5z" fill="currentColor" fill-opacity=".18"/><circle cx="13" cy="8.6" r=".6" fill="currentColor"/>',
  bold: '<text x="9" y="13.6" text-anchor="middle" font-family="Georgia,serif" font-weight="700" font-size="13" fill="currentColor" stroke="none">B</text>',
  italic: '<text x="9.4" y="13.6" text-anchor="middle" font-family="Georgia,serif" font-weight="700" font-style="italic" font-size="13" fill="currentColor" stroke="none">I</text>',
  underline: '<text x="9" y="12" text-anchor="middle" font-family="Georgia,serif" font-weight="700" font-size="12" fill="currentColor" stroke="none">U</text><path d="M4.5 15.2h9" stroke-width="2"/>',
  strike: '<text x="9" y="13.6" text-anchor="middle" font-family="Georgia,serif" font-weight="700" font-size="13" fill="currentColor" stroke="none">S</text><path d="M3.5 9.4h11" stroke-width="1.4"/>',
  textcolor: '<text x="9" y="12" text-anchor="middle" font-family="sans-serif" font-weight="700" font-size="12" fill="currentColor" stroke="none">A</text><rect class="barra-cor" x="3" y="14" width="12" height="3" fill="#d23" stroke="none"/>',
  highlight: '<path d="M6 11.5l5.5-7.5 2.5 1.8-5.5 7.5H6z"/><path d="M6 11.5l2.5 1.8" /><path d="M6 13.3l-1.2 1" /><rect class="barra-cor" x="3" y="15" width="12" height="2.5" fill="#ff0" stroke="none"/>',
  alignleft: '<path d="M3 3.5h12M3 7.5h8M3 11.5h12M3 15.5h7"/>',
  aligncenter: '<path d="M3 3.5h12M5 7.5h8M3 11.5h12M5.5 15.5h7"/>',
  alignright: '<path d="M3 3.5h12M7 7.5h8M3 11.5h12M8 15.5h7"/>',
  alignjustify: '<path d="M3 3.5h12M3 7.5h12M3 11.5h12M3 15.5h12"/>',
  linespacing: '<path d="M4 3v12M2 5l2-2 2 2M2 13l2 2 2-2"/><path d="M8.5 5h7M8.5 9h7M8.5 13h7" stroke-width="1.2"/>',
  bulletlist: '<circle cx="3.6" cy="4.5" r="1.2" fill="currentColor" stroke="none"/><circle cx="3.6" cy="9" r="1.2" fill="currentColor" stroke="none"/><circle cx="3.6" cy="13.5" r="1.2" fill="currentColor" stroke="none"/><path d="M7 4.5h8.5M7 9h8.5M7 13.5h8.5"/>',
  numberedlist: '<text x="3.5" y="6.6" text-anchor="middle" font-size="5.5" font-weight="700" fill="currentColor" stroke="none">1</text><text x="3.5" y="11.1" text-anchor="middle" font-size="5.5" font-weight="700" fill="currentColor" stroke="none">2</text><text x="3.5" y="15.6" text-anchor="middle" font-size="5.5" font-weight="700" fill="currentColor" stroke="none">3</text><path d="M7 4.5h8.5M7 9h8.5M7 13.5h8.5"/>',
  outdent: '<path d="M3 3.5h12M8 7.5h7M8 11.5h7M3 15.5h12"/><path d="M5.6 7.2L2.8 9.5l2.8 2.3z" fill="currentColor"/>',
  indent: '<path d="M3 3.5h12M8 7.5h7M8 11.5h7M3 15.5h12"/><path d="M3 7.2l2.8 2.3L3 11.8z" fill="currentColor"/>',
  clearformat: '<text x="8" y="13.6" text-anchor="middle" font-family="sans-serif" font-weight="700" font-size="12" fill="currentColor" stroke="none">T</text><path d="M4 15.5L14.5 3.5" stroke="#dc3232" stroke-width="1.6"/>',
  page: '<path d="M5 2.5h5.5L13.5 5.5V15.5H5z"/><path d="M10.5 2.5v3h3"/><path d="M7 9h4.5M7 11.5h4.5" stroke-width="1.2"/>',
  ruler: '<rect x="2" y="6" width="14" height="6" rx="1"/><path d="M5 6v2.4M8 6v3.4M11 6v2.4M14 6v3.4" stroke-width="1.2"/>',
  close: '<path d="M4.5 4.5l9 9M13.5 4.5l-9 9"/>',
  check: '<path d="M4 9.5l3.2 3.2L14 5.8"/>',
  plus: '<path d="M9 4v10M4 9h10"/>',
};
window.icone = function (nome, tam) {
  var t = tam || 18;
  return '<svg class="ic" width="' + t + '" height="' + t + '" viewBox="0 0 18 18" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round">' + (window.ICONES[nome] || '') + '</svg>';
};
