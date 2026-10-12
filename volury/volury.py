#!/usr/bin/env python3
"""
Volury (Arch Quilly) — editor de e-books EPUB 2/3. Port do Volury de Windows (C# + WebView2).

A interface (ui/index.html, app.js, style.css) é a original (emojis trocados por ícones). Este arquivo
é o "lado C#" reescrito em Python: o livro em memória (EpubManager), abrir/salvar EPUB, importar .hyp,
sumário, validação e a ponte com a página ({action, payload} <-> {action, payload}).

Correções em relação ao original:
  - reordenar capítulos e mudar metadados agora atualizam a barra lateral (update-ui);
  - arquivos do EPUB sem BOM; identificador sem "urn:uuid:" duplicado;
  - "properties" do manifesto (capa etc.) preservadas ao reabrir e salvar;
  - ids de capítulo não colidem com os de um EPUB aberto;
  - salvar pelo editor visual não renomeia o capítulo pra "Chapter".
"""
import html
import os
import re
import sys
import uuid
import xml.etree.ElementTree as ET
import zipfile
from datetime import datetime, timezone

AQUI = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, AQUI)
import archhost  # noqa: E402
from gi.repository import GLib  # noqa: E402

NS_OPF = "http://www.idpf.org/2007/opf"
NS_DC = "http://purl.org/dc/elements/1.1/"
NS_CONT = "urn:oasis:names:tc:opendocument:xmlns:container"

CSS_PADRAO = """/* Volury — Estilo padrão do e-book */
body { font-family: Georgia, "Times New Roman", serif; font-size: 1em; line-height: 1.6; margin: 2em; color: #333; }
h1, h2, h3 { font-family: Georgia, "Times New Roman", serif; margin-top: 1.5em; margin-bottom: 0.5em; }
h1 { font-size: 2em; text-align: center; margin-top: 3em; }
h2 { font-size: 1.5em; }
h3 { font-size: 1.2em; }
p { text-indent: 1.5em; margin: 0.3em 0; }
p:first-of-type { text-indent: 0; }
blockquote { margin: 1em 2em; font-style: italic; border-left: 3px solid #ccc; padding-left: 1em; }
"""


def esc(s):
    return html.escape(s or "", quote=True)


def template_capitulo(titulo, corpo=None):
    corpo = corpo if corpo is not None else f"  <h1>{esc(titulo)}</h1>\n  <p></p>"
    return f"""<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE html>
<html xmlns="http://www.w3.org/1999/xhtml">
<head>
  <title>{esc(titulo)}</title>
  <link rel="stylesheet" type="text/css" href="../Styles/style.css"/>
</head>
<body>
{corpo}
</body>
</html>"""


def _texto(el):
    return "".join(el.itertext()).strip() if el is not None else ""


def parse_xhtml(xhtml):
    """XML -> raiz (ou None). Tira o BOM e entidades HTML comuns que o XML não conhece."""
    s = (xhtml or "").lstrip("﻿")
    s = s.replace("&nbsp;", "&#160;")
    try:
        return ET.fromstring(s.encode("utf-8"))
    except ET.ParseError:
        return None


def achar(raiz, nome):
    for el in raiz.iter():
        if el.tag.split("}")[-1] == nome and _texto(el):
            return el
    return None


def titulo_xhtml(xhtml, ordem=("title", "h1", "h2")):
    raiz = parse_xhtml(xhtml)
    if raiz is None:
        return None
    for nome in ordem:
        el = achar(raiz, nome)
        if el is not None:
            return _texto(el)
    return None


class Item:
    def __init__(self, id_, nome, mime, tipo, conteudo="", binario=None, titulo="", props=""):
        self.Id, self.NomeArquivo, self.MediaType, self.Tipo = id_, nome, mime, tipo
        self.Conteudo, self.DadosBinarios, self.Titulo, self.Properties = conteudo, binario, titulo, props


