#!/usr/bin/env python3
"""
Array (Arch Quilly) — editor de imagens com camadas e os atalhos do Photoshop.

O motor é o miniPaint (MIT, ui/dist/bundle.js, sem alterações): camadas, seleção, pincéis, texto,
efeitos, histórico. Por cima dele, o Array coloca:
  - ui/array.css: o tema (escuro, chapado, no espírito do Pixelorama);
  - ui/array.js: o mapa de atalhos e nomes de ferramenta do Photoshop, a ordem da barra, abrir/salvar
    pelos diálogos do sistema e o formato .array (o JSON de camadas do miniPaint).
Este arquivo é a casca: janela GTK4 + WebKit (archhost), diálogos, leitura e gravação dos arquivos.

Ponte (JSON): JS -> Python {type: 'open-dialog' | 'save-dialog' | 'save' | 'title' | 'reveal' | 'quit' | 'exit-reply'}
              Python -> JS {type: 'open' | 'save-path' | 'save-cancel' | 'saved' | 'error' | 'confirm-exit'}
Salvar: o JS pede o caminho (save-dialog, com um nome sugerido), o diálogo do sistema devolve (save-path),
o JS gera os dados no formato da extensão escolhida e manda (save); aqui só se grava o arquivo.
Dados: ~/.local/share/arch-quilly/array (settings.json: pasta usada, recentes; window.json: tamanho).
"""
import base64
import json
import mimetypes
import os
import shutil
import subprocess
import sys
import threading
import time
import urllib.request
import zipfile

AQUI = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, AQUI)
import archhost  # noqa: E402
from gi.repository import Gdk, GLib, Gtk, WebKit  # noqa: E402

VERSAO = "1.0.0"
PERFIL = archhost.dados("array")
IMAGENS = (".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp", ".avif", ".svg", ".tif", ".tiff", ".ico")
DOCUMENTOS = (".array", ".json")
LIMITE = 200 * 1024 * 1024
# ---------- upscale: processos externos, nada residente (usou, fez, descarregou) ----------
CACHE = os.path.join(GLib.get_user_cache_dir(), "arch-quilly", "array", "upscale")     # disco, não /tmp (que é RAM)
MOTOR_DIR = os.path.join(PERFIL, "realesrgan")
MOTOR_URL = "https://github.com/xinntao/Real-ESRGAN/releases/download/v0.2.5.0/realesrgan-ncnn-vulkan-20220424-ubuntu.zip"
MOTOR_MB = 45
MODELOS = {"photo": "realesrgan-x4plus", "art": "realesrgan-x4plus-anime"}
FILTROS_ABRIR = [("Images and Array documents", ["*.array", "*.json"] + ["*" + e for e in IMAGENS]),
                 ("Array document", ["*.array"]),
                 ("Images", ["*" + e for e in IMAGENS]),
                 ("All files", ["*"])]
FILTROS_SALVAR = [("Array document (keeps layers)", ["*.array"]), ("PNG", ["*.png"]), ("JPEG", ["*.jpg", "*.jpeg"]),
                  ("WebP", ["*.webp"]), ("GIF", ["*.gif"]), ("BMP", ["*.bmp"]), ("TIFF", ["*.tif", "*.tiff"]),
                  ("JSON (miniPaint)", ["*.json"]), ("All files", ["*"])]


def motor():
    """(binário, pasta de modelos) do Real-ESRGAN: o do sistema, se houver; senão o baixado no perfil; senão None."""
    sistema = shutil.which("realesrgan-ncnn-vulkan")
    if sistema:
        for m in ("/usr/share/realesrgan-ncnn-vulkan/models", "/usr/share/realesrgan-ncnn-vulkan", "/usr/lib/realesrgan-ncnn-vulkan/models"):
            if os.path.isfile(os.path.join(m, "realesrgan-x4plus.param")):
                return sistema, m
    binario = os.path.join(MOTOR_DIR, "realesrgan-ncnn-vulkan")
    modelos = os.path.join(MOTOR_DIR, "models")
    if os.path.isfile(binario) and os.path.isfile(os.path.join(modelos, "realesrgan-x4plus.param")):
        return binario, modelos
    return None, None


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


def ler_arquivo(caminho):
    """-> mensagem 'open' pro JS: documento (.array/.json = texto) ou imagem (data URL)."""
    caminho = os.path.abspath(caminho)
    ext = os.path.splitext(caminho)[1].lower()
    if os.path.getsize(caminho) > LIMITE:
        raise ValueError("file is too big (over 200 MB)")
    if ext in DOCUMENTOS:
        with open(caminho, encoding="utf-8-sig") as f:
            return {"type": "open", "kind": "json", "path": caminho, "name": os.path.basename(caminho), "data": f.read()}
    mime = mimetypes.guess_type(caminho)[0] or "image/png"
    if not mime.startswith("image/"):
        raise ValueError("Array opens images and .array documents")
    with open(caminho, "rb") as f:
        dados = base64.b64encode(f.read()).decode("ascii")
    return {"type": "open", "kind": "image", "path": caminho, "name": os.path.basename(caminho),
            "data": f"data:{mime};base64,{dados}"}


