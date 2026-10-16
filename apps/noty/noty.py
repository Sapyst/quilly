#!/usr/bin/env python3
"""
Noty (Arch Quilly) — editor de texto simples com estética de vidro. Só .hyp, .md, .txt e .html.

Port do Noty de Windows (C# + WebView2). A interface (ui/index.html) é a original: quem entende .hyp,
Markdown, anexos, busca, tema etc. é o JavaScript dela. Este arquivo é o "lado C#" em Python:
  - RPC {type:'rpc', id, method, args} -> {type:'rpc-reply', id, ok, data, error};
  - mensagens soltas: dirty, title, fileinfo, devtools;
  - fechar com alterações -> manda {type:'action', action:'confirmExit'} e espera o app.exitForce;
  - menu do botão direito com Negrito/Itálico/Código, Pesquisar na web, Abrir/Salvar;
  - uma janela por arquivo (como no original); tamanho lembrado em window.json.
Vidro: no Windows era o acrílico; aqui a janela é transparente e o Hyprland desfoca o que está atrás.
Dados: ~/.local/share/arch-quilly/noty (settings.json, theme.css, window.json).
"""
import json
import os
import subprocess
import sys

AQUI = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, AQUI)
import archhost  # noqa: E402
from gi.repository import Gdk, Gio, GLib, Gtk, WebKit  # noqa: E402

VERSAO = "1.0.0"
PERFIL = archhost.dados("noty")
EXTENSOES = (".md", ".txt", ".markdown", ".text", ".hyp", ".html", ".htm", "")
LIMITE = 20 * 1024 * 1024
IDIOMAS = {"pt-BR": "pt_BR", "en-US": "en_US", "es": "es", "fr": "fr"}

TEMA_PADRAO = """/* ============================================================
   Tema do Noty — edite a vontade, isto e CSS puro.
   Salvou, aplicou. Estragou tudo? Botao "Restaurar padrao".
   ============================================================ */

:root {
  /* O vidro: este fundo fica POR CIMA do desfoque da janela (Hyprland).
     Quanto mais transparente, mais desktop atravessa.
     Fume escuro, um verde bem sutil, quase incolor. */
  --fundo: rgba(13, 20, 15, 0.35);

  /* Texto do editor */
  --texto: rgba(238, 244, 240, 0.96);
  --texto-suave: rgba(238, 244, 240, 0.45);

  /* Fonte e ritmo (a estetica maquina-de-escrever da tela inicial) */
  --fonte: "Cascadia Mono", "Adwaita Mono", "JetBrains Mono", "DejaVu Sans Mono", monospace;
  --tamanho: 15px;
  --altura-linha: 1.75;
  --largura-texto: 68ch;   /* coluna central; use 100% pra ocupar tudo */

  /* Cursor e selecao */
  --selecao: rgba(126, 214, 158, 0.30);
  --cursor: #8fd6a8;

  /* Menus, paineis e barra de status */
  --painel: rgba(17, 26, 20, 0.92);
  --painel-borda: rgba(255, 255, 255, 0.10);
  --realce: rgba(130, 220, 165, 0.16);

  /* Barra de rolagem (o "polegar" e o hover dele). Pra ir alem das
     cores, sobrescreva os seletores ::-webkit-scrollbar-* aqui. */
  --rolagem: rgba(238, 244, 240, 0.16);
  --rolagem-hover: rgba(143, 214, 168, 0.45);
}

/* Os dialogos do app (sair sem salvar, avisos...) tambem obedecem as
   variaveis acima — e podem ser re-estilizados aqui por seletor:
   .dlg-fundo (o embacado), .dlg (a caixa), .dlg-msg (o texto),
   .dlg-btn e .dlg-btn.primario (os botoes). */
"""


def ler_json(nome, padrao):
    try:
        with open(os.path.join(PERFIL, nome), encoding="utf-8") as f:
            return json.load(f)
    except (OSError, ValueError):
        return padrao


