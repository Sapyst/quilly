"""
archhost.py — a "casca" Linux dos apps da Arch Quilly (Quilly, Noty, Artry, Volury).

No Windows os apps eram C# + WebView2 com a interface em HTML/JS. Aqui:
  - a janela é GTK4 + WebKitGTK 6;
  - a interface é servida por um endereço interno  app://<nome-do-app>/...  (origem estável: o
    localStorage dos apps continua funcionando entre execuções);
  - uma imitação da ponte do WebView2 é injetada antes de qualquer script da página:
        window.chrome.webview.postMessage(x)          -> chega no Python (Host.ao_receber)
        window.chrome.webview.addEventListener('message', fn)  <- Host.enviar(obj) chama fn({data: obj})
    então o JavaScript original funciona sem mudanças; só o "lado C#" é reescrito em Python.

Cada app cria uma subclasse de Host e implementa ao_receber(msg). Utilidades comuns: diálogos de
abrir/salvar (portal do sistema), comandos de janela (arrastar, minimizar, maximizar, fechar), pastas
de dados (XDG), abrir arquivos/pastas no app padrão.
"""
import json
import mimetypes
import os
import subprocess
import sys

import gi

gi.require_version("Gtk", "4.0")
gi.require_version("Adw", "1")
gi.require_version("WebKit", "6.0")
from gi.repository import Adw, Gdk, Gio, GLib, Gtk, WebKit  # noqa: E402

PONTE_JS = r"""
(() => {
  if (window.chrome && window.chrome.webview && window.chrome.webview.__arch) return;
  const ouvintes = [];
  const enviar = (m) => window.webkit.messageHandlers.host.postMessage(
      typeof m === "string" ? JSON.stringify({__texto: m}) : JSON.stringify({__json: m}));
  window.chrome = window.chrome || {};
  window.chrome.webview = {
    __arch: true,
    postMessage: enviar,
    addEventListener: (tipo, fn) => { if (tipo === "message") ouvintes.push(fn); },
    removeEventListener: (tipo, fn) => { const i = ouvintes.indexOf(fn); if (i >= 0) ouvintes.splice(i, 1); },
    hostObjects: {},
  };
  window.__archDespachar = (dado) => { for (const fn of ouvintes.slice()) { try { fn({data: dado}); } catch (e) { console.error(e); } } };
  window.__ARCH_LINUX__ = true;
})();
"""

_esquemas = set()


def dados(app):
    """~/.local/share/arch-quilly/<app> (criado se faltar)."""
    p = os.path.join(GLib.get_user_data_dir(), "arch-quilly", app)
    os.makedirs(p, exist_ok=True)
    return p


def abrir_externo(caminho_ou_url):
    """Abre no app padrão do sistema (xdg-open)."""
    subprocess.Popen(["xdg-open", caminho_ou_url], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                     start_new_session=True)


def mostrar_na_pasta(caminho):
    """Abre o gerenciador de arquivos com o arquivo selecionado (portal FileManager1), ou a pasta."""
    try:
        bus = Gio.bus_get_sync(Gio.BusType.SESSION)
        bus.call_sync("org.freedesktop.FileManager1", "/org/freedesktop/FileManager1",
                      "org.freedesktop.FileManager1", "ShowItems",
                      GLib.Variant("(ass)", ([Gio.File.new_for_path(caminho).get_uri()], "")),
                      None, Gio.DBusCallFlags.NONE, 2000)
    except GLib.Error:
        abrir_externo(os.path.dirname(caminho) or caminho)


