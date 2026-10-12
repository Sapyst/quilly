#!/usr/bin/env python3
"""
Artry (Arch Quilly) — editor de texto minimalista: documentos ricos, código e régua coordenada.

O Artry de Windows era WinForms puro (sem HTML). Aqui ele foi refeito: interface em HTML/JS (ui/) na
casca GTK4 + WebKit da suíte (archhost), e este arquivo é o lado nativo:
  - abrir/salvar: .artry (HTML com formatação), .hyp/.md (Markdown/Hyper Doc v2), .rtf e .docx com
    formatação, .html, texto e código (formatos.py);
  - exportar PDF e imprimir pelo WebKit (com acentos — o original trocava por "?");
  - tela inicial: recentes com prévia do texto;
  - configurações, atalhos, tema e recentes em ~/.local/share/arch-quilly/artry/config.json
    (o Quilly lê os recentes daqui);
  - "Aplicativo padrão e formatos": associa tipos de arquivo ao Artry pelo xdg-mime.
Ponte: {type:'rpc', id, method, args} -> {type:'rpc-reply', id, ok, data, error}.
"""
import json
import os
import re
import subprocess
import sys

AQUI = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, AQUI)
import archhost  # noqa: E402
import formatos as F  # noqa: E402
from gi.repository import Adw, Gdk, Gio, GLib, Gtk, WebKit  # noqa: E402

PERFIL = archhost.dados("artry")
CONFIG = os.path.join(PERFIL, "config.json")
DESKTOP = "com.archquilly.Artry.desktop"
try:
    VERSAO = open(os.path.join(AQUI, "VERSAO")).read().strip()
except OSError:
    VERSAO = "1.1.0"

PADRAO = {"FontName": "Adwaita Sans", "FontSize": 11, "CodeFontName": "Adwaita Mono", "WordWrap": True,
          "Dark": True, "ShowLines": True, "ShowRuler": True, "PageViewMode": True, "ShowToolbar": True,
          "Shortcuts": {}, "WindowWidth": 1180, "WindowHeight": 780, "RecentFiles": [],
          "AssociatedExtensions": [".artry", ".txt", ".md", ".rtf", ".docx", ".log", ".hyp"]}

# extensão -> tipo MIME (associação de arquivos no Linux)
MIMES = {".hyp": "application/x-hyp", ".artry": "application/x-artry", ".txt": "text/plain",
         ".md": "text/markdown", ".rtf": "application/rtf",
         ".docx": "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
         ".log": "text/x-log", ".json": "application/json", ".xml": "application/xml", ".csv": "text/csv",
         ".html": "text/html", ".htm": "text/html", ".css": "text/css", ".cs": "text/x-csharp",
         ".py": "text/x-python", ".js": "text/javascript", ".ts": "text/x-typescript", ".sql": "application/sql",
         ".ps1": "text/x-powershell", ".sh": "application/x-shellscript", ".yml": "application/x-yaml",
         ".yaml": "application/x-yaml"}


def ler_config():
    try:
        c = json.load(open(CONFIG, encoding="utf-8"))
    except (OSError, ValueError):
        c = {}
    for k, v in PADRAO.items():
        c.setdefault(k, v)
    return c


def gravar_config(c):
    with open(CONFIG + ".tmp", "w", encoding="utf-8") as f:
        json.dump(c, f, indent=2, ensure_ascii=False)
    os.replace(CONFIG + ".tmp", CONFIG)


def tipo_do_arquivo(ext):
    if ext in (".artry", ".rtf", ".docx"):
        return "rich"
    if ext in (".hyp", ".md", ".markdown"):
        return "md"
    if ext in F.EXT_CODIGO:
        return "code"
    return "text"