def gravar_json(nome, obj):
    caminho = os.path.join(PERFIL, nome)
    with open(caminho + ".tmp", "w", encoding="utf-8") as f:
        json.dump(obj, f, indent=2, ensure_ascii=False)
    os.replace(caminho + ".tmp", caminho)


def ler_texto(caminho):
    caminho = os.path.abspath(caminho)
    if os.path.splitext(caminho)[1].lower() not in EXTENSOES:
        raise ValueError("o Noty só abre .hyp, .md, .txt e .html")
    if os.path.getsize(caminho) > LIMITE:
        raise ValueError("arquivo grande demais (mais de 20 MB)")
    with open(caminho, encoding="utf-8-sig", errors="replace", newline="") as f:
        return {"path": caminho, "name": os.path.basename(caminho), "text": f.read()}


def garantir_dicionarios():
    """O pt_BR vem embutido (não existe no repositório oficial do Arch): liga ele no corretor (enchant)
    do usuário. Os outros idiomas vêm dos pacotes hunspell-* do sistema."""
    origem = os.path.join(AQUI, "hunspell")
    destino = os.path.join(GLib.get_user_config_dir(), "enchant", "hunspell")
    if not os.path.isdir(origem):
        return
    os.makedirs(destino, exist_ok=True)
    for f in os.listdir(origem):
        alvo = os.path.join(destino, f)
        if os.path.islink(alvo) and os.readlink(alvo) != os.path.join(origem, f):
            os.remove(alvo)                       # apontava pra outra instalação: refaz
        if not os.path.exists(alvo):
            try:
                os.symlink(os.path.join(origem, f), alvo)
            except OSError:
                pass