class Host(Adw.ApplicationWindow):
    """Janela com a interface web do app. Subclasses: definem NOME, PASTA_UI, PAGINA e ao_receber()."""
    NOME = "app"                 # nome curto (vira app://NOME/)
    PASTA_UI = ""                # pasta com o index.html etc.
    PAGINA = "index.html"
    TITULO = "App"
    TAMANHO = (1100, 720)
    SEM_BORDA = True             # True = o app desenha a própria barra em HTML; False = barra nativa fina
    ESCURO = True                # os apps da suíte foram desenhados escuros
    FUNDO = "#14171A"            # cor de fundo enquanto a página carrega

    def __init__(self, app, **kw):
        super().__init__(application=app, title=self.TITULO, default_width=self.TAMANHO[0],
                         default_height=self.TAMANHO[1], **kw)
        if self.ESCURO:
            Adw.StyleManager.get_default().set_color_scheme(Adw.ColorScheme.FORCE_DARK)
        self.registrar_esquema()
        gerente = WebKit.UserContentManager()
        gerente.add_script(WebKit.UserScript.new(PONTE_JS, WebKit.UserContentInjectedFrames.TOP_FRAME,
                                                 WebKit.UserScriptInjectionTime.START, None, None))
        gerente.register_script_message_handler("host", None)
        gerente.connect("script-message-received::host", self._mensagem)
        self.web = WebKit.WebView(user_content_manager=gerente)
        cfg = self.web.get_settings()
        cfg.set_enable_developer_extras(bool(os.environ.get("ARCH_DEV")))
        cfg.set_javascript_can_access_clipboard(True)
        cfg.set_enable_write_console_messages_to_stdout(bool(os.environ.get("ARCH_DEV")))
        cfg.set_allow_file_access_from_file_urls(True)
        cor = Gdk.RGBA()
        cor.parse(self.FUNDO)
        self.web.set_background_color(cor)
        self.web.connect("decide-policy", self._politica)
        self.web.connect("context-menu", self.menu_contexto)
        if self.SEM_BORDA:
            self.set_content(self.web)
        else:
            # barra nativa fina (segue Settings → Theme → Window buttons), na cor do app
            self.barra = Adw.HeaderBar()
            self.barra.add_css_class("flat")
            moldura = Adw.ToolbarView(content=self.web)
            moldura.add_top_bar(self.barra)
            self.set_content(moldura)
            css = Gtk.CssProvider()
            css.load_from_string(f"window.archhost, window.archhost headerbar {{ background: {self.FUNDO}; }}"
                                 " window.archhost headerbar { box-shadow: none; min-height: 34px; }")
            Gtk.StyleContext.add_provider_for_display(Gdk.Display.get_default(), css,
                                                      Gtk.STYLE_PROVIDER_PRIORITY_APPLICATION)
            self.add_css_class("archhost")
        self.carregado = False
        self._fila = []
        self.web.connect("load-changed", self._carregou)
        self.web.load_uri(f"app://{self.NOME}/{self.PAGINA}")

    # ---------- servir a interface ----------
    def registrar_esquema(self):
        if "app" in _esquemas:
            return
        _esquemas.add("app")
        ctx = WebKit.WebContext.get_default()
        ctx.register_uri_scheme("app", Host._servir)
        ctx.get_security_manager().register_uri_scheme_as_secure("app")
        ctx.get_security_manager().register_uri_scheme_as_cors_enabled("app")

    _pastas = {}
    _dinamicos = {}        # nome -> função(caminho) -> (bytes, mime) | None   (arquivos gerados na hora)

    @classmethod
    def _servir(cls, req):
        uri = req.get_uri()                                   # app://nome/caminho
        resto = uri.split("://", 1)[1]
        nome, _, caminho = resto.partition("/")
        caminho = GLib.uri_unescape_string(caminho.split("?")[0].split("#")[0], None) or "index.html"
        dinamico = cls._dinamicos.get(nome)
        if dinamico:
            r = dinamico(caminho)
            if r:
                dados_, mime = r
                req.finish(Gio.MemoryInputStream.new_from_bytes(GLib.Bytes.new(dados_)), len(dados_), mime)
                return
        base = cls._pastas.get(nome)
        alvo = os.path.realpath(os.path.join(base or "", caminho))
        if not base or not alvo.startswith(os.path.realpath(base)) or not os.path.isfile(alvo):
            req.finish_error(GLib.Error.new_literal(GLib.quark_from_string("archhost"), f"não achei {caminho}", 404))
            return
        mime = mimetypes.guess_type(alvo)[0] or "application/octet-stream"
        dados_ = open(alvo, "rb").read()
        req.finish(Gio.MemoryInputStream.new_from_bytes(GLib.Bytes.new(dados_)), len(dados_), mime)

    def __init_subclass__(cls, **kw):
        super().__init_subclass__(**kw)
        if cls.PASTA_UI:
            Host._pastas[cls.NOME] = cls.PASTA_UI

    def _politica(self, _web, decisao, tipo):
        """Links externos (http...) abrem no navegador do sistema, não dentro do app."""
        if tipo == WebKit.PolicyDecisionType.NAVIGATION_ACTION or tipo == WebKit.PolicyDecisionType.NEW_WINDOW_ACTION:
            uri = decisao.get_navigation_action().get_request().get_uri()
            if uri.startswith(("http://", "https://", "mailto:")):
                abrir_externo(uri)
                decisao.ignore()
                return True
        return False

    def _carregou(self, _web, evento):
        if evento == WebKit.LoadEvent.FINISHED:
            self.carregado = True
            for m in self._fila:
                self.enviar(m)
            self._fila.clear()
            self.ao_carregar()

    # ---------- ponte ----------
    def _mensagem(self, _gerente, valor):
        try:
            bruto = json.loads(valor.to_json(0))
            bruto = json.loads(bruto) if isinstance(bruto, str) else bruto
            msg = bruto["__json"] if "__json" in bruto else bruto.get("__texto")
        except Exception as e:  # noqa: BLE001
            print("archhost: mensagem ilegível", e, file=sys.stderr)
            return
        try:
            self.ao_receber(msg)
        except Exception as e:  # noqa: BLE001
            import traceback
            traceback.print_exc()
            self.ao_erro(msg, e)

    def enviar(self, obj):
        """Manda pra página (o 'message' que o JS escuta). Antes de carregar, guarda na fila."""
        if not self.carregado:
            self._fila.append(obj)
            return
        self.web.evaluate_javascript(f"window.__archDespachar({json.dumps(obj, ensure_ascii=False)});", -1,
                                     None, None, None, None, None)

    def js(self, codigo):
        self.web.evaluate_javascript(codigo, -1, None, None, None, None, None)

    # ---------- pra sobrescrever ----------
    def menu_contexto(self, _web, _menu, _hit):
        """Padrão: sem o menu do navegador (Voltar/Recarregar/Inspecionar). ARCH_DEV=1 mostra."""
        return not os.environ.get("ARCH_DEV")

    def ao_receber(self, msg):
        pass

    def ao_carregar(self):
        pass

    def ao_erro(self, msg, erro):
        pass

    # ---------- janela ----------
    def arrastar(self):
        """Começa a arrastar a janela (barra de título desenhada em HTML)."""
        superficie = self.get_surface()
        seat = Gdk.Display.get_default().get_default_seat()
        ponteiro = seat.get_pointer() if seat else None
        if isinstance(superficie, Gdk.Toplevel) and ponteiro:
            _, x, y, _ = superficie.get_device_position(ponteiro) if hasattr(superficie, "get_device_position") else (0, 0, 0, 0)
            superficie.begin_move(ponteiro, 1, x, y, Gdk.CURRENT_TIME)

    def minimizar(self):
        script = os.path.expanduser("~/.local/bin/hypr-minimizar")
        if os.path.exists(script):
            subprocess.Popen([script])
        else:
            self.minimize()

    def maximizar(self):
        script = os.path.expanduser("~/.local/bin/hypr-maximizar")
        if os.path.exists(script):
            subprocess.Popen([script])
        elif self.is_maximized():
            self.unmaximize()
        else:
            self.maximize()

    # ---------- diálogos ----------
    def escolher_arquivo(self, titulo, filtros, ao_escolher, varios=False, pasta_inicial=None):
        """filtros: [("Textos", ["*.txt", "*.md"]), ...]. ao_escolher(lista de caminhos) (vazia = cancelou)."""
        d = Gtk.FileDialog(title=titulo, modal=True)
        d.set_filters(self._filtros(filtros))
        if pasta_inicial and os.path.isdir(pasta_inicial):
            d.set_initial_folder(Gio.File.new_for_path(pasta_inicial))

        def fim(dlg, res):
            try:
                if varios:
                    lista = dlg.open_multiple_finish(res)
                    caminhos = [lista.get_item(i).get_path() for i in range(lista.get_n_items())]
                else:
                    caminhos = [dlg.open_finish(res).get_path()]
            except GLib.Error:
                caminhos = []
            ao_escolher(caminhos)
        (d.open_multiple if varios else d.open)(self, None, fim)

    def salvar_como(self, titulo, nome_sugerido, filtros, ao_escolher, pasta_inicial=None):
        d = Gtk.FileDialog(title=titulo, modal=True, initial_name=nome_sugerido or "")
        d.set_filters(self._filtros(filtros))
        if pasta_inicial and os.path.isdir(pasta_inicial):
            d.set_initial_folder(Gio.File.new_for_path(pasta_inicial))

        def fim(dlg, res):
            try:
                ao_escolher(dlg.save_finish(res).get_path())
            except GLib.Error:
                ao_escolher(None)
        d.save(self, None, fim)

    def escolher_pasta(self, titulo, ao_escolher):
        d = Gtk.FileDialog(title=titulo, modal=True)

        def fim(dlg, res):
            try:
                ao_escolher(dlg.select_folder_finish(res).get_path())
            except GLib.Error:
                ao_escolher(None)
        d.select_folder(self, None, fim)

    @staticmethod
    def _filtros(filtros):
        loja = Gio.ListStore.new(Gtk.FileFilter)
        for nome, padroes in filtros or []:
            f = Gtk.FileFilter(name=nome)
            for p in padroes:
                f.add_pattern(p)
                f.add_pattern(p.upper())
            loja.append(f)
        return loja

    def perguntar(self, titulo, texto, botoes, ao_responder, destrutivo=None):
        """botoes: [("id", "Texto"), ...]. ao_responder(id)."""
        d = Adw.AlertDialog(heading=titulo, body=texto)
        for i, t in botoes:
            d.add_response(i, t)
        if destrutivo:
            d.set_response_appearance(destrutivo, Adw.ResponseAppearance.DESTRUCTIVE)
        d.connect("response", lambda _d, r: ao_responder(r))
        d.present(self)


class App(Adw.Application):
    """Instância única por app: abrir de novo (ou abrir um arquivo) vai pra janela que já existe."""

    def __init__(self, app_id, classe_janela):
        super().__init__(application_id=app_id, flags=Gio.ApplicationFlags.HANDLES_OPEN)
        self.classe = classe_janela
        self.set_option_context_parameter_string("[ARQUIVO…]")

    def janela(self):
        return self.props.active_window or self.classe(self)

    def do_activate(self):
        self.janela().present()

    def do_open(self, arquivos, _n, _dica):
        j = self.janela()
        j.present()
        for f in arquivos:
            if f.get_path():
                j.abrir_arquivo_externo(f.get_path())


def rodar(app_id, classe_janela):
    Adw.init()
    App(app_id, classe_janela).run(sys.argv)
