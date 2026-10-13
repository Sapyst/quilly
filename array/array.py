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
import re
import secrets
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
# RAW de câmera: o LibRaw (dcraw_emu) revela, o vips aplica a cor; processos externos, nada fica residente
RAW = (".cr2", ".cr3", ".crw", ".nef", ".nrw", ".arw", ".srf", ".sr2", ".dng", ".raf", ".orf", ".rw2", ".pef",
       ".srw", ".3fr", ".iiq", ".erf", ".mef", ".mos", ".kdc", ".dcr", ".x3f")
LIMITE = 200 * 1024 * 1024
# ---------- upscale: processos externos, nada residente (usou, fez, descarregou) ----------
CACHE = os.path.join(GLib.get_user_cache_dir(), "arch-quilly", "array", "upscale")     # disco, não /tmp (que é RAM)
CACHE_RAW = os.path.join(GLib.get_user_cache_dir(), "arch-quilly", "array", "raw")      # previews e a foto revelada
MOTOR_DIR = os.path.join(PERFIL, "realesrgan")
MOTOR_URL = "https://github.com/xinntao/Real-ESRGAN/releases/download/v0.2.5.0/realesrgan-ncnn-vulkan-20220424-ubuntu.zip"
MOTOR_MB = 45
MODELOS = {"photo": "realesrgan-x4plus", "art": "realesrgan-x4plus-anime"}
FILTROS_ABRIR = [("Images, camera RAW and Array documents", ["*.array", "*.json"] + ["*" + e for e in IMAGENS + RAW]),
                 ("Array document", ["*.array"]),
                 ("Images", ["*" + e for e in IMAGENS]),
                 ("Camera RAW", ["*" + e for e in RAW]),
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


# ---------- Camera Raw: ajustes -> dcraw_emu | vips ----------
AMBIENTE_VIPS = {**os.environ, "VIPS_WARNING": "0"}
REALCES = {"Clip": 0, "Unclip": 1, "Blend": 2, "Rebuild": 5}
FBDD = {"Off": 0, "Light": 1, "Full": 2}
DEMOSAIC = {"AHD": 3, "DHT": 11, "AAHD": 12, "DCB": 4, "VNG": 1, "PPG": 2, "Linear": 0}


def raw_args(aj, meio):
    """Ajustes do Camera Raw -> opções do dcraw_emu. meio=True: meio tamanho (o preview e o 'open at half size')."""
    a = ["-a" if aj.get("wb") == "Auto" else "-w"]            # balanço de branco: automático ou o da câmera
    if not aj.get("auto_bright", True):
        a.append("-W")                                         # sem o brilho automático do dcraw
    ev = float(aj.get("exposure") or 0)
    if abs(ev) >= 0.01:
        a += ["-b", f"{2 ** ev:.4f}"]                          # exposição em EV (entra depois do ponto branco)
    a += ["-H", str(REALCES.get(aj.get("highlights"), 0))]
    ruido = int(float(aj.get("noise") or 0))
    if ruido > 0:
        a += ["-n", str(ruido)]                                # redução de ruído wavelet
    fbdd = FBDD.get(aj.get("fbdd"), 0)
    if fbdd:
        a += ["-fbdd", str(fbdd)]
    cr, cb = int(float(aj.get("ca_red") or 0)), int(float(aj.get("ca_blue") or 0))
    if cr or cb:
        a += ["-C", f"{1 + cr / 10000:.5f}", f"{1 + cb / 10000:.5f}"]    # aberração cromática (escala R e B)
    if meio:
        a.append("-h")
    else:
        a += ["-q", str(DEMOSAIC.get(aj.get("demosaic"), 3))]
    return a


def raw_ganhos(aj):
    """Temperatura, tint e contraste viram um 'vips linear' (saída = a*entrada + b por canal) na mesma passada."""
    f = 2 ** (float(aj.get("temperature") or 0) / 125)          # quente: mais R, menos B
    g = 2 ** (-float(aj.get("tint") or 0) / 150)                # magenta: menos G
    c = 2 ** (float(aj.get("contrast") or 0) / 100)             # contraste em torno do cinza médio
    a = [c * f, c * g, c / f]
    b = 128 * (1 - c)
    return " ".join(f"{v:.4f}" for v in a), " ".join([f"{b:.2f}"] * 3)


def raw_info(caminho):
    """Cabeçalho do Camera Raw pelo raw-identify: câmera, ISO, exposição, abertura, focal e tamanho (já rodado)."""
    info = {}
    try:
        saida = subprocess.run(["raw-identify", "-v", caminho], capture_output=True, text=True, timeout=20).stdout
    except Exception:  # noqa: BLE001
        return info
    for linha in saida.splitlines():
        chave, _, valor = linha.strip().partition(":")
        valor = valor.strip()
        if chave == "Camera" and "camera" not in info:
            info["camera"] = valor.split(" ID:")[0].strip()
        elif chave == "ISO speed" and valor not in ("", "0"):
            info["iso"] = valor
        elif chave == "Shutter":
            info["shutter"] = re.sub(r"\.0$", "", valor)
        elif chave == "Aperture" and not valor.endswith("/0.0"):
            info["aperture"] = valor
        elif chave == "CurFocal" and "focal" not in info and not valor.startswith("0.0"):
            info["focal"] = re.sub(r"\.0 mm$", " mm", valor)
        elif chave in ("Image size", "Output size"):           # Output size vem depois, já com a rotação
            numeros = re.findall(r"\d+", valor)
            if len(numeros) >= 2:
                info["width"], info["height"] = int(numeros[0]), int(numeros[1])
    return info


def conferir_arquivo(caminho):
    """-> (caminho absoluto, kind 'json'|'image', mime). Levanta ValueError se não for coisa do Array."""
    caminho = os.path.abspath(caminho)
    ext = os.path.splitext(caminho)[1].lower()
    if os.path.getsize(caminho) > LIMITE:
        raise ValueError("file is too big (over 200 MB)")
    if ext in DOCUMENTOS:
        return caminho, "json", "application/json"
    if ext in RAW:
        if not shutil.which("dcraw_emu") or not shutil.which("vips"):
            raise ValueError("camera RAW needs LibRaw (dcraw_emu) and libvips installed")
        return caminho, "raw", "image/x-dcraw"
    mime = mimetypes.guess_type(caminho)[0] or "image/png"
    if not mime.startswith("image/"):
        raise ValueError("Array opens images and .array documents")
    return caminho, "image", mime


class Janela(archhost.Host):
    NOME = "array"
    PASTA_UI = os.path.join(AQUI, "ui")
    TITULO = "Array"
    SEM_BORDA = False
    FUNDO = "#141518"
    MEMORIA_MB = 1024                # o WebKit solta caches a partir de ~512 MB e nunca passa de 1 GB

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
        self.servidos = {}                       # token -> (caminho, mime): arquivos que a página pode ler (app://array/file/<token>)
        self.conhecidos = set()                  # caminhos abertos/salvos nesta sessão (só esses ganham token sem diálogo)
        self.silenciosos = set()                 # caminhos sendo salvos por Ctrl+S (aviso discreto, não toast)
        archhost.Host._dinamicos["array"] = self.servir_dinamico   # app://array/file/... e upscale/...: lidos do disco, em fluxo
        self.esperando = {}                      # caminho -> callback quando o download terminar (salvar, camadas do upscale)
        self.raw_pedidos = {}                    # job -> último pedido de preview (os anteriores caem)
        self.raw_ocupado = set()                 # jobs com um preview rodando
        self.raw_cancelados = set()
        self.raw_lock = threading.Lock()

    # ---------- arquivos vindos de fora (linha de comando, Quilly, gerenciador de arquivos) ----------
    def abrir_arquivo_externo(self, caminho):
        """A página recebe só um endereço interno; o WebKit lê o arquivo do disco em fluxo (nada em base64)."""
        try:
            caminho, kind, mime = conferir_arquivo(caminho)
            if kind == "raw":                  # vai pro Camera Raw da página; o RAW em si nunca é gravado
                self.enviar({"type": "open", "kind": "raw", "path": caminho, "name": os.path.basename(caminho),
                             "info": raw_info(caminho)})
                self.lembrar(caminho)
                return
            token = secrets.token_urlsafe(12)
            self.servidos[token] = (caminho, mime)
            self.conhecidos.add(caminho)
            self.enviar({"type": "open", "kind": kind, "path": caminho, "name": os.path.basename(caminho),
                         "url": f"app://array/file/{token}"})
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
        elif t == "save-token":                  # Ctrl+S no arquivo já conhecido: grava por cima, sem diálogo
            caminho = msg.get("path") or ""
            if caminho in self.conhecidos:
                self.silenciosos.add(caminho)
                self.enviar({"type": "save-path", "path": caminho, "token": self.token_para(caminho), "silent": True})
            else:
                self.dialogo_salvar(os.path.basename(caminho) or msg.get("name"))
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
        elif t == "upscale-tokens":              # a página vai gravar as camadas (downloads) em CACHE/<job>/<id>-in.png
            job = str(msg.get("job") or "")
            if not job.isdigit():
                return
            os.makedirs(os.path.join(CACHE, job), exist_ok=True)
            tokens = {}
            for ident in msg.get("ids") or []:
                caminho = os.path.join(CACHE, job, f"{int(ident)}-in.png")
                tokens[str(ident)] = self.baixar_para(caminho)
                self.esperando[caminho] = lambda i=ident, j=job: self.enviar({"type": "upscale-in-done", "job": j, "id": i})
            self.enviar({"type": "upscale-tokens", "job": job, "tokens": tokens})
        elif t == "raw-preview":
            self.raw_preview(msg)
        elif t == "raw-open":
            threading.Thread(target=self.raw_rodar, args=(msg, True), daemon=True).start()
        elif t in ("raw-cancel", "raw-cleanup"):
            self.raw_limpar(str(msg.get("job") or ""))
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
        """app://array/file/<token> (arquivo aberto), app://array/upscale/<job>/<id>.png (resultado) e
        app://array/raw/<job>/... (previews e a foto revelada do Camera Raw): o WebKit lê
        do disco em fluxo; a ponte nunca carrega imagem."""
        if caminho.startswith("file/"):
            r = self.servidos.get(caminho[len("file/"):])
            if r and os.path.isfile(r[0]):
                return r
        if caminho.startswith("upscale/"):
            alvo = os.path.realpath(os.path.join(CACHE, caminho[len("upscale/"):]))
            if alvo.startswith(os.path.realpath(CACHE)) and os.path.isfile(alvo):
                return alvo, "image/png"
        if caminho.startswith("raw/"):
            alvo = os.path.realpath(os.path.join(CACHE_RAW, caminho[len("raw/"):]))
            if alvo.startswith(os.path.realpath(CACHE_RAW)) and os.path.isfile(alvo):
                return alvo, "image/png" if alvo.endswith(".png") else "image/jpeg"
        return None

    # ---------- Camera Raw: um processo por pedido (dcraw_emu | vips); só o pedido mais novo de cada job roda ----------
    def raw_preview(self, msg):
        job = str(msg.get("job") or "")
        with self.raw_lock:
            self.raw_pedidos[job] = msg
            if job in self.raw_ocupado:
                return
            self.raw_ocupado.add(job)
            self.raw_cancelados.discard(job)
        threading.Thread(target=self._raw_fila, args=(job,), daemon=True).start()

    def _raw_fila(self, job):
        while True:
            with self.raw_lock:
                msg = self.raw_pedidos.pop(job, None)
                if msg is None:
                    self.raw_ocupado.discard(job)
                    return
            self.raw_rodar(msg, False)

    def raw_rodar(self, msg, abrir):
        """Revela o RAW em CACHE_RAW/<job>/: preview em meio tamanho (JPEG) ou a foto final (PNG, 'open as image')."""
        job, seq = str(msg.get("job") or ""), int(msg.get("seq") or 0)
        caminho, ajustes = msg.get("path") or "", msg.get("settings") or {}
        pasta = os.path.join(CACHE_RAW, job)
        try:
            if not job or not os.path.isfile(caminho) or os.path.splitext(caminho)[1].lower() not in RAW:
                raise RuntimeError("not a camera RAW file")
            os.makedirs(pasta, exist_ok=True)
            nome = "full.png" if abrir else f"p{seq}.jpg"
            saida = os.path.join(pasta, nome)
            if abrir:
                self.no_main({"type": "raw-progress", "job": job, "text": f"Developing {os.path.basename(caminho)}…"})
            t0 = time.time()
            a, b = raw_ganhos(ajustes)
            p1 = subprocess.Popen(["dcraw_emu", *raw_args(ajustes, meio=not abrir or bool(ajustes.get("half"))),
                                   "-Z", "-", caminho], stdout=subprocess.PIPE, stderr=subprocess.PIPE)
            p2 = subprocess.run(["vips", "linear", "--uchar", "--", "stdin",
                                 saida + ("[compression=1]" if abrir else "[Q=88]"), a, b],
                                stdin=p1.stdout, capture_output=True, text=True, env=AMBIENTE_VIPS)
            p1.stdout.close()
            erro1 = p1.stderr.read().decode(errors="replace")
            p1.wait()
            if p1.returncode or p2.returncode or not os.path.isfile(saida):
                raise RuntimeError((erro1.strip() or p2.stderr.strip() or "decode failed").splitlines()[-1][:200])
            if job in self.raw_cancelados:                      # cancelado enquanto rodava: não deixa nada no disco
                shutil.rmtree(pasta, ignore_errors=True)
                return
            ms = int((time.time() - t0) * 1000)
            if abrir:
                self.no_main({"type": "raw-done", "job": job, "url": f"app://array/raw/{job}/{nome}", "ms": ms})
                return
            for velho in os.listdir(pasta):                     # só os dois últimos previews ficam no disco
                if velho.startswith("p") and velho.endswith(".jpg") and velho != nome:
                    try:
                        if int(velho[1:-4]) < seq - 1:
                            os.remove(os.path.join(pasta, velho))
                    except (ValueError, OSError):
                        pass
            self.no_main({"type": "raw-preview", "job": job, "seq": seq, "url": f"app://array/raw/{job}/{nome}", "ms": ms})
        except Exception as e:  # noqa: BLE001
            self.no_main({"type": "raw-error", "job": job, "seq": seq, "text": str(e)})

    def raw_limpar(self, job):
        with self.raw_lock:
            self.raw_pedidos.pop(job, None)
            self.raw_cancelados.add(job)
        pasta = os.path.realpath(os.path.join(CACHE_RAW, job))
        if job and pasta.startswith(os.path.realpath(CACHE_RAW)) and os.path.isdir(pasta):
            shutil.rmtree(pasta, ignore_errors=True)

    def ao_gravar(self, caminho, erro):
        """Um download pedido pela página terminou: salvar (avisa a página) ou camada do upscale (conta)."""
        cb = self.esperando.pop(caminho, None)
        if erro:
            self.enviar({"type": "error", "text": f"could not write {os.path.basename(caminho or '')}: {erro}"})
            return
        if cb:
            cb()
        elif caminho:
            self.lembrar(caminho)
            self.conhecidos.add(caminho)
            self.enviar({"type": "saved", "path": caminho, "name": os.path.basename(caminho), "silent": caminho in self.silenciosos})
            self.silenciosos.discard(caminho)

    def token_para(self, caminho):
        return self.baixar_para(caminho)

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
                    if item.get("data"):                       # caminho antigo (base64); o normal é já estar no disco
                        bruto = item["data"]
                        with open(entrada, "wb") as f:
                            f.write(base64.b64decode(bruto.split(",", 1)[1] if bruto.startswith("data:") else bruto))
                    if not os.path.isfile(entrada):
                        raise RuntimeError("layer data missing")
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
            self.enviar({"type": "save-path", "path": caminho, "token": self.token_para(caminho)})
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
