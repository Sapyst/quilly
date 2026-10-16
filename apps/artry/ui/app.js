/* Artry (Arch Quilly) — editor. Refeito em HTML/JS a partir da especificação do Artry de Windows.
   Ponte com o lado nativo (artry.py): rpc(metodo, args) -> Promise; send({type,...}) avisos soltos. */
(function () {
'use strict';

/* ================================================================ ponte */
var seq = 0, pend = {};
function rpc(m, a) {
  return new Promise(function (ok, erro) {
    var id = ++seq; pend[id] = { ok: ok, erro: erro };
    window.chrome.webview.postMessage({ type: 'rpc', id: id, method: m, args: a || {} });
  });
}
function send(m) { window.chrome.webview.postMessage(m); }
window.chrome.webview.addEventListener('message', function (ev) {
  var m = ev.data || {};
  if (m.type === 'rpc-reply') {
    var p = pend[m.id]; if (!p) return; delete pend[m.id];
    if (m.ok) p.ok(m.data); else p.erro(new Error(m.error || 'erro'));
  } else if (m.type === 'confirmExit') confirmarSaida();
  else if (m.type === 'open') abrirCaminho(m.path, true);
});

var $ = function (id) { return document.getElementById(id); };
var doc = $('doc'), folha = $('folha'), trabalho = $('trabalho'), calha = $('calha');
var cfg = {}, info = {};
var atual = { path: null, name: null, ext: '.hyp', kind: 'rich', frontmatter: '' };
var sujo = false, modoCodigo = false, zoom = 1;

/* ================================================================ utilidades */
function esc(s) { return String(s == null ? '' : s).replace(/[&<>"]/g, function (c) { return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]; }); }
function ext(nome) { var m = /\.[^.\/]+$/.exec(nome || ''); return m ? m[0].toLowerCase() : ''; }
var EXT_CODIGO = ['.cs', '.csx', '.js', '.ts', '.py', '.java', '.cpp', '.c', '.h', '.css', '.sql', '.sh', '.ps1', '.json', '.xml', '.html', '.yml', '.yaml'];
function salvarConfig(parcial) { Object.assign(cfg, parcial); return rpc('settings.set', parcial).catch(function () {}); }
document.querySelectorAll('i[data-ic]').forEach(function (i) { i.outerHTML = icone(i.getAttribute('data-ic')); });

/* ================================================================ tema */
function aplicarTema() {
  document.body.classList.toggle('dark', !!cfg.Dark);
  document.body.classList.toggle('light', !cfg.Dark);
}
function alternarTema() { salvarConfig({ Dark: !cfg.Dark }); aplicarTema(); desenharReguas(); }

/* ================================================================ diálogos */
function abrirModal(html, aoMontar) {
  $('dlg').innerHTML = html; $('modal').hidden = false;
  if (aoMontar) aoMontar($('dlg'));
  var f = $('dlg').querySelector('input,button.primario'); if (f) setTimeout(function () { f.focus(); }, 30);
}
function fecharModal() { $('modal').hidden = true; $('dlg').innerHTML = ''; doc.focus(); }
function pergunta(titulo, rotulo, valor) {
  return new Promise(function (ok) {
    abrirModal('<h2>' + esc(titulo) + '</h2><p>' + esc(rotulo) + '</p><input type="text" id="pg-v" value="' + esc(valor || '') + '">' +
      '<div class="botoes"><button id="pg-c">Cancelar</button><button class="primario" id="pg-o">OK</button></div>', function (d) {
      var v = d.querySelector('#pg-v'); v.select();
      d.querySelector('#pg-o').onclick = function () { var x = v.value; fecharModal(); ok(x); };
      d.querySelector('#pg-c').onclick = function () { fecharModal(); ok(null); };
      v.onkeydown = function (e) { if (e.key === 'Enter') d.querySelector('#pg-o').click(); if (e.key === 'Escape') d.querySelector('#pg-c').click(); };
    });
  });
}
function aviso(titulo, texto) {
  return new Promise(function (ok) {
    abrirModal('<h2>' + esc(titulo) + '</h2><p>' + esc(texto) + '</p><div class="botoes"><button class="primario" id="av-ok">OK</button></div>', function (d) {
      d.querySelector('#av-ok').onclick = function () { fecharModal(); ok(); };
    });
  });
}
function escolha(titulo, texto, botoes) {      // botoes: [{id, rotulo, primario}]
  return new Promise(function (ok) {
    abrirModal('<h2>' + esc(titulo) + '</h2><p>' + esc(texto) + '</p><div class="botoes">' +
      botoes.map(function (b) { return '<button data-id="' + b.id + '"' + (b.primario ? ' class="primario"' : '') + '>' + esc(b.rotulo) + '</button>'; }).join('') + '</div>', function (d) {
      d.querySelectorAll('.botoes button').forEach(function (b) { b.onclick = function () { fecharModal(); ok(b.getAttribute('data-id')); }; });
    });
  });
}
function podeDescartar() {
  if (!sujo) return Promise.resolve(true);
  return escolha('Artry', 'Há alterações não salvas. Descartar?', [{ id: 'nao', rotulo: 'Não' }, { id: 'sim', rotulo: 'Sim, descartar', primario: true }])
    .then(function (r) { return r === 'sim'; });
}
function confirmarSaida() {
  escolha('Artry', 'Há alterações não salvas.', [{ id: 'ficar', rotulo: 'Cancelar' }, { id: 'sair', rotulo: 'Descartar e sair' }, { id: 'salvar', rotulo: 'Salvar e sair', primario: true }])
    .then(function (r) {
      if (r === 'sair') rpc('app.exitForce');
      else if (r === 'salvar') salvar().then(function (ok) { if (ok) rpc('app.exitForce'); });
    });
}

/* ================================================================ tela inicial */
function mostrarHome() {
  podeDescartar().then(function (ok) {
    if (!ok) return;
    marcarSujo(false);
    $('editor').hidden = true; $('home').hidden = false;
    send({ type: 'title', text: 'Artry' }); send({ type: 'hasdoc', value: false });
    montarCards();
  });
}
function montarCards() {
  var cards = $('cards'); cards.innerHTML = '';
  var novo = document.createElement('div'); novo.className = 'card';
  novo.innerHTML = '<div class="papel"></div><div class="mais">' + icone('plus', 46) + '</div><div class="faixa">Novo documento</div>';
  novo.onclick = function () { novoDocumento(); };
  cards.appendChild(novo);
  rpc('home.cards').then(function (lista) {
    (lista || []).forEach(function (c) {
      var el = document.createElement('div'); el.className = 'card'; el.title = c.path;
      var linhas = (c.lines || []).map(function (l, i) {
        var t = l.replace(/^[#\-\s]+/, '');
        if (!t.trim()) return '<div class="ln vazia"></div>';
        var tit = i === 0 || /^#/.test(l) || (t.length < 30 && /^[A-ZÀ-Ý]/.test(t));
        return '<div class="ln' + (tit ? ' tit' : '') + '">' + esc(t.length > 34 ? t.slice(0, 34) + '…' : t) + '</div>';
      }).join('');
      var nome = c.name.length > 21 ? c.name.slice(0, 19) + '…' : c.name;
      el.innerHTML = '<div class="papel">' + linhas + '<div class="geada"></div></div><div class="faixa">' + esc(nome) + '</div>';
      el.onclick = function () { abrirCaminho(c.path, false); };
      el.oncontextmenu = function (e) {
        e.preventDefault();
        menuFlutuante(e.clientX, e.clientY, [
          ['Abrir no editor', function () { abrirCaminho(c.path, false); }],
          ['Abrir local do arquivo', function () { rpc('file.reveal', { path: c.path }); }],
          null,
          ['Remover dos recentes', function () { rpc('recent.remove', { path: c.path }).then(montarCards); }],
        ]);
      };
      cards.appendChild(el);
    });
  });
}
$('home-abrir').onclick = function () { abrir(); };
$('home-config').onclick = function () { dialogoAtalhos(); };
$('home-tema').onclick = alternarTema;

/* ================================================================ documento */
function mostrarEditor() {
  $('home').hidden = true; $('editor').hidden = false; send({ type: 'hasdoc', value: true });
  setTimeout(function () { layout(); doc.focus(); }, 30);
}
function marcarSujo(v) { sujo = v; send({ type: 'dirty', value: v }); atualizarTitulo(); atualizarStatus(); }
function atualizarTitulo() {
  send({ type: 'title', text: (sujo ? '• ' : '') + (atual.name ? atual.name + ' — Artry' : 'Artry') });
}
function novoDocumento() {
  podeDescartar().then(function (ok) {
    if (!ok) return;
    atual = { path: null, name: null, ext: '.hyp', kind: 'rich', frontmatter: '' };
    definirModoCodigo(false);
    doc.innerHTML = '<p><br></p>';
    marcarSujo(false); mostrarEditor();
  });
}
function carregarDoc(d) {
  atual = { path: d.path, name: d.name, ext: d.ext, kind: d.kind, frontmatter: d.frontmatter || '' };
  if (d.kind === 'code' || (d.kind === 'text' && !d.html)) {
    if (d.kind === 'code') { definirModoCodigo(true); doc.textContent = d.text; realcar(); }
    else { definirModoCodigo(false); doc.innerHTML = textoParaHtml(d.text); }
  } else {
    definirModoCodigo(false);
    doc.innerHTML = d.html || '<p><br></p>';
  }
  marcarSujo(false); mostrarEditor(); folha.scrollTop = 0;
}
function textoParaHtml(t) {
  return String(t || '').replace(/\r\n/g, '\n').split('\n').map(function (l) { return '<p>' + (esc(l) || '<br>') + '</p>'; }).join('');
}
function abrir() {
  podeDescartar().then(function (ok) {
    if (!ok) return;
    rpc('file.openDialog').then(function (d) { if (d) carregarDoc(d); })
      .catch(function (e) { aviso('Não consegui abrir', e.message); });
  });
}
function abrirCaminho(p, perguntar) {
  (perguntar ? podeDescartar() : Promise.resolve(true)).then(function (ok) {
    if (!ok) return;
    rpc('file.openPath', { path: p }).then(carregarDoc).catch(function (e) { aviso('Não consegui abrir', e.message); });
  });
}
function conteudoParaSalvar(destinoExt) {
  var r = { html: doc.innerHTML, text: textoPuro(), kind: modoCodigo ? 'code' : atual.kind };
  if (destinoExt === '.hyp' || destinoExt === '.md' || destinoExt === '.markdown') r.markdown = paraMarkdown(destinoExt);
  return r;
}
function salvarEm(caminho) {
  var e = ext(caminho);
  var a = Object.assign({ path: caminho }, conteudoParaSalvar(e));
  return rpc('file.save', a).then(function (r) {
    atual.path = r.path; atual.name = r.name; atual.ext = e;
    if (e === '.hyp' && !atual.frontmatter) atual.frontmatter = frontmatterNovo();
    marcarSujo(false); return true;
  }).catch(function (err) { aviso('Não consegui salvar', err.message); return false; });
}
function salvar() { return atual.path ? salvarEm(atual.path) : salvarComo(); }
function salvarComo() {
  var nome = atual.name || 'sem título.hyp';
  return rpc('file.saveDialog', { name: nome }).then(function (r) { return r ? salvarEm(r.path) : false; });
}
function textoPuro() {
  if (modoCodigo) return doc.innerText.replace(/\n$/, '');
  var linhas = [];
  doc.childNodes.forEach(function (n) {
    if (n.nodeType === 3) { if (n.textContent.trim()) linhas.push(n.textContent); return; }
    if (n.nodeType !== 1) return;
    if (n.tagName === 'UL' || n.tagName === 'OL') {
      Array.prototype.forEach.call(n.children, function (li, i) { linhas.push((n.tagName === 'UL' ? '• ' : (i + 1) + '. ') + li.innerText); });
    } else linhas.push(n.innerText.replace(/\n$/, ''));
  });
  return linhas.join('\n');
}

/* ---------- HTML -> Markdown (.hyp v2 com anexos / .md) ---------- */
function frontmatterNovo() {
  var d = new Date(); var data = d.getFullYear() + '-' + String(d.getMonth() + 1).padStart(2, '0') + '-' + String(d.getDate()).padStart(2, '0');
  return '---\ntitulo: ' + (atual.name || 'sem título').replace(/\.[^.]+$/, '') + '\ncriado: ' + data + '\napp: Artry\nformato: hyp-v2\n---\n';
}
function paraMarkdown(destino) {
  var anexos = [], n = 0;
  function inline(no) {
    var s = '';
    no.childNodes.forEach(function (c) {
      if (c.nodeType === 3) { s += c.textContent.replace(/([*_`\\])/g, '\\$1'); return; }
      if (c.nodeType !== 1) return;
      var t = c.tagName, dentro = inline(c), st = c.getAttribute('style') || '';
      if (t === 'BR') s += '  \n';
      else if (t === 'B' || t === 'STRONG' || /font-weight:\s*(bold|[6-9]00)/.test(st)) s += dentro.trim() ? '**' + dentro + '**' : dentro;
      else if (t === 'I' || t === 'EM' || /font-style:\s*italic/.test(st)) s += dentro.trim() ? '*' + dentro + '*' : dentro;
      else if (t === 'S' || t === 'STRIKE' || t === 'DEL' || /line-through/.test(st)) s += dentro.trim() ? '~~' + dentro + '~~' : dentro;
      else if (t === 'CODE') s += '`' + c.textContent + '`';
      else if (t === 'A') s += '[' + dentro + '](' + (c.getAttribute('href') || '') + ')';
      else if (t === 'IMG') {
        var src = c.getAttribute('src') || '', alt = c.getAttribute('alt') || 'imagem';
        var m = /^data:([\w\/+.-]+);base64,(.*)$/.exec(src);
        if (m && destino === '.hyp') {
          var extensao = (m[1].split('/')[1] || 'bin').replace('jpeg', 'jpg').replace('svg+xml', 'svg');
          var id = 'imagem-' + (++n) + '.' + extensao;
          anexos.push({ id: id, mime: m[1], b64: m[2] });
          s += '![' + alt + '](anexo:' + id + ')';
        } else s += '![' + alt + '](' + src + ')';
      } else s += dentro;
    });
    return s;
  }
  var blocos = [];
  doc.childNodes.forEach(function (b) {
    if (b.nodeType === 3) { if (b.textContent.trim()) blocos.push(b.textContent); return; }
    if (b.nodeType !== 1) return;
    var t = b.tagName;
    if (t === 'H1') blocos.push('# ' + inline(b).trim());
    else if (t === 'H2') blocos.push('## ' + inline(b).trim());
    else if (t === 'H3' || t === 'H4') blocos.push('### ' + inline(b).trim());
    else if (t === 'UL' || t === 'OL') {
      blocos.push(Array.prototype.map.call(b.children, function (li, i) { return (t === 'UL' ? '- ' : (i + 1) + '. ') + inline(li).trim(); }).join('\n'));
    } else if (t === 'BLOCKQUOTE') blocos.push(inline(b).trim().split('\n').map(function (l) { return '> ' + l; }).join('\n'));
    else if (t === 'PRE') blocos.push('```\n' + b.textContent + '\n```');
    else { var x = inline(b); blocos.push(x.replace(/\s+$/, '')); }
  });
  var corpo = blocos.join('\n\n').replace(/\n{3,}/g, '\n\n');
  var fm = destino === '.hyp' ? (atual.frontmatter || frontmatterNovo()) : (atual.frontmatter || '');
  var saida = (fm ? fm.replace(/\s*$/, '\n') : '') + corpo + '\n';
  if (anexos.length) {
    saida += '\n<!-- ===== ANEXOS HYP ===== -->\n';
    anexos.forEach(function (a) { saida += '```hyp-anexo ' + a.id + ' ' + a.mime + '\n' + a.b64.replace(/(.{76})/g, '$1\n').replace(/\n$/, '') + '\n```\n'; });
  }
  return saida;
}

/* ================================================================ formatação */
function exec(cmd, val) { doc.focus(); document.execCommand('styleWithCSS', false, true); document.execCommand(cmd, false, val); marcarSujo(true); estadoBarra(); }
function blocosSelecionados() {
  var sel = window.getSelection(); if (!sel.rangeCount) return [];
  var r = sel.getRangeAt(0), lista = [];
  function bloco(n) { while (n && n !== doc) { if (n.nodeType === 1 && /^(P|DIV|H[1-6]|LI|BLOCKQUOTE|PRE)$/.test(n.tagName)) return n; n = n.parentNode; } return null; }
  var ini = bloco(r.startContainer), fim = bloco(r.endContainer);
  if (!ini) { document.execCommand('formatBlock', false, 'p'); ini = bloco(sel.getRangeAt(0).startContainer); fim = ini; }
  if (!ini) return [];
  var todos = doc.querySelectorAll('p,div,h1,h2,h3,h4,h5,h6,li,blockquote,pre');
  var dentro = false;
  todos.forEach(function (b) { if (b === ini) dentro = true; if (dentro && !b.querySelector('p,div,li')) lista.push(b); if (b === fim) dentro = false; });
  return lista.length ? lista : [ini];
}
function tamanhoSelecao(pt) {
  pt = Math.max(1, Math.min(300, Math.round(pt)));
  doc.focus(); document.execCommand('styleWithCSS', false, false); document.execCommand('fontSize', false, '7');
  doc.querySelectorAll('font[size="7"]').forEach(function (f) {
    var s = document.createElement('span'); s.style.fontSize = pt + 'pt';
    while (f.firstChild) s.appendChild(f.firstChild); f.replaceWith(s);
  });
  doc.querySelectorAll('span[style*="xxx-large"]').forEach(function (s) { s.style.fontSize = pt + 'pt'; });
  $('tamanho').value = pt; marcarSujo(true);
}
function fonteSelecao(nome) { if (nome) exec('fontName', nome); }
function aplicarEstilo(v) {
  var tag = { p: 'p', titulo: 'h1', subtitulo: 'h2', t1: 'h1', t2: 'h2', t3: 'h3' }[v] || 'p';
  doc.focus(); document.execCommand('formatBlock', false, tag);
  blocosSelecionados().forEach(function (b) { if (v === 'p') b.removeAttribute('class'); else b.className = v; });
  marcarSujo(true);
}
function alinhar(a) {
  exec({ left: 'justifyLeft', center: 'justifyCenter', right: 'justifyRight', justify: 'justifyFull' }[a]);
}
function espacamento(lh) { blocosSelecionados().forEach(function (b) { b.style.lineHeight = lh; }); marcarSujo(true); }
function recuo(delta) {
  blocosSelecionados().forEach(function (b) {
    var atualPx = parseFloat(b.style.marginLeft) || 0;
    var novo = Math.max(0, atualPx + delta);
    b.style.marginLeft = novo ? novo + 'px' : '';
  });
  marcarSujo(true); desenharReguas();
}
function limparFormatacao() {
  exec('removeFormat');
  blocosSelecionados().forEach(function (b) {
    b.removeAttribute('style'); b.removeAttribute('class');
    if (/^H[1-6]$/.test(b.tagName)) { var p = document.createElement('p'); while (b.firstChild) p.appendChild(b.firstChild); b.replaceWith(p); }
  });
  if (document.queryCommandState('insertUnorderedList')) document.execCommand('insertUnorderedList');
  if (document.queryCommandState('insertOrderedList')) document.execCommand('insertOrderedList');
}
var faixaGuardada = null;
function guardarSelecao() { var s = window.getSelection(); faixaGuardada = s.rangeCount ? s.getRangeAt(0).cloneRange() : null; }
function voltarSelecao() { if (faixaGuardada) { doc.focus(); var s = window.getSelection(); s.removeAllRanges(); s.addRange(faixaGuardada); } }
function escolherCor(input, aoEscolher) {
  guardarSelecao();
  input.oninput = null;
  input.onchange = function () { voltarSelecao(); aoEscolher(input.value); };
  input.click();
}
function estadoBarra() {
  if (modoCodigo) return;
  document.querySelectorAll('[data-estado]').forEach(function (b) {
    var on = false; try { on = document.queryCommandState(b.getAttribute('data-estado')); } catch (e) {}
    b.classList.toggle('on', on);
  });
  var s = window.getSelection(); if (!s.rangeCount) return;
  var n = s.anchorNode; if (!n || !doc.contains(n)) return;
  var el = n.nodeType === 1 ? n : n.parentElement, cs = getComputedStyle(el);
  if (document.activeElement !== $('fonte')) $('fonte').value = cs.fontFamily.split(',')[0].replace(/['"]/g, '');
  if (document.activeElement !== $('tamanho')) $('tamanho').value = Math.round(parseFloat(cs.fontSize) * 0.75 / zoom * zoom);
  var cor = cs.color; var barra = document.querySelector('[data-cmd="Cor do texto"] .barra-cor'); if (barra) barra.setAttribute('fill', cor);
  var bl = blocosSelecionados()[0];
  if (bl) {
    var est = bl.className && /^(titulo|subtitulo|t1|t2|t3)$/.test(bl.className) ? bl.className : (bl.tagName === 'H1' ? 't1' : bl.tagName === 'H2' ? 't2' : bl.tagName === 'H3' ? 't3' : 'p');
    $('estilos').value = est;
  }
}

/* ================================================================ modo código */
var PALAVRAS = 'class public private internal static void string int bool new return if else for foreach while using namespace const var function let import from def async await true false null select from where create insert update delete'.split(' ');
function definirModoCodigo(on) {
  modoCodigo = on;
  doc.classList.toggle('codigo', on);
  doc.setAttribute('contenteditable', on ? 'plaintext-only' : 'true');
  doc.spellcheck = !on;
  $('tb-code').hidden = !(on || cfg.UIBarraCodigo);
  if (on) { definirQuebra(false); }
  else { definirQuebra(cfg.WordWrap !== false); }
  atualizarStatus(); numeros();
}
function realcar() {
  var texto = doc.innerText.replace(/\n$/, '');
  if (texto.length > 250000) return;
  var caret = posicaoCaretTexto();
  var reKw = new RegExp('\\b(' + PALAVRAS.join('|') + ')\\b', 'gi');
  var com = /(\.py|\.sh|\.ps1|\.yml|\.yaml)$/.test(atual.ext || '') ? /#.*$/ : /\/\/.*$|#.*$/;
  doc.innerHTML = texto.split('\n').map(function (l) {
    var marcas = [];                       // [ini, fim, classe]: o último a pintar ganha (como no original)
    function achar(re, cls) { var m, r = new RegExp(re.source, re.flags.indexOf('g') < 0 ? re.flags + 'g' : re.flags); while ((m = r.exec(l))) { if (!m[0].length) { r.lastIndex++; continue; } marcas.push([m.index, m.index + m[0].length, cls]); } }
    achar(/\b\d+(\.\d+)?\b/, 'hl-num'); achar(reKw, 'hl-kw'); achar(/"(?:\\.|[^"\\])*"|'(?:\\.|[^'\\])*'/, 'hl-str'); achar(com, 'hl-com');
    var cls = new Array(l.length).fill('');
    marcas.forEach(function (mk) { for (var i = mk[0]; i < mk[1]; i++) cls[i] = mk[2]; });
    var out = '', i = 0;
    while (i < l.length) { var c = cls[i], j = i; while (j < l.length && cls[j] === c) j++; out += c ? '<span class="' + c + '">' + esc(l.slice(i, j)) + '</span>' : esc(l.slice(i, j)); i = j; }
    return out;
  }).join('\n') + '\n';
  definirCaretTexto(caret); numeros();
}
function posicaoCaretTexto() {
  var s = window.getSelection(); if (!s.rangeCount || !doc.contains(s.anchorNode)) return 0;
  var r = s.getRangeAt(0).cloneRange(); r.selectNodeContents(doc); r.setEnd(s.anchorNode, s.anchorOffset);
  return r.toString().length;
}
function definirCaretTexto(pos) {
  var andou = 0, w = document.createTreeWalker(doc, NodeFilter.SHOW_TEXT), n;
  while ((n = w.nextNode())) {
    if (andou + n.length >= pos) { var s = window.getSelection(), r = document.createRange(); r.setStart(n, pos - andou); r.collapse(true); s.removeAllRanges(); s.addRange(r); return; }
    andou += n.length;
  }
}
function linhaAtualCodigo() {
  var t = doc.innerText, p = posicaoCaretTexto();
  var ini = t.lastIndexOf('\n', p - 1) + 1, fim = t.indexOf('\n', p); if (fim < 0) fim = t.length;
  return { texto: t, ini: ini, fim: fim, linha: t.slice(0, p).split('\n').length, col: p - ini + 1 };
}
function trocarTextoCodigo(novo, caret) { doc.textContent = novo; realcar(); definirCaretTexto(caret); marcarSujo(true); }
function comentar() {
  if (!modoCodigo) return;
  var l = linhaAtualCodigo(), prefixo = /(\.py|\.sh|\.ps1|\.yml|\.yaml)$/.test(atual.ext || '') ? '# ' : '// ';
  var linha = l.texto.slice(l.ini, l.fim), recuo = /^\s*/.exec(linha)[0], resto = linha.slice(recuo.length);
  var nova = resto.indexOf(prefixo) === 0 ? recuo + resto.slice(prefixo.length) : (resto.indexOf(prefixo.trim()) === 0 ? recuo + resto.slice(prefixo.trim().length) : recuo + prefixo + resto);
  trocarTextoCodigo(l.texto.slice(0, l.ini) + nova + l.texto.slice(l.fim), l.ini + nova.length);
}
function tabQuatro() {
  var s = window.getSelection();
  if (!s.rangeCount || s.isCollapsed || !modoCodigo) { document.execCommand('insertText', false, '    '); return; }
  var t = doc.innerText, r = s.getRangeAt(0), a = r.cloneRange(); a.selectNodeContents(doc); a.setEnd(r.startContainer, r.startOffset);
  var ini = a.toString().length, fim = ini + r.toString().length;
  var li = t.lastIndexOf('\n', ini - 1) + 1, bloco = t.slice(li, fim).split('\n').map(function (x) { return '    ' + x; }).join('\n');
  trocarTextoCodigo(t.slice(0, li) + bloco + t.slice(fim), li + bloco.length);
}
function irParaLinha() {
  pergunta('Ir para linha', 'Número da linha:', '').then(function (v) {
    var n = parseInt(v, 10); if (!n) return;
    if (modoCodigo) {
      var linhas = doc.innerText.split('\n'), pos = 0;
      for (var i = 0; i < Math.min(n - 1, linhas.length - 1); i++) pos += linhas[i].length + 1;
      definirCaretTexto(pos);
    } else {
      var blocos = doc.querySelectorAll('p,h1,h2,h3,li,div,blockquote,pre'); var b = blocos[Math.min(n, blocos.length) - 1];
      if (b) { var r = document.createRange(); r.selectNodeContents(b); r.collapse(true); var s = window.getSelection(); s.removeAllRanges(); s.addRange(r); b.scrollIntoView({ block: 'center' }); }
    }
    doc.focus(); atualizarStatus(); desenharReguas();
  });
}

/* ================================================================ busca */
function localizar() {
  pergunta('Localizar', 'Texto para localizar:', localizar.ultimo || '').then(function (t) {
    if (!t) return; localizar.ultimo = t; doc.focus();
    if (!window.find(t, false, false, true, false, false, false)) aviso('Localizar', 'Nada encontrado.');
  });
}
function substituir() {
  pergunta('Substituir', 'Texto para localizar:', localizar.ultimo || '').then(function (t) {
    if (!t) return;
    pergunta('Substituir', 'Trocar por:', '').then(function (novo) {
      if (novo === null) return;
      var re = new RegExp(t.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'), 'gi'), qtd = 0;
      var w = document.createTreeWalker(doc, NodeFilter.SHOW_TEXT), n, nos = [];
      while ((n = w.nextNode())) nos.push(n);
      nos.forEach(function (x) { var v = x.textContent.replace(re, function () { qtd++; return novo; }); if (v !== x.textContent) x.textContent = v; });
      if (qtd) { marcarSujo(true); if (modoCodigo) realcar(); }
      aviso('Substituir', qtd ? qtd + ' ocorrência(s) trocada(s).' : 'Nada encontrado.');
    });
  });
}

/* ================================================================ exibição */
function definirQuebra(on) {
  document.body.classList.toggle('sem-quebra', !on);
  $('bt-quebra').textContent = on ? 'Sem quebra' : 'Com quebra';
  if (!modoCodigo) cfg.WordWrap = on;
  atualizarStatus(); numeros();
}
function alternarPagina() { salvarConfig({ PageViewMode: !cfg.PageViewMode }); document.body.classList.toggle('pagina', !!cfg.PageViewMode); layout(); }
function alternarRegua() { salvarConfig({ ShowRuler: !cfg.ShowRuler }); document.body.classList.toggle('sem-regua', !cfg.ShowRuler); $('bt-regua').classList.toggle('on', !!cfg.ShowRuler); layout(); atualizarStatus(); }
function alternarLinhas() { salvarConfig({ ShowLines: !cfg.ShowLines }); document.body.classList.toggle('sem-linhas', !cfg.ShowLines); numeros(); }
function alternarBarra() { salvarConfig({ ShowToolbar: !cfg.ShowToolbar }); $('tb-doc').hidden = !cfg.ShowToolbar; layout(); }
function alternarBarraCodigo() { cfg.UIBarraCodigo = $('tb-code').hidden; $('tb-code').hidden = !$('tb-code').hidden; layout(); }
function definirZoom(z) { zoom = Math.max(0.2, Math.min(5, z)); doc.style.zoom = zoom; calha.style.zoom = zoom; numeros(); desenharReguas(); }
function layout() {
  document.body.classList.toggle('pagina', !!cfg.PageViewMode);
  $('bt-pagina').classList.toggle('on', !!cfg.PageViewMode);
  $('bt-pagina').title = cfg.PageViewMode ? 'Modo Página Ativo (816px) — Alternar para Largura Total' : 'Modo Largura Total Ativo — Alternar para Modo Página';
  requestAnimationFrame(function () { numeros(); desenharReguas(); });
}

/* ---------- números de linha ---------- */
function numeros() {
  if (!cfg.ShowLines || $('editor').hidden) { calha.innerHTML = ''; return; }
  var html = '';
  if (modoCodigo) {
    var linhas = doc.innerText.replace(/\n$/, '').split('\n').length;
    var lh = parseFloat(getComputedStyle(doc).lineHeight) || 18, topo = parseFloat(getComputedStyle(doc).paddingTop) || 0;
    for (var i = 0; i < linhas; i++) html += '<div class="n" style="top:' + (topo + i * lh) + 'px;line-height:' + lh + 'px">' + (i + 1) + '</div>';
  } else {
    var blocos = doc.querySelectorAll(':scope > p, :scope > div, :scope > h1, :scope > h2, :scope > h3, :scope > h4, :scope > blockquote, :scope > pre, :scope > ul > li, :scope > ol > li');
    blocos.forEach(function (b, i) { html += '<div class="n" style="top:' + b.offsetTop + 'px;line-height:' + getComputedStyle(b).lineHeight + '">' + (i + 1) + '</div>'; });
  }
  calha.innerHTML = html;
  calha.style.height = doc.scrollHeight + 'px';
}

/* ---------- barra de status ---------- */
function atualizarStatus() {
  var info = (atual.path || 'Sem título') + (sujo ? '  • alterado' : '') + '  · ' + (modoCodigo ? 'Código' : 'Texto') +
    ' · quebra ' + (document.body.classList.contains('sem-quebra') ? 'desligada' : 'ligada') + ' · réguas ' + (cfg.ShowRuler ? 'ligadas' : 'desligadas');
  $('st-info').textContent = info;
  var ln = 1, col = 1;
  if (modoCodigo) { var l = linhaAtualCodigo(); ln = l.linha; col = l.col; }
  else {
    var s = window.getSelection();
    if (s.rangeCount && doc.contains(s.anchorNode)) {
      var b = blocosSelecionados()[0], blocos = Array.prototype.slice.call(doc.querySelectorAll('p,h1,h2,h3,h4,li,div,blockquote,pre'));
      ln = Math.max(1, blocos.indexOf(b) + 1);
      if (b) { var r = s.getRangeAt(0).cloneRange(); r.selectNodeContents(b); r.setEnd(s.anchorNode, s.anchorOffset); col = r.toString().length + 1; }
    }
  }
  $('st-pos').textContent = 'Ln ' + ln + ', Col ' + col;
}

/* ================================================================ réguas (Ruler.cs) */
var reguaH = $('regua-h'), reguaV = $('regua-v'), guia = $('guia');
var subReguas = [], subSel = null, matiz = 205, arrasto = null, digitosApertados = {};
function corDoTema(nome) { return getComputedStyle(document.body).getPropertyValue(nome).trim(); }
function larguraColuna() {
  var s = document.createElement('span'); s.textContent = '0000000000'; s.style.cssText = 'position:absolute;visibility:hidden;white-space:pre';
  doc.appendChild(s); var w = s.getBoundingClientRect().width / 10; s.remove(); return Math.max(7 * zoom, w);
}
function origemX() {     // onde o texto começa, em coordenadas da régua horizontal
  var rd = doc.getBoundingClientRect(), cs = getComputedStyle(doc);
  return rd.left + parseFloat(cs.paddingLeft) * zoom - reguaH.getBoundingClientRect().left;
}
function blocoAtual() { var b = blocosSelecionados(); return b.length ? b[0] : null; }
function recuoAtualPx() { var b = blocoAtual(); return b ? (parseFloat(b.style.marginLeft) || 0) * zoom : 0; }
function caretRect() {
  var s = window.getSelection(); if (!s.rangeCount || !doc.contains(s.anchorNode)) return null;
  var r = s.getRangeAt(0).cloneRange(); r.collapse(true);
  var rr = r.getClientRects()[0]; if (rr) return rr;
  var n = s.anchorNode.nodeType === 1 ? s.anchorNode : s.anchorNode.parentElement; return n.getBoundingClientRect();
}
function hslHex(h, s, l) {
  var a = s * Math.min(l, 1 - l), f = function (n) { var k = (n + h / 30) % 12; var c = l - a * Math.max(Math.min(k - 3, 9 - k, 1), -1); return Math.round(255 * c).toString(16).padStart(2, '0'); };
  return '#' + f(0) + f(8) + f(4);
}
function claro(hex) { var r = parseInt(hex.substr(1, 2), 16), g = parseInt(hex.substr(3, 2), 16), b = parseInt(hex.substr(5, 2), 16); return (0.299 * r + 0.587 * g + 0.114 * b) / 255 > 0.6; }
function desenharReguas() {
  if (!cfg.ShowRuler || $('editor').hidden) return;
  var dpr = window.devicePixelRatio || 1;
  var W = reguaH.clientWidth, H = 26;
  reguaH.width = W * dpr; reguaH.height = H * dpr;
  var c = reguaH.getContext('2d'); c.setTransform(dpr, 0, 0, dpr, 0, 0);
  var sup = corDoTema('--superficie'), papel = corDoTema('--papel'), suave = corDoTema('--suave'), borda = corDoTema('--borda'), acento = corDoTema('--acento');
  c.fillStyle = sup; c.fillRect(0, 0, W, H);
  var rf = folha.getBoundingClientRect(), rr = reguaH.getBoundingClientRect();
  c.fillStyle = papel; c.fillRect(rf.left - rr.left, 0, rf.width, H);
  c.strokeStyle = borda; c.beginPath(); c.moveTo(rf.left - rr.left + .5, 0); c.lineTo(rf.left - rr.left + .5, H); c.moveTo(rf.right - rr.left - .5, 0); c.lineTo(rf.right - rr.left - .5, H); c.stroke();
  var o = origemX(), cw = larguraColuna();
  c.strokeStyle = suave; c.fillStyle = suave; c.lineWidth = 1;
  c.font = Math.min(10, cw * 1.1) + 'px "Adwaita Mono", monospace';
  for (var col = 0, x = o; x < W; col++, x = o + col * cw) {
    if (x < 0) continue;
    var h = col % 10 === 0 ? 10 : col % 5 === 0 ? 6 : 3;
    c.beginPath(); c.moveTo(Math.round(x) + .5, H); c.lineTo(Math.round(x) + .5, H - h); c.stroke();
    if (col % 10 === 0 && col) c.fillText(String(col), x + 2, 10);
  }
  c.beginPath(); c.moveTo(0, H - .5); c.lineTo(W, H - .5); c.stroke();
  // guias pontilhadas nas colunas 80 e 120
  c.setLineDash([2, 2]); c.strokeStyle = corDoTema('--regua-guia');
  [80, 120].forEach(function (cl) { var gx = o + cl * cw; c.beginPath(); c.moveTo(gx + .5, 0); c.lineTo(gx + .5, H); c.stroke(); });
  c.setLineDash([]);
  // marcador do recuo (margem do parágrafo)
  var mx = o + recuoAtualPx();
  c.fillStyle = acento; c.strokeStyle = 'rgba(0,0,0,.2)';
  c.fillRect(mx - 4, 1, 8, 3);
  c.beginPath(); c.moveTo(mx - 5, 4); c.lineTo(mx + 5, 4); c.lineTo(mx, 11); c.closePath(); c.fill(); c.stroke();
  c.setLineDash([1, 2]); c.strokeStyle = acento; c.beginPath(); c.moveTo(mx + .5, 11); c.lineTo(mx + .5, H); c.stroke(); c.setLineDash([]);
  // cursor de texto
  var cr = caretRect();
  if (cr) { var cx = cr.left - rr.left; c.fillStyle = acento; c.beginPath(); c.moveTo(cx - 3, 0); c.lineTo(cx + 3, 0); c.lineTo(cx, 5); c.closePath(); c.fill(); }
  // sub-réguas
  subReguas.forEach(function (s) {
    var sx = o + s.col * cw;
    c.fillStyle = s.cor;
    c.beginPath(); c.moveTo(sx - 6, 0); c.lineTo(sx + 6, 0); c.lineTo(sx + 6, 9); c.lineTo(sx, 15); c.lineTo(sx - 6, 9); c.closePath(); c.fill();
    if (s === subSel) { c.strokeStyle = '#fff'; c.lineWidth = 2; } else { c.strokeStyle = 'rgba(0,0,0,.27)'; c.lineWidth = 1; }
    c.stroke(); c.lineWidth = 1;
    if (s.slot) { c.fillStyle = claro(s.cor) ? '#000' : '#fff'; c.font = 'bold 8px sans-serif'; c.textAlign = 'center'; c.fillText(String(s.slot), sx, 8); c.textAlign = 'left'; }
    c.setLineDash([2, 2]); c.strokeStyle = s.cor; c.beginPath(); c.moveTo(sx + .5, 14); c.lineTo(sx + .5, H); c.stroke(); c.setLineDash([]);
  });
  desenharReguaV(sup, suave, acento, cr);
}
function desenharReguaV(sup, suave, acento, cr) {
  var dpr = window.devicePixelRatio || 1, Hh = reguaV.clientHeight, W = 28;
  reguaV.width = W * dpr; reguaV.height = Hh * dpr;
  var c = reguaV.getContext('2d'); c.setTransform(dpr, 0, 0, dpr, 0, 0);
  c.fillStyle = sup; c.fillRect(0, 0, W, Hh);
  var rv = reguaV.getBoundingClientRect(), rd = doc.getBoundingClientRect(), cs = getComputedStyle(doc);
  var lh = (parseFloat(cs.lineHeight) || 18) * zoom, topo = rd.top + parseFloat(cs.paddingTop) * zoom - rv.top;
  c.strokeStyle = suave; c.fillStyle = suave; c.font = '9px "Adwaita Mono", monospace'; c.textAlign = 'right';
  for (var n = 0, y = topo; y < Hh; n++, y = topo + n * lh) {
    if (y < 0) continue;
    var w = (n + 1) % 10 === 0 ? 12 : (n + 1) % 5 === 0 ? 7 : 4;
    c.beginPath(); c.moveTo(W, Math.round(y) + .5); c.lineTo(W - w, Math.round(y) + .5); c.stroke();
    if ((n + 1) % 5 === 0) c.fillText(String(n + 1), W - 9, y + lh / 2 + 3);
  }
  var rf = folha.getBoundingClientRect();
  c.strokeStyle = corDoTema('--borda'); [rf.top, rf.bottom].forEach(function (yy) { var y = yy - rv.top; c.beginPath(); c.moveTo(0, y + .5); c.lineTo(W, y + .5); c.stroke(); });
  if (cr) { var cy = cr.top - rv.top; c.fillStyle = acento; c.beginPath(); c.moveTo(0, cy + 2); c.lineTo(5, cy + 6); c.lineTo(0, cy + 10); c.closePath(); c.fill(); }
}
function novaSubRegua(col) {
  var s = { col: col, cor: hslHex(matiz % 360, 0.84, 0.56), slot: 0 }; matiz += 137.508;
  subReguas.push(s); subSel = s; atribuirSlotSeTiver(s); return s;
}
function atribuirSlotSeTiver(s) {
  var d = Object.keys(digitosApertados).filter(function (k) { return digitosApertados[k]; })[0];
  if (d) { subReguas.forEach(function (o) { if (o.slot === +d) o.slot = 0; }); s.slot = +d; }
}
function colunaDoX(x) { return Math.max(0, Math.round((x - origemX()) / larguraColuna())); }
function subNoX(x, y) { var o = origemX(), cw = larguraColuna(); if (y > 18) return null; return subReguas.filter(function (s) { return Math.abs(o + s.col * cw - x) <= 8; })[0] || null; }
function mostrarGuia(xRegua, cor) {
  var rr = reguaH.getBoundingClientRect(), rt = trabalho.getBoundingClientRect();
  guia.style.left = (rr.left + xRegua - rt.left) + 'px'; guia.style.background = cor; guia.style.display = 'block';
}
reguaH.addEventListener('mousedown', function (e) {
  if (e.button !== 0) return;
  var x = e.offsetX, y = e.offsetY, mx = origemX() + recuoAtualPx();
  if (Math.abs(x - mx) <= 7 && y <= 20 && !subNoX(x, y)) { arrasto = { tipo: 'recuo', bloco: blocoAtual() }; e.preventDefault(); return; }
  var s = subNoX(x, y);
  if (s) {
    subSel = s;
    if (e.ctrlKey) atribuirSlotSeTiver(s);
    arrasto = { tipo: 'sub', s: s }; atribuirSlotSeTiver(s); desenharReguas(); e.preventDefault(); return;
  }
  var nova = novaSubRegua(colunaDoX(x)); arrasto = { tipo: 'sub', s: nova }; desenharReguas(); e.preventDefault();
});
$('canto').addEventListener('mousedown', function (e) {
  var nova = novaSubRegua(0); arrasto = { tipo: 'sub', s: nova }; e.preventDefault();
});
document.addEventListener('mousemove', function (e) {
  if (!arrasto) return;
  var rr = reguaH.getBoundingClientRect(), x = e.clientX - rr.left;
  if (arrasto.tipo === 'recuo') {
    var px = Math.max(0, (x - origemX()) / zoom);
    if (arrasto.bloco) { arrasto.bloco.style.marginLeft = px ? px + 'px' : ''; marcarSujo(true); }
    mostrarGuia(x, corDoTema('--acento'));
  } else {
    arrasto.s.col = colunaDoX(x); arrasto.dentro = !(e.clientY > rr.bottom + 35 || e.clientY < rr.top - 25);
    mostrarGuia(origemX() + arrasto.s.col * larguraColuna(), arrasto.s.cor);
  }
  desenharReguas();
});
document.addEventListener('mouseup', function () {
  if (!arrasto) return;
  if (arrasto.tipo === 'sub' && arrasto.dentro === false) { subReguas = subReguas.filter(function (s) { return s !== arrasto.s; }); subSel = null; }
  arrasto = null; guia.style.display = 'none'; desenharReguas(); numeros();
});
reguaH.addEventListener('dblclick', function (e) {
  var x = e.offsetX, y = e.offsetY, s = subNoX(x, y);
  if (s) { escolherCor($('cor-regua'), function (v) { s.cor = v; desenharReguas(); }); return; }
  if (Math.abs(x - (origemX() + recuoAtualPx())) <= 7) { var b = blocoAtual(); if (b) { b.style.marginLeft = ''; marcarSujo(true); desenharReguas(); } }
});
reguaH.addEventListener('contextmenu', function (e) {
  e.preventDefault();
  var s = subNoX(e.offsetX, e.offsetY); if (!s) return;
  subSel = s; desenharReguas();
  var slots = []; for (var i = 1; i <= 9; i++) (function (n) { slots.push([(s.slot === n ? '* ' : '') + 'Ctrl+' + n, function () { subReguas.forEach(function (o) { if (o.slot === n) o.slot = 0; }); s.slot = n; desenharReguas(); }]); })(i);
  slots.push(null); slots.push(['Nenhum', function () { s.slot = 0; desenharReguas(); }]);
  menuFlutuante(e.clientX, e.clientY, [
    ['Alterar cor…', function () { escolherCor($('cor-regua'), function (v) { s.cor = v; desenharReguas(); }); }],
    ['Vincular atalho', slots],
    null,
    ['Alinhar parágrafo atual a esta régua (Col ' + s.col + ')', function () { alinharARegua(s); }],
    null,
    ['Excluir régua', function () { subReguas = subReguas.filter(function (o) { return o !== s; }); subSel = null; desenharReguas(); }],
  ]);
});
function alinharARegua(s) {
  if (modoCodigo || document.body.classList.contains('sem-quebra')) {
    if (modoCodigo) {
      var l = linhaAtualCodigo(), linha = l.texto.slice(l.ini, l.fim), nova = ' '.repeat(s.col) + linha.replace(/^\s*/, '');
      trocarTextoCodigo(l.texto.slice(0, l.ini) + nova + l.texto.slice(l.fim), l.ini + s.col);
      return;
    }
  }
  var b = blocoAtual(); if (!b) return;
  b.style.marginLeft = (s.col * larguraColuna() / zoom) + 'px'; marcarSujo(true); desenharReguas();
}

/* ================================================================ menus */
function menuFlutuante(x, y, itens) {
  var m = $('menu-ctx'); m.innerHTML = ''; montarItens(m, itens);
  m.hidden = false; m.style.left = Math.min(x, innerWidth - 260) + 'px'; m.style.top = Math.min(y, innerHeight - m.offsetHeight - 8) + 'px';
}
function montarItens(alvo, itens) {
  itens.forEach(function (it) {
    if (!it) { var sp = document.createElement('div'); sp.className = 'ms'; alvo.appendChild(sp); return; }
    var d = document.createElement('div'); d.className = 'mi';
    var marcado = it[0].indexOf('* ') === 0, rotulo = marcado ? it[0].slice(2) : it[0];
    d.innerHTML = '<span class="marca">' + (marcado ? icone('check', 16) : '') + '</span><span>' + esc(rotulo) + '</span>';
    if (Array.isArray(it[1])) { d.classList.add('sub'); var sub = document.createElement('div'); sub.className = 'menu-flutuante'; montarItens(sub, it[1]); d.appendChild(sub); }
    else d.onclick = function (e) { e.stopPropagation(); fecharMenus(); it[1](); };
    alvo.appendChild(d);
  });
}
function fecharMenus() { $('menu-ctx').hidden = true; document.querySelectorAll('.mb-item.aberto, .dropdown.aberto').forEach(function (m) { m.classList.remove('aberto'); }); }
document.addEventListener('mousedown', function (e) { if (!e.target.closest('.menu-flutuante, .mb-item, .dropdown')) fecharMenus(); });

var MENUS = [
  ['Arquivo', ['Início', 'Novo', 'Abrir', null, 'Salvar', 'Salvar como', null, 'Exportar PDF', 'Imprimir']],
  ['Editar', ['Desfazer', 'Refazer', null, 'Localizar', 'Substituir', 'Ir para linha', null, 'Selecionar tudo']],
  ['Ver', ['Barra de ferramentas', 'Barra de código', 'Modo de Exibição', 'Quebra de linha', 'Números de linha', 'Régua', 'Tema escuro', 'Tela cheia']],
  ['Formatar', ['Negrito', 'Itálico', 'Sublinhado', 'Tachado', null, 'Alinhar à Esquerda', 'Centralizar', 'Alinhar à Direita', 'Justificar', null, 'Limpar Formatação']],
  ['Configurar', ['Atalhos', 'Aplicativo padrão e formatos', 'Aparência']],
  ['Ajuda', ['Sobre o Artry']],
];
var ROTULOS = { 'Abrir': 'Abrir…', 'Salvar como': 'Salvar como…', 'Exportar PDF': 'Exportar PDF…', 'Imprimir': 'Imprimir…', 'Localizar': 'Localizar…',
  'Substituir': 'Substituir…', 'Ir para linha': 'Ir para linha…', 'Modo de Exibição': 'Modo de Exibição (Página/Completo)', 'Atalhos': 'Atalhos…',
  'Aplicativo padrão e formatos': 'Aplicativo padrão e formatos…', 'Aparência': 'Aparência (alternar tema)', 'Sobre o Artry': 'Sobre o Artry…' };
function marcado(c) {
  return { 'Barra de ferramentas': cfg.ShowToolbar !== false, 'Barra de código': !$('tb-code').hidden, 'Modo de Exibição': !!cfg.PageViewMode,
    'Quebra de linha': !document.body.classList.contains('sem-quebra'), 'Números de linha': !!cfg.ShowLines, 'Régua': !!cfg.ShowRuler,
    'Tema escuro': !!cfg.Dark }[c];
}
function montarMenubar() {
  var mb = $('menubar'); mb.innerHTML = '';
  MENUS.forEach(function (m) {
    var item = document.createElement('div'); item.className = 'mb-item';
    var b = document.createElement('button'); b.textContent = m[0]; item.appendChild(b);
    var lista = document.createElement('div'); lista.className = 'mb-lista'; item.appendChild(lista);
    function encher() {
      lista.innerHTML = '';
      m[1].forEach(function (c) {
        if (!c) { var sp = document.createElement('div'); sp.className = 'ms'; lista.appendChild(sp); return; }
        var d = document.createElement('div'); d.className = 'mi';
        var mk = marcado(c), tecla = (cfg.Shortcuts || {})[c] || FIXOS_ROTULO[c] || '';
        d.innerHTML = '<span class="marca">' + (mk ? icone('check', 16) : '') + '</span><span>' + esc(ROTULOS[c] || c) + '</span><span class="tecla">' + esc(tecla) + '</span>';
        d.onclick = function () { fecharMenus(); acao(c); };
        lista.appendChild(d);
      });
    }
    b.onclick = function (e) { e.stopPropagation(); var aberto = item.classList.contains('aberto'); fecharMenus(); if (!aberto) { encher(); item.classList.add('aberto'); } };
    b.onmouseenter = function () { if (document.querySelector('.mb-item.aberto') && !item.classList.contains('aberto')) { fecharMenus(); encher(); item.classList.add('aberto'); } };
    mb.appendChild(item);
  });
}
var FIXOS_ROTULO = { 'Desfazer': 'Ctrl+Z', 'Refazer': 'Ctrl+Y', 'Selecionar tudo': 'Ctrl+A', 'Alinhar à Esquerda': 'Ctrl+Shift+L', 'Centralizar': 'Ctrl+Shift+E',
  'Alinhar à Direita': 'Ctrl+Shift+R', 'Justificar': 'Ctrl+Shift+J', 'Limpar Formatação': 'Ctrl+\\' };

/* ================================================================ ações (menus, barra, atalhos) */
function acao(c) {
  switch (c) {
    case 'Início': mostrarHome(); break;
    case 'Novo': novoDocumento(); break;
    case 'Abrir': abrir(); break;
    case 'Salvar': salvar(); break;
    case 'Salvar como': salvarComo(); break;
    case 'Exportar PDF': rpc('export.pdf', { html: modoCodigo ? textoPuro() : doc.innerHTML, code: modoCodigo, name: atual.name || 'sem título' })
      .then(function (r) { if (r) aviso('Exportar PDF', 'PDF salvo em ' + r.path); }).catch(function (e) { aviso('Exportar PDF', e.message); }); break;
    case 'Imprimir': rpc('print', { html: modoCodigo ? textoPuro() : doc.innerHTML, code: modoCodigo }).catch(function (e) { aviso('Imprimir', e.message); }); break;
    case 'Desfazer': doc.focus(); document.execCommand('undo'); break;
    case 'Refazer': doc.focus(); document.execCommand('redo'); break;
    case 'Localizar': localizar(); break;
    case 'Substituir': substituir(); break;
    case 'Ir para linha': irParaLinha(); break;
    case 'Selecionar tudo': doc.focus(); document.execCommand('selectAll'); break;
    case 'Barra de ferramentas': case 'Gaveta texto': alternarBarra(); break;
    case 'Barra de código': case 'Gaveta código': alternarBarraCodigo(); break;
    case 'Modo de Exibição': alternarPagina(); break;
    case 'Quebra de linha': definirQuebra(document.body.classList.contains('sem-quebra')); if (!modoCodigo) salvarConfig({ WordWrap: !document.body.classList.contains('sem-quebra') }); break;
    case 'Números de linha': alternarLinhas(); break;
    case 'Régua': alternarRegua(); break;
    case 'Tema escuro': case 'Tema': case 'Aparência': alternarTema(); break;
    case 'Tela cheia': send({ type: 'fullscreen' }); break;
    case 'Negrito': exec('bold'); break;
    case 'Itálico': exec('italic'); break;
    case 'Sublinhado': exec('underline'); break;
    case 'Tachado': exec('strikeThrough'); break;
    case 'Cor do texto': escolherCor($('cor-texto'), function (v) { exec('foreColor', v); }); break;
    case 'Cor de destaque': escolherCor($('cor-realce'), function (v) { exec('hiliteColor', v); var b = document.querySelector('[data-cmd="Cor de destaque"] .barra-cor'); if (b) b.setAttribute('fill', v); }); break;
    case 'Alinhar à Esquerda': alinhar('left'); break;
    case 'Centralizar': alinhar('center'); break;
    case 'Alinhar à Direita': alinhar('right'); break;
    case 'Justificar': alinhar('justify'); break;
    case 'Lista com marcadores': exec('insertUnorderedList'); break;
    case 'Lista numerada': exec('insertOrderedList'); break;
    case 'Diminuir recuo': recuo(-24); break;
    case 'Aumentar recuo': recuo(24); break;
    case 'Limpar Formatação': limparFormatacao(); break;
    case 'Aumentar fonte': tamanhoSelecao((parseFloat($('tamanho').value) || 11) + 1); break;
    case 'Diminuir fonte': tamanhoSelecao((parseFloat($('tamanho').value) || 11) - 1); break;
    case 'Ativar Código': definirModoCodigo(true); doc.textContent = doc.innerText; realcar(); break;
    case 'Realçar Sintaxe': if (modoCodigo) realcar(); break;
    case 'Tab 4 espaços': tabQuatro(); break;
    case 'Comentário': comentar(); break;
    case 'Recarregar': if (atual.path) abrirCaminho(atual.path, true); break;
    case 'Fechar barra de código': cfg.UIBarraCodigo = false; $('tb-code').hidden = true; layout(); break;
    case 'Atalhos': dialogoAtalhos(); break;
    case 'Aplicativo padrão e formatos': dialogoFormatos(); break;
    case 'Sobre o Artry': dialogoSobre(); break;
  }
  if ($('menubar').querySelector('.aberto')) fecharMenus();
}
document.querySelectorAll('[data-cmd]').forEach(function (b) {
  b.addEventListener('mousedown', function (e) { if (b.tagName === 'BUTTON') e.preventDefault(); });   // não tira o foco do texto
  b.addEventListener('click', function () { acao(b.getAttribute('data-cmd')); });
});
$('bt-espaco').addEventListener('mousedown', function (e) { e.preventDefault(); });
$('bt-espaco').onclick = function (e) { e.stopPropagation(); var d = $('bt-espaco').parentNode; var ab = d.classList.contains('aberto'); fecharMenus(); if (!ab) d.classList.add('aberto'); };
document.querySelectorAll('#dd-espaco > div').forEach(function (o) { o.addEventListener('mousedown', function (e) { e.preventDefault(); }); o.onclick = function () { espacamento(o.getAttribute('data-lh')); fecharMenus(); }; });
$('zoom').onchange = function () { definirZoom(parseInt($('zoom').value, 10) / 100); };
$('estilos').addEventListener('mousedown', guardarSelecao);
$('estilos').onchange = function () { voltarSelecao(); aplicarEstilo($('estilos').value); };
$('fonte').addEventListener('focus', guardarSelecao);
$('fonte').addEventListener('keydown', function (e) { if (e.key === 'Enter') { voltarSelecao(); fonteSelecao($('fonte').value); } });
$('fonte').addEventListener('change', function () { voltarSelecao(); fonteSelecao($('fonte').value); });
$('tamanho').addEventListener('focus', guardarSelecao);
function aplicarTamanhoCaixa() { var v = parseFloat(String($('tamanho').value).replace(',', '.')); voltarSelecao(); tamanhoSelecao(isNaN(v) ? 11 : v); }
$('tamanho').addEventListener('keydown', function (e) { if (e.key === 'Enter') { e.preventDefault(); aplicarTamanhoCaixa(); } });
$('tamanho').addEventListener('blur', function () { if (faixaGuardada) aplicarTamanhoCaixa(); faixaGuardada = null; });

/* ================================================================ atalhos (ShortcutCatalog) */
var ATALHOS_PADRAO = { 'Novo': 'Ctrl+N', 'Abrir': 'Ctrl+O', 'Salvar': 'Ctrl+S', 'Salvar como': 'Ctrl+Shift+S', 'Imprimir': 'Ctrl+P',
  'Localizar': 'Ctrl+F', 'Substituir': 'Ctrl+H', 'Ir para linha': 'Ctrl+G', 'Quebra de linha': 'Ctrl+W', 'Régua': 'Ctrl+R',
  'Números de linha': 'Ctrl+L', 'Gaveta texto': 'Alt+1', 'Gaveta código': 'Alt+2', 'Tema': 'Ctrl+Alt+T', 'Tela cheia': 'F11',
  'Negrito': 'Ctrl+B', 'Itálico': 'Ctrl+I', 'Sublinhado': 'Ctrl+U' };
function nomeTecla(e) {
  var c = e.code || '';
  if (/^Key[A-Z]$/.test(c)) return c.slice(3);
  if (/^Digit\d$/.test(c)) return c.slice(5);
  if (/^Numpad\d$/.test(c)) return c.slice(6);
  if (/^F\d{1,2}$/.test(c)) return c;
  return { Period: 'Period', Comma: 'Comma', Backslash: 'Backslash', IntlBackslash: 'Backslash', BracketLeft: 'BracketLeft', BracketRight: 'BracketRight',
    Minus: 'Minus', Equal: 'Plus', Slash: 'Slash', IntlRo: 'Slash', Semicolon: 'Semicolon', Quote: 'Quote', Backquote: 'Backquote',
    Space: 'Space', Enter: 'Enter', Tab: 'Tab', Delete: 'Delete', Backspace: 'Backspace', Escape: 'Escape', Home: 'Home', End: 'End',
    PageUp: 'PageUp', PageDown: 'PageDown', Insert: 'Insert' }[c] || null;
}
function acorde(e) {
  var t = nomeTecla(e); if (!t) return null;
  var p = []; if (e.ctrlKey) p.push('Ctrl'); if (e.altKey) p.push('Alt'); if (e.shiftKey) p.push('Shift');
  p.push(t); return p.join('+');
}
document.addEventListener('keydown', function (e) {
  if (/^Digit\d$/.test(e.code)) digitosApertados[e.code.slice(5)] = true;
  if (!$('modal').hidden) { if (e.key === 'Escape') fecharModal(); return; }
  if (e.key === 'Escape') { fecharMenus(); return; }
  if ($('editor').hidden) { var a0 = acorde(e); if (a0 === (cfg.Shortcuts || {})['Abrir']) { e.preventDefault(); abrir(); } else if (a0 === (cfg.Shortcuts || {})['Novo']) { e.preventDefault(); novoDocumento(); } return; }
  var ac = acorde(e);
  // sub-régua selecionada + Delete
  if (e.key === 'Delete' && subSel && !e.ctrlKey) { subReguas = subReguas.filter(function (s) { return s !== subSel; }); subSel = null; desenharReguas(); e.preventDefault(); return; }
  // fixos
  var fixos = { 'Ctrl+Shift+L': 'Alinhar à Esquerda', 'Ctrl+Shift+E': 'Centralizar', 'Ctrl+Shift+R': 'Alinhar à Direita', 'Ctrl+Shift+J': 'Justificar',
    'Ctrl+Shift+Period': 'Aumentar fonte', 'Ctrl+Shift+BracketRight': 'Aumentar fonte', 'Ctrl+Shift+Comma': 'Diminuir fonte', 'Ctrl+Shift+BracketLeft': 'Diminuir fonte',
    'Ctrl+B': 'Negrito', 'Ctrl+I': 'Itálico', 'Ctrl+U': 'Sublinhado', 'Ctrl+Backslash': 'Limpar Formatação', 'Ctrl+Y': 'Refazer' };
  if (ac && fixos[ac]) { e.preventDefault(); acao(fixos[ac]); return; }
  var m = /^Ctrl\+([1-9])$/.exec(ac || '');
  if (m) { var s = subReguas.filter(function (x) { return x.slot === +m[1]; })[0]; if (s) { e.preventDefault(); alinharARegua(s); return; } }
  if (ac) {
    var mapa = cfg.Shortcuts || {};
    for (var nome in mapa) if (mapa[nome] && mapa[nome].toLowerCase() === ac.toLowerCase()) { e.preventDefault(); acao(nome); return; }
  }
  if (e.key === 'Tab' && document.activeElement === doc) { e.preventDefault(); if (modoCodigo) tabQuatro(); else document.execCommand('insertText', false, '\t'); }
});
document.addEventListener('keyup', function (e) { if (/^Digit\d$/.test(e.code)) digitosApertados[e.code.slice(5)] = false; });
window.addEventListener('blur', function () { digitosApertados = {}; });

function dialogoAtalhos() {
  var mapa = Object.assign({}, ATALHOS_PADRAO, cfg.Shortcuts || {});
  var linhas = Object.keys(ATALHOS_PADRAO).map(function (k) {
    return '<tr><td>' + esc(k) + '</td><td><input class="campo atalho" readonly data-acao="' + esc(k) + '" value="' + esc(mapa[k] || '') + '"></td></tr>';
  }).join('');
  abrirModal('<h2>Atalhos — Artry</h2><p>Clique em um campo e aperte até três teclas: Ctrl + Shift + S, por exemplo.</p>' +
    '<table>' + linhas + '</table><p id="at-erro" style="color:#e06050"></p>' +
    '<div class="botoes"><button id="at-pad">Restaurar padrões</button><button id="at-can">Cancelar</button><button class="primario" id="at-ok">Salvar</button></div>', function (d) {
    d.querySelectorAll('.atalho').forEach(function (f) {
      f.onfocus = function () { f.classList.add('gravando'); };
      f.onblur = function () { f.classList.remove('gravando'); };
      f.onkeydown = function (e) {
        e.preventDefault(); e.stopPropagation();
        if (['Control', 'Shift', 'Alt', 'Meta'].indexOf(e.key) >= 0) return;
        var mods = (e.ctrlKey ? 1 : 0) + (e.altKey ? 1 : 0) + (e.shiftKey ? 1 : 0);
        if (mods > 2) { d.querySelector('#at-erro').textContent = 'Use no máximo três teclas simultâneas.'; return; }
        var a = acorde(e); if (a) { f.value = a; d.querySelector('#at-erro').textContent = ''; }
      };
    });
    d.querySelector('#at-pad').onclick = function () { d.querySelectorAll('.atalho').forEach(function (f) { f.value = ATALHOS_PADRAO[f.getAttribute('data-acao')]; }); };
    d.querySelector('#at-can').onclick = fecharModal;
    d.querySelector('#at-ok').onclick = function () {
      var novo = {}, vistos = {}, erro = null;
      d.querySelectorAll('.atalho').forEach(function (f) { var v = f.value.trim(); if (!v) return; if (vistos[v.toLowerCase()]) erro = v; vistos[v.toLowerCase()] = 1; novo[f.getAttribute('data-acao')] = v; });
      if (erro) { d.querySelector('#at-erro').textContent = 'O atalho ' + erro + ' está repetido. Cada comando precisa ter uma combinação própria.'; return; }
      salvarConfig({ Shortcuts: novo }); fecharModal();
    };
  });
}

/* ================================================================ formatos padrão (xdg-mime) */
var GRUPOS_FORMATOS = [
  ['Documentos e Textos', [['.hyp', 'Documento Hyper (.hyp)'], ['.artry', 'Documento do Artry (.artry)'], ['.txt', 'Texto puro (.txt)'], ['.md', 'Documento Markdown (.md)'],
    ['.rtf', 'Texto rico (.rtf)'], ['.docx', 'Documento Word (.docx)'], ['.log', 'Arquivo de log (.log)']]],
  ['Web e Dados Estruturados', [['.json', 'Dados JSON (.json)'], ['.xml', 'Documento XML (.xml)'], ['.csv', 'Planilha CSV (.csv)'], ['.html', 'Página HTML (.html)'],
    ['.htm', 'Página HTML (.htm)'], ['.css', 'Folha de estilos (.css)']]],
  ['Código e Scripts de Programação', [['.cs', 'C# (.cs)'], ['.py', 'Python (.py)'], ['.js', 'JavaScript (.js)'], ['.ts', 'TypeScript (.ts)'], ['.sql', 'SQL (.sql)'],
    ['.ps1', 'PowerShell (.ps1)'], ['.sh', 'Shell script (.sh)'], ['.yml', 'YAML (.yml)'], ['.yaml', 'YAML (.yaml)']]],
];
function dialogoFormatos() {
  rpc('assoc.status').then(function (st) {
    var marcados = st.active.length ? st.active : (st.saved || []);
    var html = '<h2>Aplicativo Padrão e Formatos — Artry</h2><p>Escolha os tipos de arquivo que o sistema abre com o Artry (duplo clique no gerenciador de arquivos).</p>' +
      '<div class="botoes" style="justify-content:flex-start;margin:0"><button id="fm-comuns">Apenas textos comuns (.hyp, .artry, .txt, .md, .rtf, .docx, .log)</button>' +
      '<button id="fm-todos">Selecionar todos</button><button id="fm-nenhum">Desmarcar todos</button></div>';
    GRUPOS_FORMATOS.forEach(function (g) {
      html += '<div class="grupo">' + esc(g[0]) + '</div>' + g[1].map(function (f) {
        return '<label class="chk"><input type="checkbox" value="' + f[0] + '"' + (marcados.indexOf(f[0]) >= 0 ? ' checked' : '') + '> ' + esc(f[1]) + '</label>';
      }).join('');
    });
    html += '<div class="status-assoc ' + (st.active.length ? 'ok' : '') + '">' + (st.active.length ? 'Status: o Artry é o padrão para ' + st.active.length + ' formato(s).' : 'Status: o Artry ainda não é o padrão para nenhum formato.') + '</div>' +
      '<div class="botoes"><button id="fm-fechar">Fechar</button><button id="fm-remover">Remover registro</button><button class="primario" id="fm-ok">Definir como padrão</button></div>';
    abrirModal(html, function (d) {
      var cks = d.querySelectorAll('input[type=checkbox]');
      d.querySelector('#fm-comuns').onclick = function () { cks.forEach(function (c) { c.checked = ['.hyp', '.artry', '.txt', '.md', '.rtf', '.docx', '.log'].indexOf(c.value) >= 0; }); };
      d.querySelector('#fm-todos').onclick = function () { cks.forEach(function (c) { c.checked = true; }); };
      d.querySelector('#fm-nenhum').onclick = function () { cks.forEach(function (c) { c.checked = false; }); };
      d.querySelector('#fm-fechar').onclick = fecharModal;
      d.querySelector('#fm-remover').onclick = function () { rpc('assoc.remove').then(function () { fecharModal(); aviso('Formatos', 'O Artry deixou de ser o padrão dos formatos.'); }); };
      d.querySelector('#fm-ok').onclick = function () {
        var exts = Array.prototype.filter.call(cks, function (c) { return c.checked; }).map(function (c) { return c.value; });
        if (!exts.length) { escolha('Formatos', 'Nenhum formato marcado. Remover o Artry como padrão de todos?', [{ id: 'n', rotulo: 'Não' }, { id: 's', rotulo: 'Sim', primario: true }]).then(function (r) { if (r === 's') rpc('assoc.remove'); }); return; }
        rpc('assoc.apply', { exts: exts }).then(function () { fecharModal(); aviso('Formatos', 'Pronto: o Artry abre ' + exts.length + ' formato(s).'); });
      };
    });
  });
}
function dialogoSobre() {
  abrirModal('<div class="sobre"><img src="icon.png" alt=""><h2 style="font-size:20px;margin:10px 0 4px">Artry</h2><p>Versão ' + esc(info.version || '') +
    '<br>Editor de texto minimalista, rápido e focado.<br>Suporte a documentos ricos, código e régua coordenada.<br>Arch Quilly</p>' +
    '<div class="botoes" style="justify-content:center"><button class="primario" id="sb-ok" style="width:90px">OK</button></div></div>', function (d) {
    d.querySelector('#sb-ok').onclick = fecharModal;
  });
}

/* ================================================================ eventos do editor */
doc.addEventListener('input', function () { if (!sujo) marcarSujo(true); else atualizarStatus(); numeros(); desenharReguas(); });
document.addEventListener('selectionchange', function () {
  if ($('editor').hidden) return;
  estadoBarra(); atualizarStatus(); desenharReguas();
});
folha.addEventListener('scroll', function () { desenharReguas(); });
window.addEventListener('resize', function () { layout(); });
doc.addEventListener('paste', function (e) {
  if (modoCodigo) return;
  var itens = (e.clipboardData && e.clipboardData.items) || [];
  for (var i = 0; i < itens.length; i++) {
    if (itens[i].type.indexOf('image/') === 0) {
      e.preventDefault();
      var leitor = new FileReader();
      leitor.onload = function () { exec('insertImage', leitor.result); };
      leitor.readAsDataURL(itens[i].getAsFile()); return;
    }
  }
});
document.addEventListener('dragover', function (e) { e.preventDefault(); });
document.addEventListener('drop', function (e) {
  var uris = (e.dataTransfer && e.dataTransfer.getData('text/uri-list')) || '';
  var arq = uris.split(/\r?\n/).filter(function (u) { return u.indexOf('file://') === 0; })[0];
  if (arq) { e.preventDefault(); abrirCaminho(decodeURIComponent(arq.replace('file://', '')), true); return; }
  if (e.dataTransfer && e.dataTransfer.files && e.dataTransfer.files.length && !e.target.closest('#doc')) e.preventDefault();
});

/* ganchos pra testes automáticos (não mudam nada no uso normal) */
window.__artry = { paraMarkdown: paraMarkdown, salvarEm: salvarEm, abrirCaminho: abrirCaminho, textoPuro: textoPuro,
  html: function () { return doc.innerHTML; }, modoCodigo: function () { return modoCodigo; } };

/* ================================================================ início */
Promise.all([rpc('app.info'), rpc('settings.get')]).then(function (r) {
  info = r[0]; cfg = r[1];
  cfg.Shortcuts = Object.assign({}, ATALHOS_PADRAO, cfg.Shortcuts || {});
  aplicarTema();
  document.documentElement.style.setProperty('--fonte-doc', '"' + (cfg.FontName || 'Adwaita Sans') + '"');
  document.documentElement.style.setProperty('--tam-doc', (cfg.FontSize || 11) + 'pt');
  document.documentElement.style.setProperty('--fonte-codigo', '"' + (cfg.CodeFontName || 'Adwaita Mono') + '"');
  document.body.classList.toggle('pagina', !!cfg.PageViewMode);
  document.body.classList.toggle('sem-regua', !cfg.ShowRuler);
  document.body.classList.toggle('sem-linhas', !cfg.ShowLines);
  $('bt-regua').classList.toggle('on', !!cfg.ShowRuler);
  $('tb-doc').hidden = cfg.ShowToolbar === false;
  $('tamanho').value = cfg.FontSize || 11;
  montarMenubar();
  if (info.initialFile) abrirCaminho(info.initialFile, false); else mostrarHome();
  rpc('fonts.list').then(function (fs) { $('fontes').innerHTML = fs.map(function (f) { return '<option value="' + esc(f) + '">'; }).join(''); });
});
})();