class EpubManager:
    def __init__(self):
        self.titulo, self.autor, self.idioma, self.descricao = "Sem Título", "Autor", "pt", ""
        self.identificador = str(uuid.uuid4())
        self.itens = []
        self.spine = []
        self.caminho = None

    # ---- criar / capítulos / recursos ----
    def criar_novo(self, titulo="Sem Título", autor="Autor"):
        self.__init__()
        self.titulo, self.autor = titulo, autor
        self.itens.append(Item("style", "OEBPS/Styles/style.css", "text/css", "css", CSS_PADRAO))

    def item(self, id_):
        return next((i for i in self.itens if i.Id == id_), None)

    def novo_id(self, prefixo):
        n = 1
        while self.item(f"{prefixo}{n}") or any(i.NomeArquivo.endswith(f"/{prefixo}{n}.xhtml") for i in self.itens):
            n += 1
        return f"{prefixo}{n}"

    def adicionar_capitulo(self, titulo, xhtml=None):
        cid = self.novo_id("chapter_")
        self.itens.append(Item(cid, f"OEBPS/Text/{cid}.xhtml", "application/xhtml+xml", "chapter",
                               xhtml if xhtml is not None else template_capitulo(titulo), titulo=titulo))
        self.spine.append(cid)
        return cid

    def remover_capitulo(self, id_):
        self.itens = [i for i in self.itens if i.Id != id_]
        self.spine = [s for s in self.spine if s != id_]

    def capitulos(self):
        return [i for i in (self.item(s) for s in self.spine) if i and i.Tipo == "chapter"]

    def _id_recurso(self, prefixo, arquivo):
        base = prefixo + re.sub(r"\s+", "_", os.path.splitext(os.path.basename(arquivo))[0])
        id_, n = base, 2
        while self.item(id_):
            id_, n = f"{base}_{n}", n + 1
        return id_

    def adicionar_imagem(self, arquivo):
        ext = os.path.splitext(arquivo)[1].lower()
        mime = {".png": "image/png", ".jpg": "image/jpeg", ".jpeg": "image/jpeg", ".gif": "image/gif",
                ".svg": "image/svg+xml", ".webp": "image/webp"}.get(ext, "application/octet-stream")
        self.itens.append(Item(self._id_recurso("img_", arquivo), f"OEBPS/Images/{os.path.basename(arquivo)}",
                               mime, "image", binario=open(arquivo, "rb").read()))

    def adicionar_css(self, arquivo):
        self.itens.append(Item(self._id_recurso("css_", arquivo), f"OEBPS/Styles/{os.path.basename(arquivo)}",
                               "text/css", "css", open(arquivo, encoding="utf-8-sig").read()))

    # ---- abrir ----
    def abrir(self, caminho):
        self.__init__()
        with zipfile.ZipFile(caminho) as z:
            nomes = set(z.namelist())
            if "META-INF/container.xml" not in nomes:
                raise ValueError("EPUB inválido: META-INF/container.xml não encontrado.")
            cont = ET.fromstring(z.read("META-INF/container.xml"))
            rf = cont.find(f".//{{{NS_CONT}}}rootfile")
            opf_path = rf.get("full-path") if rf is not None else "OEBPS/content.opf"
            if opf_path not in nomes:
                raise ValueError(f"EPUB inválido: {opf_path} não encontrado.")
            opf = ET.fromstring(z.read(opf_path))
            opf_dir = os.path.dirname(opf_path) + "/" if "/" in opf_path else ""
            meta = opf.find(f"{{{NS_OPF}}}metadata")

            def dc(nome, padrao):
                el = meta.find(f"{{{NS_DC}}}{nome}") if meta is not None else None
                return (el.text or "").strip() if el is not None and (el.text or "").strip() else padrao
            self.titulo = dc("title", "Sem Título")
            self.autor = dc("creator", "Autor")
            self.idioma = dc("language", "pt")
            self.descricao = dc("description", "")
            ident = dc("identifier", str(uuid.uuid4()))
            self.identificador = ident[len("urn:uuid:"):] if ident.lower().startswith("urn:uuid:") else ident
            for it in opf.iter(f"{{{NS_OPF}}}item"):
                id_, href, mime = it.get("id"), it.get("href") or "", it.get("media-type") or ""
                props = it.get("properties") or ""
                cheio = os.path.normpath(opf_dir + href).replace("\\", "/")
                if "nav" in props.split():
                    tipo = "nav"
                elif mime == "application/xhtml+xml":
                    tipo = "chapter"
                elif mime == "text/css":
                    tipo = "css"
                elif mime == "application/x-dtbncx+xml":
                    tipo = "ncx"
                elif mime.startswith("image/"):
                    tipo = "image"
                elif mime.startswith("font/") or mime == "application/font-woff":
                    tipo = "font"
                else:
                    tipo = "other"
                dados = z.read(cheio) if cheio in nomes else b""
                item = Item(id_, cheio, mime, tipo, props=props)
                if tipo in ("chapter", "css", "nav", "ncx"):
                    item.Conteudo = dados.decode("utf-8-sig", "replace")
                    if tipo == "chapter":
                        item.Titulo = titulo_xhtml(item.Conteudo) or os.path.splitext(os.path.basename(href))[0]
                else:
                    item.DadosBinarios = dados
                self.itens.append(item)
            self.spine = [r.get("idref") for r in opf.iter(f"{{{NS_OPF}}}itemref")
                          if self.item(r.get("idref")) and self.item(r.get("idref")).Tipo != "nav"]
        if not self.item("style") and not any(i.Tipo == "css" for i in self.itens):
            self.itens.append(Item("style", "OEBPS/Styles/style.css", "text/css", "css", CSS_PADRAO))
        self.caminho = caminho

    # ---- salvar (EPUB 3 + NCX pra leitores EPUB 2) ----
    def salvar(self, caminho):
        def href(item):
            return item.NomeArquivo[len("OEBPS/"):] if item.NomeArquivo.startswith("OEBPS/") else item.NomeArquivo
        conteudo = [i for i in self.itens if i.Tipo not in ("nav", "ncx")]
        caps = [self.item(s) for s in self.spine if self.item(s)]
        agora = datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")
        manifest = "\n".join(
            f'    <item id="{esc(i.Id)}" href="{esc(href(i))}" media-type="{esc(i.MediaType)}"'
            + (f' properties="{esc(i.Properties)}"' if i.Properties and "nav" not in i.Properties.split() else "") + "/>"
            for i in conteudo)
        spine = "\n".join(f'    <itemref idref="{esc(s)}"/>' for s in self.spine if self.item(s))
        opf = f"""<?xml version="1.0" encoding="UTF-8"?>
<package xmlns="http://www.idpf.org/2007/opf" unique-identifier="bookid" version="3.0">
  <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
    <dc:identifier id="bookid">urn:uuid:{esc(self.identificador)}</dc:identifier>
    <dc:title>{esc(self.titulo)}</dc:title>
    <dc:creator>{esc(self.autor)}</dc:creator>
    <dc:language>{esc(self.idioma)}</dc:language>
    <dc:description>{esc(self.descricao)}</dc:description>
    <meta property="dcterms:modified">{agora}</meta>
  </metadata>
  <manifest>
    <item id="ncx" href="toc.ncx" media-type="application/x-dtbncx+xml"/>
    <item id="nav" href="nav.xhtml" media-type="application/xhtml+xml" properties="nav"/>
{manifest}
  </manifest>
  <spine toc="ncx">
{spine}
  </spine>
</package>"""
        pontos = "\n".join(f"""    <navPoint id="navPoint-{n}" playOrder="{n}">
      <navLabel><text>{esc(c.Titulo or c.Id)}</text></navLabel>
      <content src="{esc(href(c))}"/>
    </navPoint>""" for n, c in enumerate(caps, 1))
        ncx = f"""<?xml version="1.0" encoding="UTF-8"?>
<ncx xmlns="http://www.daisy.org/z3986/2005/ncx/" version="2005-1">
  <head>
    <meta name="dtb:uid" content="urn:uuid:{esc(self.identificador)}"/>
  </head>
  <docTitle><text>{esc(self.titulo)}</text></docTitle>
  <navMap>
{pontos}
  </navMap>
</ncx>"""
        lis = "\n".join(f'      <li><a href="{esc(href(c))}">{esc(c.Titulo or c.Id)}</a></li>' for c in caps)
        nav = f"""<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE html>
<html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops">
<head><title>Sumário</title></head>
<body>
  <nav epub:type="toc" id="toc">
    <h1>Sumário</h1>
    <ol>
{lis}
    </ol>
  </nav>
</body>
</html>"""
        container = """<?xml version="1.0" encoding="UTF-8"?>
<container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
  <rootfiles>
    <rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/>
  </rootfiles>
</container>"""
        tmp = caminho + ".tmp"
        with zipfile.ZipFile(tmp, "w") as z:
            z.writestr(zipfile.ZipInfo("mimetype"), "application/epub+zip", compress_type=zipfile.ZIP_STORED)
            ZD = zipfile.ZIP_DEFLATED
            z.writestr("META-INF/container.xml", container, compress_type=ZD)
            z.writestr("OEBPS/content.opf", opf, compress_type=ZD)
            z.writestr("OEBPS/toc.ncx", ncx, compress_type=ZD)
            z.writestr("OEBPS/nav.xhtml", nav, compress_type=ZD)
            ja = set()
            for i in conteudo:
                nome = i.NomeArquivo if i.NomeArquivo.startswith("OEBPS/") else "OEBPS/" + i.NomeArquivo
                if nome in ja:
                    continue
                ja.add(nome)
                z.writestr(nome, i.DadosBinarios if i.DadosBinarios is not None else (i.Conteudo or "").encode("utf-8"),
                           compress_type=ZD)
        os.replace(tmp, caminho)
        self.caminho = caminho

    # ---- sumário e validação ----
    def gerar_toc(self):
        for c in self.capitulos():
            t = titulo_xhtml(c.Conteudo, ("h1", "h2", "h3"))
            if t:
                c.Titulo = t

    def validar(self):
        r = []
        caps = self.capitulos()
        if not caps:
            r.append(("warning", "O livro não contém nenhum capítulo."))
        for c in caps:
            if not (c.Conteudo or "").strip():
                r.append(("warning", f"O capítulo '{c.Titulo}' ({c.Id}) está vazio."))
                continue
            try:
                ET.fromstring((c.Conteudo or "").lstrip("﻿").replace("&nbsp;", "&#160;").encode("utf-8"))
            except ET.ParseError as e:
                r.append(("error", f"XHTML inválido no capítulo '{c.Titulo}' ({c.Id}): {e}"))
        for s in self.spine:
            if not self.item(s):
                r.append(("error", f"Item do spine '{s}' não encontrado no manifest."))
        if not self.titulo.strip():
            r.append(("error", "Metadado obrigatório 'título' está vazio."))
        if not self.idioma.strip():
            r.append(("warning", "Metadado 'idioma' não definido. Usando 'pt' como padrão."))
        if not self.identificador.strip():
            r.append(("error", "Metadado obrigatório 'identificador' (UUID) está vazio."))
        imagens = {os.path.basename(i.NomeArquivo).lower() for i in self.itens if i.Tipo == "image"}
        for c in caps:
            for src in re.findall(r'src\s*=\s*"([^"]+)"', c.Conteudo or "", re.I):
                f = os.path.basename(src)
                if f.lower().endswith((".png", ".jpg", ".jpeg", ".gif", ".svg", ".webp")) and f.lower() not in imagens:
                    r.append(("warning", f"Imagem '{f}' referenciada no capítulo '{c.Titulo}' não está no EPUB."))
        if not r:
            r.append(("info", "EPUB válido! Nenhum problema encontrado."))
        return [{"Severity": s, "Message": m} for s, m in r]


