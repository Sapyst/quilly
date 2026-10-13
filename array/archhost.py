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
import secrets
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
    MEMORIA_MB = 0               # > 0: teto de memória do processo web (o WebKit solta caches antes de chegar lá)

    def __init__(self, app, **kw):
        super().__init__(application=app, title=self.TITULO, default_width=self.TAMANHO[0],
                         default_height=self.TAMANHO[1], **kw)
        if self.ESCURO:
            Adw.StyleManager.get_default().set_color_scheme(Adw.ColorScheme.FORCE_DARK)
        contexto = self.contexto()
        self.registrar_esquema(contexto)
        gerente = WebKit.UserContentManager()
        gerente.add_script(WebKit.UserScript.new(PONTE_JS, WebKit.UserContentInjectedFrames.TOP_FRAME,
                                                 WebKit.UserScriptInjectionTime.START, None, None))
        gerente.register_script_message_handler("host", None)
        gerente.connect("script-message-received::host", self._mensagem)
        self.web = WebKit.WebView(user_content_manager=gerente, web_context=contexto)
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
        self.destinos = {}            # token -> caminho: downloads que a página pode gravar (ver baixar_para)
        self.web.get_network_session().connect("download-started", self._download)
        self.web.connect("load-changed", self._carregou)
        self.web.load_uri(f"app://{self.NOME}/{self.PAGINA}")

    # ---------- servir a interface ----------
    _contextos = {}

    def contexto(self):
        """O contexto web do app. Com MEMORIA_MB, um contexto próprio com teto de memória: o WebKit começa a
        soltar caches (imagens decodificadas, JIT) ao chegar perto e nunca passa do limite."""
        if not self.MEMORIA_MB:
            return WebKit.WebContext.get_default()
        if self.NOME not in Host._contextos:
            pressao = WebKit.MemoryPressureSettings.new()
            pressao.set_memory_limit(int(self.MEMORIA_MB))
            pressao.set_strict_threshold(0.75)     # (o estrito antes: o conservador tem de ser menor que ele)
            pressao.set_conservative_threshold(0.5)
            pressao.set_kill_threshold(0.0)        # 0 = nunca mata o processo (só aperta)
            Host._contextos[self.NOME] = WebKit.WebContext(memory_pressure_settings=pressao)
        return Host._contextos[self.NOME]

    def registrar_esquema(self, ctx):
        if id(ctx) in _esquemas:
            return
        _esquemas.add(id(ctx))
        ctx.register_uri_scheme("app", Host._servir)
        ctx.get_security_manager().register_uri_scheme_as_secure("app")
        ctx.get_security_manager().register_uri_scheme_as_cors_enabled("app")

    _pastas = {}
    _dinamicos = {}        # nome -> função(caminho) -> (bytes, mime) | (caminho_de_arquivo, mime) | None

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
                if isinstance(dados_, str):                   # caminho de arquivo: o WebKit lê do disco, em fluxo
                    arquivo = Gio.File.new_for_path(dados_)
                    req.finish(arquivo.read(None), os.path.getsize(dados_), mime)
                else:
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
        """Links externos (http...) abrem no navegador do sistema, não dentro do app. Um blob: com atributo
        download vira um download do WebKit (gravado onde baixar_para mandou), nunca navegação."""
        if tipo == WebKit.PolicyDecisionType.NAVIGATION_ACTION or tipo == WebKit.PolicyDecisionType.NEW_WINDOW_ACTION:
            acao = decisao.get_navigation_action()
            uri = acao.get_request().get_uri()
            if uri.startswith(("http://", "https://", "mailto:")):
                abrir_externo(uri)
                decisao.ignore()
                return True
            if uri.startswith("blob:") and (acao.get_navigation_type() == WebKit.NavigationType.LINK_CLICKED):
                decisao.download()
                return True
        return False

    def _carregou(self, _web, evento):
        if evento == WebKit.LoadEvent.FINISHED:
            self.carregado = True
            for m in self._fila:
                self.enviar(m)
            self._fila.clear()
            self.ao_carregar()

    # ---------- gravar arquivos pelo WebKit (download de um blob, em fluxo, sem base64 na ponte) ----------
    # A página cria <a download="array-token-<token>" href="blob:..."> e clica; o token foi dado antes pelo app
    # (baixar_para) junto com o destino. Qualquer outro download é recusado.
    def baixar_para(self, caminho):
        token = secrets.token_urlsafe(12)
        self.destinos[token] = caminho
        return token

    def _download(self, _sessao, download):
        def destino(d, nome):
            # o WebKit acrescenta a extensão do MIME ao nome sugerido (array-token-xxx.png): fica só o token
            token = (nome or "").removeprefix("array-token-").split(".", 1)[0]
            caminho = self.destinos.pop(token, None) if nome and nome.startswith("array-token-") else None
            if not caminho:
                d.cancel()
                return True
            d.set_allow_overwrite(True)
            d.set_destination(caminho)
            d._caminho = caminho
            return True
        def falhou(d, erro):
            d._falhou = True
            if os.environ.get("ARCH_DEV"):
                print(f"[archhost] download falhou destino={d.get_destination()!r}: {erro}", file=sys.stderr)
            self.ao_gravar(getattr(d, "_caminho", None), erro)
        def terminou(d):   # 'finished' também vem depois de 'failed': só avisa o sucesso
            if not getattr(d, "_falhou", False):
                self.ao_gravar(getattr(d, "_caminho", None), None)
        download.connect("decide-destination", destino)
        download.connect("failed", falhou)
        download.connect("finished", terminou)

    def ao_gravar(self, caminho, erro):
        """Pra sobrescrever: um arquivo pedido com baixar_para terminou (erro = None) ou falhou."""

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