def abrir_documento(caminho):
    caminho = os.path.abspath(caminho)
    ext = os.path.splitext(caminho)[1].lower()
    tipo = tipo_do_arquivo(ext)
    doc = {"path": caminho, "name": os.path.basename(caminho), "ext": ext, "kind": tipo,
           "html": "", "text": "", "frontmatter": ""}
    if ext == ".docx":
        doc["html"] = F.docx_para_html(caminho)
    elif ext == ".rtf":
        doc["html"] = F.rtf_para_html(F.ler_texto(caminho))
    elif ext == ".artry":
        t = F.ler_texto(caminho)
        if re.match(r"\s*<", t):
            m = re.search(r"<body[^>]*>([\s\S]*)</body>", t, re.I)
            doc["html"] = m.group(1) if m else t
        else:
            doc["html"] = F.texto_para_html(t)          # .artry antigo (texto puro)
    elif tipo == "md":
        doc["html"], doc["frontmatter"] = F.markdown_para_html(F.ler_texto(caminho))
        doc["text"] = F.ler_texto(caminho)
    else:
        doc["text"] = F.ler_texto(caminho)
    return doc


ARTRY_MODELO = """<!DOCTYPE html>
<html><head><meta charset="utf-8"><meta name="generator" content="Artry {v}"><title>{t}</title></head>
<body>
{c}
</body></html>
"""


def salvar_documento(a, cfg):
    caminho = a["path"]
    ext = os.path.splitext(caminho)[1].lower()
    html_ = a.get("html") or ""
    if ext == ".artry":
        dados = ARTRY_MODELO.format(v=VERSAO, t=F.esc(os.path.basename(caminho)), c=html_)
    elif ext == ".rtf":
        dados = F.html_para_rtf(html_, cfg.get("FontName"), cfg.get("FontSize"))
    elif ext == ".docx":
        F.html_para_docx(html_, caminho, cfg.get("FontName"), cfg.get("FontSize"))
        return
    elif ext in (".hyp", ".md", ".markdown"):
        dados = a.get("markdown") if a.get("markdown") is not None else a.get("text") or ""
    elif ext in (".html", ".htm") and a.get("kind") != "code":
        dados = ARTRY_MODELO.format(v=VERSAO, t=F.esc(os.path.basename(caminho)), c=html_)
    else:
        dados = a.get("text") or ""
    with open(caminho + ".tmp", "w", encoding="utf-8", newline="") as f:
        f.write(dados)
    os.replace(caminho + ".tmp", caminho)


def documento_para_impressao(html_, fonte, tamanho, codigo=False):
    """HTML do documento numa página A4 limpa (pra PDF e impressão)."""
    corpo = f"<pre>{F.esc(html_)}</pre>" if codigo else html_
    return f"""<!DOCTYPE html><html><head><meta charset="utf-8"><style>
@page {{ size: A4; margin: 20mm 18mm; }}
body {{ font-family: '{fonte}', sans-serif; font-size: {tamanho}pt; color: #111; background: #fff; line-height: 1.4; }}
pre {{ font-family: 'Adwaita Mono', monospace; font-size: 9.5pt; white-space: pre-wrap; }}
h1.titulo {{ font-size: 26pt; }} h2.subtitulo {{ font-size: 16pt; font-weight: normal; color: #444; }}
h1.t1 {{ font-size: 20pt; }} h2.t2 {{ font-size: 16pt; }} h3.t3 {{ font-size: 14pt; }}
p {{ margin: 0 0 .35em; }} img {{ max-width: 100%; }}
</style></head><body>{corpo}</body></html>"""


FILTROS_SALVAR = [("Documento Hyper (.hyp)", ["*.hyp"]), ("Documento Artry (.artry)", ["*.artry"]),
                  ("Documento Word (.docx)", ["*.docx"]), ("Texto rico (.rtf)", ["*.rtf"]),
                  ("Texto Puro / Markdown", ["*.txt", "*.md"]), ("Página Web", ["*.html", "*.htm"]),
                  ("Código e dados", ["*.cs", "*.js", "*.ts", "*.py", "*.java", "*.cpp", "*.c", "*.h", "*.css",
                                      "*.sql", "*.json", "*.xml", "*.ps1", "*.sh", "*.yml", "*.yaml"]),
                  ("Todos os arquivos", ["*"])]