class Janela(archhost.Host):
    NOME = "array"
    PASTA_UI = os.path.join(AQUI, "ui")
    TITULO = "Array"
    SEM_BORDA = False
    FUNDO = "#141518"

    def __init__(self, app, arquivo=None):
        self.arquivo_inicial = arquivo
        self.forcar_saida = False
        self.settings = ler_json("settings.json", {"folder": "", "recent": []})
        janela = ler_json("window.json", {})
        self.TAMANHO = (janela["W"], janela["H"]) if janela.get("W", 0) > 400 and janela.get("H", 0) > 300 else (1280, 820)
        super().__init__(app)
        self.set_size_request(760, 520)
        if janela.get("Maximized"):
            self.maximize()
        css = Gtk.CssProvider()
        css.load_from_string("window.array headerbar { background: #1e1f23; color: #d6d8dc; box-shadow: none; min-height: 34px; }")
        Gtk.StyleContext.add_provider_for_display(Gdk.Display.get_default(), css,
                                                  Gtk.STYLE_PROVIDER_PRIORITY_APPLICATION + 1)
        self.add_css_class("array")
        self.connect("close-request", self.ao_fechar)
        self.trabalho = None                     # subprocesso de upscale em andamento (pra cancelar)
        archhost.Host._dinamicos["array"] = self.servir_dinamico   # app://array/upscale/... = resultados no disco

    # ---------- arquivos vindos de fora (linha de comando, Quilly, gerenciador de arquivos) ----------
    def abrir_arquivo_externo(self, caminho):
        try:
            self.enviar(ler_arquivo(caminho))
            self.lembrar(caminho)
        except Exception as e:  # noqa: BLE001
            self.enviar({"type": "error", "text": f"{os.path.basename(caminho)}: {e}"})

    def ao_carregar(self):
        if self.arquivo_inicial and os.path.isfile(self.arquivo_inicial):
            self.abrir_arquivo_externo(self.arquivo_inicial)

    def lembrar(self, caminho):
        rec = [c for c in self.settings.get("recent", []) if c != caminho]
        self.settings["recent"] = [caminho] + rec[:29]
        self.settings["folder"] = os.path.dirname(caminho)
        gravar_json("settings.json", self.settings)

    def pasta_inicial(self):
        p = self.settings.get("folder") or ""
        if os.path.isdir(p):
            return p
        return GLib.get_user_special_dir(GLib.UserDirectory.DIRECTORY_PICTURES) or os.path.expanduser("~")

    # ---------- menu do botão direito: sem os itens de navegador ----------
    def menu_contexto(self, _web, menu, _hit):
        tirar = {WebKit.ContextMenuAction.GO_BACK, WebKit.ContextMenuAction.GO_FORWARD, WebKit.ContextMenuAction.STOP,
                 WebKit.ContextMenuAction.RELOAD, WebKit.ContextMenuAction.INSPECT_ELEMENT,
                 WebKit.ContextMenuAction.OPEN_LINK_IN_NEW_WINDOW, WebKit.ContextMenuAction.DOWNLOAD_LINK_TO_DISK,
                 WebKit.ContextMenuAction.COPY_LINK_TO_CLIPBOARD, WebKit.ContextMenuAction.OPEN_IMAGE_IN_NEW_WINDOW,
                 WebKit.ContextMenuAction.DOWNLOAD_IMAGE_TO_DISK, WebKit.ContextMenuAction.COPY_IMAGE_URL_TO_CLIPBOARD,
                 WebKit.ContextMenuAction.OPEN_FRAME_IN_NEW_WINDOW}
        if os.environ.get("ARCH_DEV"):
            tirar.discard(WebKit.ContextMenuAction.INSPECT_ELEMENT)
        for item in list(menu.get_items()):
            if item.get_stock_action() in tirar:
                menu.remove(item)
        anterior_sep = True
        for item in list(menu.get_items()):
            sep = item.is_separator()
            if sep and anterior_sep:
                menu.remove(item)
            anterior_sep = sep
        n = menu.get_n_items()
        if n and menu.get_item_at_position(n - 1).is_separator():
            menu.remove(menu.get_item_at_position(n - 1))
        return menu.get_n_items() == 0        # nada sobrou: não mostra menu

    # ---------- fechar ----------
    def ao_fechar(self, *_):
        if not self.forcar_saida and self.carregado:
            self.enviar({"type": "confirm-exit"})       # o JS confere se há trabalho não salvo e responde
            return True
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
        if t == "title":
            self.set_title(msg.get("text") or "Array")
        elif t == "open-dialog":
            def escolheu(caminhos):
                for c in caminhos:
                    self.abrir_arquivo_externo(c)
            self.escolher_arquivo("Open", FILTROS_ABRIR, escolheu, varios=True, pasta_inicial=self.pasta_inicial())
        elif t == "save-dialog":
            self.dialogo_salvar(msg.get("name"))
        elif t == "save":
            self.salvar(msg)
        elif t == "reveal":
            if msg.get("path") and os.path.exists(msg["path"]):
                archhost.mostrar_na_pasta(msg["path"])
        elif t == "open-external":
            url = msg.get("url") or ""
            if url.startswith(("http://", "https://")):
                archhost.abrir_externo(url)
        elif t == "devtools":
            if os.environ.get("ARCH_DEV"):
                self.web.get_inspector().show()
        elif t == "exit-reply":
            if msg.get("force"):
                self.forcar_saida = True
                GLib.idle_add(lambda: (self.close(), False)[1])
        elif t == "quit":
            self.close()
        elif t == "upscale-engine?":
            self.enviar({"type": "upscale-engine", "ready": motor()[0] is not None, "size_mb": MOTOR_MB})
        elif t == "upscale-engine-install":
            threading.Thread(target=self.instalar_motor, daemon=True).start()
        elif t == "upscale":
            self.upscale(msg)
        elif t == "upscale-cancel":
            p = self.trabalho
            if p:
                p.kill()
        elif t == "upscale-cleanup":
            shutil.rmtree(os.path.join(CACHE, str(msg.get("job") or "")), ignore_errors=True)

    # ---------- upscale ----------
    def no_main(self, msg):
        """Mensagem pro JS a partir de outra thread: o GTK só aceita no loop principal."""
        GLib.idle_add(lambda: (self.enviar(msg), False)[1])

    def servir_dinamico(self, caminho):
        """O resultado do upscale vai pro JS como app://array/upscale/<job>/<id>.png: o WebKit lê o arquivo
        direto, sem passar pela ponte em base64."""
        if caminho.startswith("upscale/"):
            alvo = os.path.realpath(os.path.join(CACHE, caminho[len("upscale/"):]))
            if alvo.startswith(os.path.realpath(CACHE)) and os.path.isfile(alvo):
                with open(alvo, "rb") as f:
                    return f.read(), "image/png"
        return None

    def instalar_motor(self):
        """Baixa o release oficial do Real-ESRGAN (ncnn + Vulkan) pro perfil: só o binário e os modelos."""
        try:
            os.makedirs(CACHE, exist_ok=True)
            zip_ = os.path.join(CACHE, "realesrgan.zip")
            self.no_main({"type": "upscale-progress", "text": "Downloading the AI engine…"})
            req = urllib.request.Request(MOTOR_URL, headers={"User-Agent": "array/1.0"})
            with urllib.request.urlopen(req, timeout=120) as r, open(zip_, "wb") as f:
                total = int(r.headers.get("Content-Length") or 0)
                lido = 0
                while True:
                    bloco = r.read(1 << 18)
                    if not bloco:
                        break
                    f.write(bloco)
                    lido += len(bloco)
                    if total:
                        self.no_main({"type": "upscale-progress", "text": f"Downloading the AI engine… {lido * 100 // total}%"})
            self.no_main({"type": "upscale-progress", "text": "Unpacking…"})
            os.makedirs(MOTOR_DIR, exist_ok=True)
            with zipfile.ZipFile(zip_) as z:
                for nome in z.namelist():
                    if nome == "realesrgan-ncnn-vulkan" or (nome.startswith("models/") and nome.endswith((".bin", ".param"))):
                        z.extract(nome, MOTOR_DIR)
            os.chmod(os.path.join(MOTOR_DIR, "realesrgan-ncnn-vulkan"), 0o755)
            os.remove(zip_)
            self.no_main({"type": "upscale-engine", "ready": motor()[0] is not None, "size_mb": MOTOR_MB, "installed": True})
        except Exception as e:  # noqa: BLE001
            self.no_main({"type": "upscale-error", "text": f"could not install the AI engine: {e}"})

    def upscale(self, msg):
        """items: [{id, data (png base64)}]. Cada um vira um PNG no disco, passa pelo vips (Lanczos) ou pelo
        Real-ESRGAN (IA), e o resultado fica em CACHE/<job>/<id>.png (servido como app://array/upscale/...).
        O processo externo termina a cada camada: nada de modelo carregado entre um uso e outro."""
        job = str(msg.get("job") or int(time.time() * 1000))
        pasta = os.path.join(CACHE, job)
        itens = msg.get("items") or []
        metodo = msg.get("method") or "lanczos"
        escala = max(2, min(4, int(msg.get("scale") or 2)))
        modelo = MODELOS.get(msg.get("model") or "photo", "realesrgan-x4plus")

        def trabalho():
            try:
                os.makedirs(pasta, exist_ok=True)
                binario = modelos = None
                if metodo == "ai":
                    binario, modelos = motor()
                    if not binario:
                        raise RuntimeError("the AI engine is not installed")
                rotulo = "AI" if metodo == "ai" else "Lanczos"
                for i, item in enumerate(itens, 1):
                    entrada = os.path.join(pasta, f"{item['id']}-in.png")
                    saida = os.path.join(pasta, f"{item['id']}.png")
                    bruto = item.get("data") or ""
                    with open(entrada, "wb") as f:
                        f.write(base64.b64decode(bruto.split(",", 1)[1] if bruto.startswith("data:") else bruto))
                    self.no_main({"type": "upscale-progress", "text": f"{rotulo} · layer {i} of {len(itens)}…"})
                    if metodo == "ai":
                        cmd = [binario, "-i", entrada, "-o", saida, "-n", modelo, "-s", str(escala), "-m", modelos, "-f", "png"]
                    else:
                        cmd = ["vips", "resize", entrada, saida, str(escala), "--kernel", "lanczos3"]
                    p = subprocess.Popen(cmd, stdout=subprocess.DEVNULL, stderr=subprocess.PIPE, text=True, errors="replace")
                    self.trabalho = p
                    for linha in p.stderr:                       # o Real-ESRGAN imprime "12.50%" por linha
                        linha = linha.strip()
                        if linha.endswith("%") and linha[:-1].replace(".", "").isdigit():
                            self.no_main({"type": "upscale-progress", "text": f"{rotulo} · layer {i} of {len(itens)} · {linha}"})
                    rc = p.wait()
                    self.trabalho = None
                    os.remove(entrada)
                    if rc != 0 or not os.path.isfile(saida):
                        raise RuntimeError(f"{rotulo} failed (exit code {rc})")
                    self.no_main({"type": "upscaled", "job": job, "id": item["id"], "url": f"app://array/upscale/{job}/{item['id']}.png"})
                self.no_main({"type": "upscale-done", "job": job})
            except Exception as e:  # noqa: BLE001
                self.trabalho = None
                self.no_main({"type": "upscale-error", "job": job, "text": str(e)})
        threading.Thread(target=trabalho, daemon=True).start()

    def salvar(self, msg):
        """{path, name, data (base64)}: grava e avisa. O caminho vem do save-dialog (ou é o do arquivo aberto)."""
        caminho = msg.get("path") or ""
        bruto = msg.get("data") or ""
        if not caminho:
            self.enviar({"type": "error", "text": "no file name"})
            return
        try:
            dados = base64.b64decode(bruto.split(",", 1)[1] if bruto.startswith("data:") else bruto)
        except ValueError:
            self.enviar({"type": "error", "text": "could not decode the image data"})
            return
        try:
            with open(caminho + ".tmp", "wb") as f:
                f.write(dados)
            os.replace(caminho + ".tmp", caminho)
        except OSError as e:
            self.enviar({"type": "error", "text": f"could not save: {e}"})
            return
        self.lembrar(caminho)
        self.enviar({"type": "saved", "path": caminho, "name": os.path.basename(caminho), "silent": bool(msg.get("silent"))})

    def dialogo_salvar(self, nome):
        """Pergunta onde salvar; sem extensão no nome escolhido, usa a do nome sugerido."""
        nome = nome or "untitled.array"
        ext = os.path.splitext(nome)[1].lower() or ".array"
        filtros = sorted(FILTROS_SALVAR, key=lambda f: not any(p.endswith(ext) for p in f[1]))   # o tipo do nome primeiro

        def escolheu(caminho):
            if not caminho:
                self.enviar({"type": "save-cancel"})
                return
            if not os.path.splitext(caminho)[1]:
                caminho += ext
            self.enviar({"type": "save-path", "path": caminho})
        self.salvar_como("Save", nome, filtros, escolheu, pasta_inicial=self.pasta_inicial())


class App(archhost.App):
    def do_activate(self):
        (self.props.active_window or Janela(self)).present()

    def do_open(self, arquivos, _n, _dica):
        j = self.props.active_window
        if not j:
            j = Janela(self, arquivos[0].get_path() if arquivos else None)
            j.present()
            arquivos = arquivos[1:]
        else:
            j.present()
        for f in arquivos:
            if f.get_path():
                j.abrir_arquivo_externo(f.get_path())


if __name__ == "__main__":
    archhost.Adw.init()
    App("com.archquilly.Array", Janela).run(sys.argv)