# ---------- .hyp -> capítulo ----------
def strip_rtf(t):
    out, prof, i = [], 0, 0
    while i < len(t):
        c = t[i]
        if c == "{":
            prof += 1
        elif c == "}":
            prof -= 1
        elif c == "\\":
            i += 1
            if i < len(t) and t[i] == "'":
                i += 3
                continue
            while i < len(t) and t[i].isalpha():
                i += 1
            while i < len(t) and (t[i].isdigit() or t[i] == "-"):
                i += 1
            if i < len(t) and t[i] == " ":
                i += 1
            continue
        elif prof <= 1:
            out.append(c)
        i += 1
    return "".join(out)


ANEXO_MARCA = "<!-- ===== ANEXOS HYP ===== -->"


def ler_hyp(texto):
    """.hyp v2 -> (corpo markdown, {id: (mime, bytes)}). Front-matter e bloco de anexos saem do corpo."""
    import base64
    anexos = {}
    corpo, _, rodape = texto.partition(ANEXO_MARCA)
    for m in re.finditer(r"```hyp-anexo\s+(\S+)\s+(\S+)\s*\n(.*?)\n```", rodape, re.S):
        try:
            anexos[m.group(1).lower()] = (m.group(2), base64.b64decode(re.sub(r"\s+", "", m.group(3))))
        except ValueError:
            pass
    fm = re.match(r"\s*---\r?\n.*?\r?\n---\r?\n", corpo, re.S)
    if fm:
        corpo = corpo[fm.end():]
    return corpo, anexos