FILTROS_ABRIR = [("Documentos Hyper", ["*.hyp"]),
                 ("Todos os textos", ["*.hyp", "*.artry", "*.txt", "*.md", "*.rtf", "*.docx", "*.log", "*.csv",
                                      "*.json", "*.xml", "*.html", "*.htm", "*.css", "*.js", "*.ts", "*.py",
                                      "*.cs", "*.sql", "*.sh", "*.yml", "*.yaml"]),
                 ("Artry", ["*.artry"]), ("Word", ["*.docx"]), ("RTF", ["*.rtf"]),
                 ("Texto / Markdown", ["*.txt", "*.md"]), ("Código e dados", ["*.cs", "*.js", "*.ts", "*.py",
                                                                             "*.java", "*.cpp", "*.c", "*.h",
                                                                             "*.css", "*.sql", "*.json", "*.xml",
                                                                             "*.sh", "*.yml", "*.yaml"]),
                 ("Todos", ["*"])]


class Janela(archhost.Host):
    NOME = "artry"
    PASTA_UI = os.path.join(AQUI, "ui")
    TITULO = "Artry"
    SEM_BORDA = False

    def __init__(self, app, arquivo=None):
        self.cfg = ler_config()
        self.ESCURO = self.cfg.get("Dark", True)
        self.FUNDO = "#181A1E" if self.ESCURO else "#F4F3EF"
        self.TAMANHO = (max(760, self.cfg.get("WindowWidth") or 1180), max(480, self.cfg.get("WindowHeight") or 780))
        self.arquivo_inicial = arquivo
        self.sujo = False
        self.forcar = False
        super().__init__(app)
        if not self.ESCURO:
            Adw.StyleManager.get_default().set_color_scheme(Adw.ColorScheme.FORCE_LIGHT)
        self.set_size_request(760, 480)
        self.css_barra = Gtk.CssProvider()
        Gtk.StyleContext.add_provider_for_display(Gdk.Display.get_default(), self.css_barra,
                                                  Gtk.STYLE_PROVIDER_PRIORITY_APPLICATION + 2)
        self.pintar_barra()
        self.connect("close-request", self.ao_fechar)
        self.impressoes = []          # webviews de impressão vivas até terminar

    def pintar_barra(self):
        cor = "#282C34" if self.cfg.get("Dark", True) else "#FAF9F6"
        self.css_barra.load_from_string(f"window.archhost, window.archhost headerbar {{ background: {cor}; }}")

    def abrir_arquivo_externo(self, caminho):
        if self.carregado and not self.sujo and not getattr(self, "tem_doc", False):
            self.enviar({"type": "open", "path": caminho})
        else:
            Janela(self.get_application(), caminho).present()

    # ---------- fechar ----------
    def ao_fechar(self, *_):
        if self.sujo and not self.forcar and self.carregado:
            self.enviar({"type": "confirmExit"})
            return True
        w, h = self.get_width(), self.get_height()
        cfg = ler_config()
        if w > 300 and not self.is_maximized():
            cfg["WindowWidth"], cfg["WindowHeight"] = w, h
        gravar_config(cfg)
        return False

    # ---------- ponte ----------
    def ao_receber(self, msg):
        if not isinstance(msg, dict):
            return
        t = msg.get("type")
        if t == "dirty":
            self.sujo = bool(msg.get("value"))
        elif t == "title":
            self.set_title(msg.get("text") or "Artry")
        elif t == "hasdoc":
            self.tem_doc = bool(msg.get("value"))
        elif t == "fullscreen":
            if self.is_fullscreen():
                self.unfullscreen()
            else:
                self.fullscreen()
        elif t == "rpc":
            id_ = msg.get("id")

            def ok(d=None):
                self.enviar({"type": "rpc-reply", "id": id_, "ok": True, "data": d, "error": None})

            def erro(e):
                self.enviar({"type": "rpc-reply", "id": id_, "ok": False, "data": None, "error": str(e)})
            try:
                self.rpc(msg.get("method"), msg.get("args") or {}, ok, erro)
            except Exception as e:  # noqa: BLE001
                import traceback
                traceback.print_exc()
                erro(e)

    def lembrar(self, caminho):
        cfg = ler_config()
        r = [caminho] + [p for p in cfg.get("RecentFiles", []) if p != caminho]
        cfg["RecentFiles"] = r[:16]
        gravar_config(cfg)
        self.cfg = cfg

    def rpc(self, m, a, ok, erro):
        if m == "app.info":
            ok({"version": VERSAO, "initialFile": self.arquivo_inicial if self.arquivo_inicial and
                os.path.isfile(self.arquivo_inicial) else None, "profile": PERFIL})
        elif m == "settings.get":
            ok(ler_config())
        elif m == "settings.set":
            cfg = ler_config()
            cfg.update({k: v for k, v in a.items() if k in PADRAO or k.startswith("UI")})
            gravar_config(cfg)
            self.cfg = cfg
            if "Dark" in a:
                Adw.StyleManager.get_default().set_color_scheme(
                    Adw.ColorScheme.FORCE_DARK if a["Dark"] else Adw.ColorScheme.FORCE_LIGHT)
                self.pintar_barra()
            ok(True)
        elif m == "fonts.list":
            r = subprocess.run(["fc-list", ":", "family"], capture_output=True, text=True)
            nomes = sorted({l.split(",")[0].strip() for l in r.stdout.splitlines() if l.strip()}, key=str.lower)
            ok(nomes)
        elif m == "home.cards":
            cards = []
            for p in ler_config().get("RecentFiles", []):
                if os.path.isfile(p) and len(cards) < 15:
                    cards.append({"path": p, "name": os.path.basename(p), "lines": F.preview(p)})
            ok(cards)
        elif m == "recent.remove":
            cfg = ler_config()
            cfg["RecentFiles"] = [p for p in cfg.get("RecentFiles", []) if p != a.get("path")]
            gravar_config(cfg)
            ok(True)
        elif m == "file.reveal":
            archhost.mostrar_na_pasta(a.get("path") or "")
            ok(True)
        elif m == "file.openDialog":
            def escolheu(cs):
                if not cs:
                    ok(None)
                    return
                try:
                    doc = abrir_documento(cs[0])
                    self.lembrar(doc["path"])
                    ok(doc)
                except Exception as e:  # noqa: BLE001
                    erro(f"Não consegui abrir: {e}")
            self.escolher_arquivo("Abrir", FILTROS_ABRIR, escolheu)
        elif m == "file.openPath":
            doc = abrir_documento(a["path"])
            self.lembrar(doc["path"])
            ok(doc)
        elif m == "file.save":
            salvar_documento(a, ler_config())
            self.lembrar(os.path.abspath(a["path"]))
            ok({"path": os.path.abspath(a["path"]), "name": os.path.basename(a["path"])})
        elif m == "file.saveDialog":
            nome = a.get("name") or "sem título.hyp"
            ext = os.path.splitext(nome)[1].lower()
            filtros = sorted(FILTROS_SALVAR, key=lambda f: not any(p == "*" + ext for p in f[1]))

            def salvou(c):
                if c and not os.path.splitext(c)[1]:
                    c += ext or ".hyp"
                ok({"path": c, "name": os.path.basename(c)} if c else None)
            self.salvar_como("Salvar como", nome, filtros, salvou)
        elif m == "export.pdf":
            nome = os.path.splitext(a.get("name") or "sem título")[0] + ".pdf"

            def destino(c):
                if not c:
                    ok(None)
                    return
                if not c.lower().endswith(".pdf"):
                    c += ".pdf"
                self.imprimir(documento_para_impressao(a.get("html") or "", ler_config().get("FontName"),
                                                       ler_config().get("FontSize"), a.get("code")),
                              pdf=c, fim=lambda okk, msg: ok({"path": c}) if okk else erro(msg))
            self.salvar_como("Exportar PDF", nome, [("PDF", ["*.pdf"])], destino)
        elif m == "print":
            self.imprimir(documento_para_impressao(a.get("html") or "", ler_config().get("FontName"),
                                                   ler_config().get("FontSize"), a.get("code")),
                          pdf=None, fim=lambda okk, msg: ok(True) if okk else erro(msg))
        elif m == "assoc.status":
            ativos = []
            for ext, mime in MIMES.items():
                r = subprocess.run(["xdg-mime", "query", "default", mime], capture_output=True, text=True)
                if r.stdout.strip() == DESKTOP:
                    ativos.append(ext)
            ok({"active": ativos, "saved": ler_config().get("AssociatedExtensions", [])})
        elif m == "assoc.apply":
            exts = [e for e in a.get("exts", []) if e in MIMES]
            mimes = sorted({MIMES[e] for e in exts})
            if mimes:
                subprocess.run(["xdg-mime", "default", DESKTOP] + mimes)
            # tira o Artry dos tipos desmarcados
            self.remover_associacoes([MIMES[e] for e in MIMES if e not in exts and MIMES[e] not in mimes])
            cfg = ler_config()
            cfg["AssociatedExtensions"] = exts
            gravar_config(cfg)
            ok(True)
        elif m == "assoc.remove":
            self.remover_associacoes(list(MIMES.values()))
            ok(True)
        elif m == "assoc.openSettings":
            archhost.abrir_externo(os.path.join(GLib.get_user_config_dir(), "mimeapps.list"))
            ok(True)
        elif m == "app.exitForce":
            self.forcar = True
            ok(True)
            GLib.idle_add(lambda: (self.close(), False)[1])
        elif m == "app.newWindow":
            Janela(self.get_application(), a.get("path")).present()
            ok(True)
        else:
            erro(f"Método não implementado: {m}")

    @staticmethod
    def remover_associacoes(mimes):
        caminho = os.path.join(GLib.get_user_config_dir(), "mimeapps.list")
        try:
            linhas = open(caminho, encoding="utf-8").read().splitlines()
        except OSError:
            return
        saida = []
        for l in linhas:
            if "=" in l and not l.startswith("["):
                k, v = l.split("=", 1)
                if k.strip() in mimes:
                    resto = [x for x in v.split(";") if x and x != DESKTOP]
                    if not resto:
                        continue
                    l = f"{k}={';'.join(resto)};"
            saida.append(l)
        open(caminho, "w", encoding="utf-8").write("\n".join(saida) + "\n")

    # ---------- PDF / impressão ----------
    def imprimir(self, html_doc, pdf, fim):
        """Renderiza o documento num WebView à parte (página A4 limpa) e manda pra PDF ou impressora."""
        vista = WebKit.WebView()
        self.impressoes.append(vista)

        def carregou(_v, ev):
            if ev != WebKit.LoadEvent.FINISHED:
                return
            op = WebKit.PrintOperation.new(vista)
            pagina = Gtk.PageSetup()
            pagina.set_paper_size(Gtk.PaperSize.new(Gtk.PAPER_NAME_A4))
            op.set_page_setup(pagina)

            def terminou(*_):
                if vista in self.impressoes:
                    self.impressoes.remove(vista)
                fim(True, "")

            def falhou(_op, e):
                if vista in self.impressoes:
                    self.impressoes.remove(vista)
                fim(False, f"Não consegui imprimir: {e.message if hasattr(e, 'message') else e}")
            op.connect("finished", terminou)
            op.connect("failed", falhou)
            if pdf:
                cfg = Gtk.PrintSettings()
                cfg.set_printer("Print to File")
                cfg.set(Gtk.PRINT_SETTINGS_OUTPUT_FILE_FORMAT, "pdf")
                cfg.set(Gtk.PRINT_SETTINGS_OUTPUT_URI, Gio.File.new_for_path(pdf).get_uri())
                op.set_print_settings(cfg)
                op.print_()
            else:
                op.run_dialog(self)
        vista.connect("load-changed", carregou)
        vista.load_html(html_doc, "app://artry/")


class App(archhost.App):
    def do_activate(self):
        if self.get_windows():
            self.get_windows()[0].present()
        else:
            Janela(self).present()

    def do_open(self, arquivos, _n, _dica):
        for f in arquivos:
            if f.get_path():
                if self.get_windows():
                    self.get_windows()[0].abrir_arquivo_externo(f.get_path())
                else:
                    Janela(self, f.get_path()).present()


if __name__ == "__main__":
    archhost.Adw.init()
    App("com.archquilly.Artry", Janela).run(sys.argv)