class Janela(archhost.Host):
    NOME = "noty"
    PASTA_UI = os.path.join(AQUI, "ui")
    TITULO = "Noty"
    SEM_BORDA = False
    FUNDO = "rgba(0,0,0,0)"          # vidro: o fundo vem do tema (--fundo) por cima do desfoque

    def __init__(self, app, arquivo=None):
        self.arquivo_inicial = arquivo
        self.sujo = False
        self.forcar_saida = False
        self.eh_markdown = True
        self.settings = ler_json("settings.json", {"spellLang": "pt-BR", "ui": {}})
        janela = ler_json("window.json", {})
        if janela.get("W", 0) > 300 and janela.get("H", 0) > 200:
            self.TAMANHO = (janela["W"], janela["H"])
        else:
            self.TAMANHO = (980, 700)
        super().__init__(app)
        self.set_size_request(420, 320)
        if janela.get("Maximized"):
            self.maximize()
        # vidro: janela e barra transparentes (o tema pinta --fundo por cima)
        css = Gtk.CssProvider()
        css.load_from_string("window.noty, window.noty headerbar { background: rgba(10,14,11,0.30); }"
                             " window.noty headerbar { box-shadow: none; }")
        Gtk.StyleContext.add_provider_for_display(Gdk.Display.get_default(), css,
                                                  Gtk.STYLE_PROVIDER_PRIORITY_APPLICATION + 1)
        self.add_css_class("noty")
        # corretor ortográfico (Enchant/hunspell)
        ctx = self.web.get_context()
        ctx.set_spell_checking_enabled(True)
        ctx.set_spell_checking_languages([IDIOMAS.get(self.settings.get("spellLang", "pt-BR"), "pt_BR")])
        self.connect("close-request", self.ao_fechar)
        self.criar_acoes()
        self.monitor = None
        self.vigiado = None
        self.ultimo_texto = None      # o que esta janela sabe que está no disco (abriu ou salvou)
        self.espera_recarga = 0

    # ---------- abrir arquivo vindo de fora (linha de comando, outro app) ----------
    def abrir_arquivo_externo(self, caminho):
        """Já tem arquivo nesta janela? Abre outra (uma janela por arquivo, como no original)."""
        Janela(self.get_application(), caminho).present()

    # ---------- recarga em tempo real (outro programa mudou o arquivo) ----------
    def vigiar(self, caminho, texto):
        """Passa a vigiar `caminho`. `texto` é o que acabou de ser lido/gravado, pra não recarregar o próprio salvar."""
        self.ultimo_texto = texto
        caminho = os.path.abspath(caminho)
        if caminho == self.vigiado:
            return
        if self.monitor:
            self.monitor.cancel()
        self.vigiado = caminho
        self.monitor = Gio.File.new_for_path(caminho).monitor_file(Gio.FileMonitorFlags.WATCH_MOVES, None)
        self.monitor.connect("changed", self.ao_mudar_no_disco)

    def ao_mudar_no_disco(self, _mon, _f, _outro, evento):
        if evento not in (Gio.FileMonitorEvent.CHANGES_DONE_HINT, Gio.FileMonitorEvent.CHANGED,
                          Gio.FileMonitorEvent.CREATED, Gio.FileMonitorEvent.RENAMED,
                          Gio.FileMonitorEvent.MOVED_IN):
            return
        if self.espera_recarga:                  # rajada de eventos de um salvar só: lê uma vez no fim
            GLib.source_remove(self.espera_recarga)
        self.espera_recarga = GLib.timeout_add(200, self.recarregar)

    def recarregar(self):
        self.espera_recarga = 0
        try:
            dados = ler_texto(self.vigiado)
        except (OSError, ValueError):
            return False                         # sumiu ou ficou ilegível: deixa o texto como está
        if dados["text"] != self.ultimo_texto:
            self.ultimo_texto = dados["text"]
            self.enviar({"type": "reload", "data": dados})
        return False

    # ---------- menu do botão direito ----------
    def criar_acoes(self):
        grupo = Gio.SimpleActionGroup()
        for nome in ("save", "saveAs", "open", "md-bold", "md-italic", "md-code", "searchWeb"):
            a = Gio.SimpleAction.new(nome, None)
            a.connect("activate", lambda _a, _p, n=nome: self.enviar({"type": "action", "action": n}))
            grupo.add_action(a)
        self.insert_action_group("noty", grupo)
        self.acoes = grupo

    def menu_contexto(self, _web, menu, hit):
        tirar = {WebKit.ContextMenuAction.GO_BACK, WebKit.ContextMenuAction.GO_FORWARD, WebKit.ContextMenuAction.STOP,
                 WebKit.ContextMenuAction.RELOAD, WebKit.ContextMenuAction.INSPECT_ELEMENT,
                 WebKit.ContextMenuAction.OPEN_LINK_IN_NEW_WINDOW, WebKit.ContextMenuAction.DOWNLOAD_LINK_TO_DISK,
                 WebKit.ContextMenuAction.COPY_LINK_TO_CLIPBOARD, WebKit.ContextMenuAction.OPEN_IMAGE_IN_NEW_WINDOW,
                 WebKit.ContextMenuAction.DOWNLOAD_IMAGE_TO_DISK, WebKit.ContextMenuAction.COPY_IMAGE_TO_CLIPBOARD,
                 WebKit.ContextMenuAction.COPY_IMAGE_URL_TO_CLIPBOARD, WebKit.ContextMenuAction.OPEN_FRAME_IN_NEW_WINDOW,
                 WebKit.ContextMenuAction.OPEN_VIDEO_IN_NEW_WINDOW, WebKit.ContextMenuAction.OPEN_AUDIO_IN_NEW_WINDOW,
                 WebKit.ContextMenuAction.DOWNLOAD_VIDEO_TO_DISK, WebKit.ContextMenuAction.DOWNLOAD_AUDIO_TO_DISK}
        if os.environ.get("ARCH_DEV"):
            tirar.discard(WebKit.ContextMenuAction.INSPECT_ELEMENT)
        for item in list(menu.get_items()):
            if item.get_stock_action() in tirar:
                menu.remove(item)
        # separadores sobrando no começo/fim/repetidos
        itens = menu.get_items()
        anterior_sep = True
        for item in list(itens):
            sep = item.is_separator()
            if sep and anterior_sep:
                menu.remove(item)
            anterior_sep = sep
        if menu.get_n_items() and menu.get_item_at_position(menu.get_n_items() - 1).is_separator():
            menu.remove(menu.get_item_at_position(menu.get_n_items() - 1))

        def item(acao, rotulo):
            return WebKit.ContextMenuItem.new_from_gaction(self.acoes.lookup_action(acao), rotulo, None)
        if hit.context_is_selection():
            if self.eh_markdown:
                for i, (a, r) in enumerate((("md-bold", "Negrito"), ("md-italic", "Itálico"), ("md-code", "Código"))):
                    menu.insert(item(a, r), i)
                menu.insert(WebKit.ContextMenuItem.new_separator(), 3)
            menu.append(WebKit.ContextMenuItem.new_separator())
            menu.append(item("searchWeb", "Pesquisar na web"))
        else:
            if menu.get_n_items():
                menu.append(WebKit.ContextMenuItem.new_separator())
            menu.append(item("open", "Abrir…"))
            menu.append(item("save", "Salvar"))
            menu.append(item("saveAs", "Salvar como…"))
        return False

    # ---------- fechar ----------
    def ao_fechar(self, *_):
        if self.sujo and not self.forcar_saida and self.carregado:
            self.enviar({"type": "action", "action": "confirmExit"})
            return True          # o app pergunta (Salvar e sair / Sair sem salvar / Ficar)
        self.salvar_janela()
        return False

    def salvar_janela(self):
        w, h = self.get_default_size()
        if self.get_width() > 0 and not self.is_maximized():
            w, h = self.get_width(), self.get_height()
        gravar_json("window.json", {"W": w, "H": h, "Maximized": self.is_maximized()})

    # ---------- ponte ----------
    def ao_receber(self, msg):
        if not isinstance(msg, dict):
            return
        t = msg.get("type")
        if t == "dirty":
            self.sujo = bool(msg.get("value"))
        elif t == "title":
            self.set_title(msg.get("text") or "Noty")
        elif t == "fileinfo":
            self.eh_markdown = (msg.get("ext") or "").lower() != ".txt"
        elif t == "devtools":
            if os.environ.get("ARCH_DEV"):
                self.web.get_inspector().show()
        elif t == "rpc":
            self.rpc(msg.get("id"), msg.get("method"), msg.get("args") or {})

    def responder(self, id_, ok, data=None, erro=None):
        self.enviar({"type": "rpc-reply", "id": id_, "ok": ok, "data": data, "error": erro})

    def rpc(self, id_, metodo, a):
        ok = lambda d=None: self.responder(id_, True, d)          # noqa: E731
        try:
            if metodo == "file.openDialog":
                filtros = [("Documento Hyper", ["*.hyp"]), ("Textos (.hyp .md .txt .html)", ["*.hyp", "*.md", "*.txt", "*.html", "*.htm"]),
                           ("Markdown", ["*.md"]), ("HTML", ["*.html", "*.htm"]), ("Texto", ["*.txt"]), ("Todos", ["*"])]

                def escolheu(cs):
                    try:
                        dados = ler_texto(cs[0]) if cs else None
                        if dados:
                            self.vigiar(dados["path"], dados["text"])
                        ok(dados)
                    except Exception as e:  # noqa: BLE001
                        self.responder(id_, False, erro=str(e))
                self.escolher_arquivo("Abrir", filtros, escolheu)
            elif metodo == "file.openPath":
                dados = ler_texto(a["path"])
                self.vigiar(dados["path"], dados["text"])
                ok(dados)
            elif metodo == "file.save":
                with open(a["path"], "w", encoding="utf-8", newline="") as f:
                    f.write(a.get("text") or "")
                self.vigiar(a["path"], a.get("text") or "")
                ok({"ok": True})
            elif metodo == "file.saveDialog":
                nome = a.get("name") or "sem-titulo.hyp"
                ext = os.path.splitext(nome)[1].lower() or ".hyp"
                todos = [(".hyp", "Documento Hyper", ["*.hyp"]), (".md", "Markdown", ["*.md"]),
                         (".html", "Página HTML", ["*.html"]), (".txt", "Texto", ["*.txt"])]
                todos.sort(key=lambda x: x[0] != ext)            # o tipo do nome sugerido primeiro

                def salvou(c):
                    if not c:
                        ok(None)
                        return
                    if not os.path.splitext(c)[1]:
                        c += ext
                    ok({"path": c, "name": os.path.basename(c)})
                self.salvar_como("Salvar como", nome, [(n, p) for _, n, p in todos], salvou)
            elif metodo == "theme.read":
                caminho = os.path.join(PERFIL, "theme.css")
                if not os.path.exists(caminho):
                    open(caminho, "w", encoding="utf-8").write(TEMA_PADRAO)
                ok({"text": open(caminho, encoding="utf-8").read()})
            elif metodo == "theme.write":
                open(os.path.join(PERFIL, "theme.css"), "w", encoding="utf-8").write(a.get("text") or "")
                ok({"ok": True})
            elif metodo == "theme.default":
                ok({"text": TEMA_PADRAO})
            elif metodo == "settings.get":
                ok(self.settings)
            elif metodo == "settings.set":
                self.settings = a if isinstance(a, dict) else self.settings
                gravar_json("settings.json", self.settings)
                ok({"ok": True})
            elif metodo == "spell.set":
                lang = a.get("lang") or "pt-BR"
                self.settings["spellLang"] = lang
                gravar_json("settings.json", self.settings)
                self.web.get_context().set_spell_checking_languages([IDIOMAS.get(lang, lang.replace("-", "_"))])
                ok({"restart": False})                         # aqui troca na hora, sem reiniciar
            elif metodo == "app.info":
                ok({"version": VERSAO, "appDir": AQUI, "profileDir": PERFIL,
                    "spellLang": self.settings.get("spellLang", "pt-BR"),
                    "initialFile": self.arquivo_inicial if self.arquivo_inicial and os.path.isfile(self.arquivo_inicial) else None})
            elif metodo == "app.openExternal":
                url = a.get("url") or ""
                if url.startswith(("http://", "https://", "mailto:")):
                    archhost.abrir_externo(url)
                ok({"ok": True})
            elif metodo == "app.openProfile":
                archhost.abrir_externo(PERFIL)
                ok({"ok": True})
            elif metodo == "app.restart":
                self.sujo = False
                ok(None)
                subprocess.Popen([sys.executable, os.path.abspath(__file__)] +
                                 ([self.arquivo_inicial] if self.arquivo_inicial else []), start_new_session=True)
                GLib.timeout_add(200, lambda: (self.close(), False)[1])
            elif metodo == "app.exit":
                ok(None)
                GLib.idle_add(lambda: (self.close(), False)[1])
            elif metodo == "app.exitForce":
                self.forcar_saida = True
                ok(None)
                GLib.idle_add(lambda: (self.close(), False)[1])
            else:
                self.responder(id_, False, erro=f"Método não implementado: {metodo}")
        except Exception as e:  # noqa: BLE001
            self.responder(id_, False, erro=str(e))


class App(archhost.App):
    """Uma janela por arquivo: abrir um arquivo cria uma janela nova."""

    def do_activate(self):
        if not self.get_windows():
            Janela(self).present()
        else:
            self.get_windows()[0].present()

    def do_open(self, arquivos, _n, _dica):
        for f in arquivos:
            if f.get_path():
                Janela(self, f.get_path()).present()


if __name__ == "__main__":
    garantir_dicionarios()
    archhost.Adw.init()
    App("com.archquilly.Noty", Janela).run(sys.argv)