def hyp_para_xhtml(caminho):
    """.hyp (ou .md/.txt/.html/.rtf) -> (XHTML de um capítulo, [(nome, mime, bytes)] imagens anexadas).
    Markdown de verdade (listas, citações, links, código); imagens "anexo:id" viram ../Images/id."""
    import markdown
    titulo = os.path.splitext(os.path.basename(caminho))[0]
    texto = open(caminho, encoding="utf-8-sig", errors="replace").read()
    corpo, anexos = ler_hyp(texto)
    ini = corpo.lstrip()
    if re.match(r"<!doctype|<html", ini, re.I):
        b = re.search(r"<body[^>]*>([\s\S]*)</body>", corpo, re.I)
        return template_capitulo(titulo, (b.group(1) if b else corpo).strip()), []
    if ini.startswith("{\\rtf"):
        corpo = strip_rtf(corpo)
    usados = []

    def anexo(m):
        alt, id_ = m.group(1), m.group(2).lower()
        if id_ in anexos and anexos[id_][0].startswith("image/"):
            usados.append(id_)
            return f"![{alt}](../Images/{id_})"
        return f"[{alt}]"
    corpo = re.sub(r"!\[([^\]]*)\]\(anexo:([\w.\-]+)\)", anexo, corpo, flags=re.I)
    corpo = re.sub(r"\[([^\]]*)\]\(anexo:[^)]*\)", r"\1", corpo, flags=re.I)
    if caminho.lower().endswith(".txt"):
        html_ = "\n".join(f"<p>{esc(l)}</p>" for l in corpo.splitlines() if l.strip())
    else:
        html_ = markdown.markdown(corpo, extensions=["extra", "sane_lists"], output_format="xhtml")
    # XHTML: entidades HTML que o XML não conhece
    html_ = html_.replace("&nbsp;", "&#160;")
    if not re.match(r"\s*<h1", html_):
        html_ = f"<h1>{esc(titulo)}</h1>\n" + html_
    imagens = [(id_, anexos[id_][0], anexos[id_][1]) for id_ in dict.fromkeys(usados)]
    return template_capitulo(titulo, html_), imagens


def importar_hyp(livro, caminho):
    """Cria um capítulo com o .hyp e põe as imagens anexadas no livro."""
    xhtml, imagens = hyp_para_xhtml(caminho)
    for nome, mime, dados in imagens:
        if not any(i.NomeArquivo == f"OEBPS/Images/{nome}" for i in livro.itens):
            livro.itens.append(Item(livro._id_recurso("img_", nome), f"OEBPS/Images/{nome}", mime, "image",
                                    binario=dados))
    return livro.adicionar_capitulo(os.path.splitext(os.path.basename(caminho))[0], xhtml)


# ---------- janela ----------
class Janela(archhost.Host):
    NOME = "volury"
    PASTA_UI = os.path.join(AQUI, "ui")
    TITULO = "Volury"
    TAMANHO = (1200, 780)
    SEM_BORDA = False
    FUNDO = "#14171A"

    def __init__(self, app):
        super().__init__(app)
        archhost.Host._dinamicos[self.NOME] = self.servir_do_livro
        self.set_size_request(960, 620)
        self.livro = None
        self.arquivo_inicial = None

    def servir_do_livro(self, caminho):
        """Imagens/CSS do livro aberto aparecem no editor: "../Images/x.png" vira app://volury/Images/x.png."""
        if not self.livro or not caminho.startswith(("Images/", "Styles/", "Fonts/")):
            return None
        alvo = "OEBPS/" + caminho
        it = next((i for i in self.livro.itens if i.NomeArquivo == alvo or i.NomeArquivo.endswith("/" + caminho)), None)
        if not it:
            return None
        return (it.DadosBinarios if it.DadosBinarios is not None else (it.Conteudo or "").encode()), it.MediaType

    def abrir_arquivo_externo(self, caminho):
        if self.carregado:
            self.abrir_arquivo(caminho)
        else:
            self.arquivo_inicial = caminho

    def ao_carregar(self):
        if self.arquivo_inicial:
            GLib.timeout_add(300, lambda: (self.abrir_arquivo(self.arquivo_inicial), False)[1])

    def responder(self, action, payload):
        self.enviar({"action": action, "payload": payload})

    def ao_erro(self, msg, erro):
        self.responder("error", {"message": str(erro)})

    def atualizar_ui(self):
        if not self.livro:
            return
        l = self.livro
        self.responder("update-ui", {
            "chapters": [{"Id": c.Id, "Titulo": c.Titulo} for c in l.capitulos()],
            "resources": [{"Id": i.Id, "Tipo": i.Tipo, "NomeArquivo": i.NomeArquivo}
                          for i in l.itens if i.Tipo in ("image", "css")],
            "metadata": {"title": l.titulo, "author": l.autor}})

    def novo(self):
        self.livro = EpubManager()
        self.livro.criar_novo()
        self.set_title("Volury — Sem Título")
        self.atualizar_ui()

    def abrir_arquivo(self, caminho):
        try:
            if caminho.lower().endswith(".hyp"):
                nome = os.path.splitext(os.path.basename(caminho))[0]
                self.livro = EpubManager()
                self.livro.criar_novo(nome, "Autor")
                importar_hyp(self.livro, caminho)
            else:
                livro = EpubManager()
                livro.abrir(caminho)
                self.livro = livro
            self.set_title(f"Volury — {os.path.basename(caminho)}")
            self.atualizar_ui()
        except Exception as e:  # noqa: BLE001
            self.responder("error", {"message": str(e)})

    def salvar(self, caminho):
        try:
            self.livro.salvar(caminho)
            self.set_title(f"Volury — {os.path.basename(caminho)}")
            self.responder("saved", {"path": caminho})
        except Exception as e:  # noqa: BLE001
            self.responder("error", {"message": f"Não consegui salvar: {e}"})

    def salvar_como_dialogo(self):
        if not self.livro:
            return
        nome = re.sub(r'[\\/:*?"<>|]', "_", self.livro.titulo or "livro") + ".epub"
        self.salvar_como("Salvar EPUB como", nome, [("EPUB", ["*.epub"])],
                         lambda c: c and self.salvar(c if c.lower().endswith(".epub") else c + ".epub"))

    def ao_receber(self, msg):
        import json
        if isinstance(msg, str):
            msg = json.loads(msg)
        acao, p = msg.get("action"), msg.get("payload") or {}
        l = self.livro
        if acao == "new-epub":
            self.novo()
        elif acao == "open-epub":
            self.escolher_arquivo("Abrir EPUB", [("EPUB", ["*.epub"]), ("Documento Hyper", ["*.hyp"]),
                                                 ("Todos os arquivos", ["*"])],
                                  lambda cs: cs and self.abrir_arquivo(cs[0]))
        elif acao == "save-epub":
            if l and l.caminho and l.caminho.lower().endswith(".epub"):
                self.salvar(l.caminho)
            else:
                self.salvar_como_dialogo()
        elif acao in ("save-epub-as", "export-epub"):
            self.salvar_como_dialogo()
        elif acao == "import-hyp":
            def importar(cs):
                if not cs:
                    return
                if not self.livro:
                    self.novo()
                importar_hyp(self.livro, cs[0])
                self.atualizar_ui()
            self.escolher_arquivo("Importar .hyp", [("Documento Hyper", ["*.hyp"]),
                                                     ("Texto e Markdown", ["*.md", "*.txt"])], importar)
        elif acao == "add-chapter":
            if not l:
                self.novo()
            self.livro.adicionar_capitulo(p.get("title") or "Novo Capítulo")
            self.atualizar_ui()
        elif acao == "delete-chapter" and l:
            l.remover_capitulo(p.get("id"))
            self.atualizar_ui()
        elif acao == "get-chapter-content" and l:
            it = l.item(p.get("id"))
            self.responder("chapter-content", {"id": p.get("id"), "content": it.Conteudo if it else ""})
        elif acao == "save-chapter-content" and l:
            it = l.item(p.get("id"))
            if it:
                novo = p.get("content") or ""
                t = titulo_xhtml(novo)
                if t == "Chapter":            # o editor visual embrulha com <title>Chapter</title>
                    novo = novo.replace("<title>Chapter</title>", f"<title>{esc(it.Titulo)}</title>", 1)
                    t = titulo_xhtml(novo, ("h1", "h2")) or it.Titulo
                it.Conteudo = novo
                if t:
                    it.Titulo = t
        elif acao == "reorder-chapters" and l:
            ordem = [i for i in (p.get("order") or []) if l.item(i)]
            resto = [s for s in l.spine if s not in ordem]
            l.spine = ordem + resto
            self.atualizar_ui()
        elif acao == "update-metadata" and l:
            for chave, attr in (("title", "titulo"), ("author", "autor"), ("language", "idioma"),
                                ("description", "descricao")):
                if p.get(chave) is not None:
                    setattr(l, attr, p[chave])
            self.set_title(f"Volury — {l.titulo}")
            self.atualizar_ui()
        elif acao == "get-metadata" and l:
            self.responder("metadata", {"title": l.titulo, "author": l.autor, "language": l.idioma,
                                        "description": l.descricao})
        elif acao == "add-image" and l:
            def img(cs):
                for c in cs:
                    l.adicionar_imagem(c)
                if cs:
                    self.atualizar_ui()
            self.escolher_arquivo("Adicionar imagem", [("Imagens", ["*.png", "*.jpg", "*.jpeg", "*.gif", "*.svg", "*.webp"])],
                                  img, varios=True)
        elif acao == "add-stylesheet" and l:
            self.escolher_arquivo("Adicionar folha de estilos", [("CSS", ["*.css"])],
                                  lambda cs: cs and (l.adicionar_css(cs[0]), self.atualizar_ui()))
        elif acao == "generate-toc" and l:
            l.gerar_toc()
            self.atualizar_ui()
            self.responder("toc-generated", {"success": True})
        elif acao == "validate" and l:
            self.responder("validation-result", {"errors": l.validar()})


if __name__ == "__main__":
    archhost.rodar("com.archquilly.Volury", Janela)
