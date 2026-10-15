#!/usr/bin/env python3
"""
Quilly (Arch Quilly) — a central da suíte: abre o Noty, o Artry, o Volury, o Fotry e o Takery e junta os
documentos recentes de todos eles na aba Arquivos.

Port do Quilly de Windows (C# + WebView2). A interface (ui/) é a original, com os textos de Windows
adaptados. Instalar e atualizar: o Quilly lê a release mais recente do GitHub (Sapyst/quilly), mostra
"Atualizar vX.Y" quando há versão nova, baixa o pacote, confere o SHA-256 e abre um terminal com
`sudo pacman -U` (atualizador.py). Publicou release = todo Quilly vê a atualização.

Ponte: {type:'rpc', id, method, args} -> {type:'rpc-reply', id, ok, data, error} (igual ao original;
AppInfo/FileItem em PascalCase porque a interface depende disso).
Dados: ~/.local/share/arch-quilly/quilly/recent_files.json (lista de caminhos, mais recente primeiro).
"""
import json
import os
import shutil
import subprocess
import sys
import threading
import time

AQUI = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, AQUI)
import archhost  # noqa: E402
import atualizador  # noqa: E402
from gi.repository import GLib  # noqa: E402

PERFIL = archhost.dados("quilly")
DADOS = os.path.dirname(PERFIL)                         # ~/.local/share/arch-quilly
RAIZ_INSTALACAO = os.path.dirname(AQUI)                 # /usr/share/arch-quilly (ou a pasta do projeto)

APPS = [
    {"Id": "artry", "Name": "Artry", "Subtitle": "Editor de documentos e código",
     "Description": "Editor minimalista para escrever, editar documentos ricos e mexer em código, com régua "
                    "coordenada e modo página.",
     "Icon": "assets/artry.png", "Formats": [".hyp", ".artry", ".docx", ".rtf", ".txt"]},
    {"Id": "noty", "Name": "Noty", "Subtitle": "Notas e Markdown com vidro",
     "Description": "Editor de texto simples e bonito para notas, Markdown e documentos Hyper com imagens "
                    "embutidas.",
     "Icon": "assets/noty.png", "Formats": [".hyp", ".md", ".txt", ".html"]},
    {"Id": "volury", "Name": "Volury", "Subtitle": "Editor de e-books EPUB",
     "Description": "Monte e-books EPUB 2/3: capítulos, imagens, estilos, sumário e validação. Importa .hyp.",
     "Icon": "assets/volury.png", "Formats": [".epub", ".hyp"]},
    {"Id": "fotry", "Name": "Fotry", "Subtitle": "Editor de imagens nativo",
     "Description": "Fotos e arte com camadas, máscaras, ajustes, estilos, texto, PSD, RAW de câmera e Upscale com IA. "
                    "Nativo, desenhado pela GPU, baseado no PhotoCraft.",
     "Icon": "assets/fotry.png", "Formats": [".pcraft", ".psd", ".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp", ".tiff",
                 ".cr2", ".cr3", ".nef", ".arw", ".dng", ".raf", ".orf", ".rw2", ".pef"]},
    {"Id": "takery", "Name": "Takery", "Subtitle": "Editor de vídeo com legenda automática",
     "Description": "Edição de vídeo em linha do tempo, cor, áudio, títulos e legendas automáticas pela GPU "
                    "(Whisper). Nativo, com aceleração por hardware, baseado no FilmCraft.",
     "Icon": "assets/takery.png", "Formats": [".fcproj", ".mp4", ".mov", ".mkv", ".webm", ".mxf", ".srt", ".vtt"]},
]
RECOMENDADO = {".hyp": ("Noty / Artry", "noty"), ".artry": ("Artry", "artry"), ".docx": ("Artry", "artry"),
               ".rtf": ("Artry", "artry"), ".md": ("Noty", "noty"), ".txt": ("Noty", "noty"),
               ".html": ("Noty", "noty"), ".epub": ("Volury", "volury"),
               ".pcraft": ("Fotry", "fotry"), ".psd": ("Fotry", "fotry"), ".png": ("Fotry", "fotry"), ".jpg": ("Fotry", "fotry"),
               ".jpeg": ("Fotry", "fotry"), ".webp": ("Fotry", "fotry"), ".gif": ("Fotry", "fotry"),
               ".bmp": ("Fotry", "fotry"), ".tif": ("Fotry", "fotry"), ".tiff": ("Fotry", "fotry"),
               ".cr2": ("Fotry", "fotry"), ".cr3": ("Fotry", "fotry"), ".nef": ("Fotry", "fotry"),
               ".arw": ("Fotry", "fotry"), ".dng": ("Fotry", "fotry"), ".raf": ("Fotry", "fotry"),
               ".orf": ("Fotry", "fotry"), ".rw2": ("Fotry", "fotry"), ".pef": ("Fotry", "fotry"),
               ".fcproj": ("Takery", "takery"), ".mp4": ("Takery", "takery"), ".mov": ("Takery", "takery"),
               ".mkv": ("Takery", "takery"), ".webm": ("Takery", "takery"), ".mxf": ("Takery", "takery")}


def comando_do_app(app_id):
    """Instalado: /usr/bin/<app>. Rodando da pasta do projeto: python3 ../<app>/<app>.py."""
    no_sistema = shutil.which(app_id)
    if no_sistema:
        return [no_sistema]
    script = os.path.join(RAIZ_INSTALACAO, app_id, f"{app_id}.py")
    return [sys.executable, script] if os.path.exists(script) else None


def versao_do_app(app_id):
    try:
        return open(os.path.join(RAIZ_INSTALACAO, app_id, "VERSAO")).read().strip()
    except OSError:
        return "1.0.0"


def info_app(a, tem=None):
    """O cartão de um app: instalado?, versão instalada e a do GitHub (atualizador.em_cache)."""
    tem = atualizador.instalados() if tem is None else tem
    cmd = comando_do_app(a["Id"])
    v = tem.get("arch-quilly") or tem.get(f"arch-{a['Id']}") or versao_do_app(a["Id"])
    remoto = atualizador.em_cache()
    rv = remoto["versao"] if remoto else v
    return dict(a, Installed=bool(cmd), UpdateAvailable=bool(cmd and remoto and atualizador.mais_nova(rv, v)),
                InstalledPath=" ".join(cmd or []), SourcePath="", InstalledSize=0, SourceSize=0,
                InstalledDate=None, SourceDate=None, InstalledVersion=v, SourceVersion=rv)


def lista_apps():
    tem = atualizador.instalados()
    return [info_app(x, tem) for x in APPS]


def ler_json(caminho, padrao):
    try:
        with open(caminho, encoding="utf-8") as f:
            return json.load(f)
    except (OSError, ValueError):
        return padrao


def recentes_proprios():
    r = ler_json(os.path.join(PERFIL, "recent_files.json"), [])
    return [p for p in r if isinstance(p, str)]


def gravar_recentes(lista):
    with open(os.path.join(PERFIL, "recent_files.json"), "w", encoding="utf-8") as f:
        json.dump(lista[:50], f, indent=2, ensure_ascii=False)


def adicionar_recente(caminho):
    lista = [p for p in recentes_proprios() if p != caminho and os.path.exists(p)]
    gravar_recentes([caminho] + lista)


def tamanho(b):
    for u in ("B", "KB", "MB", "GB"):
        if b < 1024 or u == "GB":
            return f"{b:.0f} {u}" if u == "B" else f"{b:.1f} {u}"
        b /= 1024


def chave_recursiva(obj, chave):
    """A primeira lista de nome `chave` em qualquer nível de um JSON (as preferências do Takery)."""
    if isinstance(obj, dict):
        if isinstance(obj.get(chave), list):
            return obj[chave]
        for v in obj.values():
            r = chave_recursiva(v, chave)
            if r:
                return r
    return []


def arquivos_recentes():
    """Quilly + Noty (ui.recent) + Artry (RecentFiles) + Fotry + Takery: os que existem, mais recentes primeiro, até 30."""
    caminhos = list(recentes_proprios())
    caminhos += ler_json(os.path.join(DADOS, "noty", "settings.json"), {}).get("ui", {}).get("recent", []) or []
    caminhos += ler_json(os.path.join(DADOS, "artry", "config.json"), {}).get("RecentFiles", []) or []
    caminhos += ler_json(os.path.join(DADOS, "array", "settings.json"), {}).get("recent", []) or []      # Array 1
    config = os.environ.get("XDG_CONFIG_HOME") or os.path.expanduser("~/.config")                         # Fotry
    for pasta in ("fotry", "array"):
        caminhos += (ler_json(os.path.join(config, "arch-quilly", pasta, "preferences.json"), {})
                     .get("fileHandling", {}).get("recentFiles", []) or [])
    caminhos += chave_recursiva(ler_json(os.path.join(DADOS, "takery", "preferences.json"), {}), "recentProjects")  # Takery
    vistos, itens = set(), []
    for p in caminhos:
        if not isinstance(p, str) or p in vistos or not os.path.isfile(p):
            continue
        vistos.add(p)
        st = os.stat(p)
        ext = os.path.splitext(p)[1].lower()
        rec, rec_id = RECOMENDADO.get(ext, ("App padrão", ""))
        itens.append((st.st_mtime, {"Path": p, "Name": os.path.basename(p), "Extension": ext,
                                    "SizeFormatted": tamanho(st.st_size),
                                    "ModifiedFormatted": time.strftime("%d/%m/%Y %H:%M", time.localtime(st.st_mtime)),
                                    "RecommendedApp": rec, "RecommendedAppId": rec_id}))
    itens.sort(key=lambda x: -x[0])
    return [i for _, i in itens[:30]]


def lancar(app_id, arquivo=None):
    cmd = comando_do_app(app_id)
    if not cmd:
        nome = next((a["Name"] for a in APPS if a["Id"] == app_id), app_id)
        raise RuntimeError(f"O {nome} não está instalado. Instale com o pacote arch-quilly (ou arch-{app_id}).")
    subprocess.Popen(cmd + ([arquivo] if arquivo else []), start_new_session=True,
                     stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)


def abrir_com(caminho, app_id):
    if not os.path.isfile(caminho):
        raise FileNotFoundError(f"Arquivo não encontrado: {caminho}")
    adicionar_recente(caminho)
    if app_id in ("noty", "artry", "volury", "fotry", "takery"):
        lancar(app_id, caminho)
        return
    ext = os.path.splitext(caminho)[1].lower()
    if ext in RECOMENDADO:
        for candidato in (RECOMENDADO[ext][1], "artry", "noty"):
            if comando_do_app(candidato):
                lancar(candidato, caminho)
                return
    archhost.abrir_externo(caminho)


def criar_atalho(app_id):
    """Atalho do app na Área de Trabalho (pasta XDG Desktop): uma cópia do .desktop dele."""
    origem = None
    for d in [os.path.join(GLib.get_user_data_dir(), "applications")] + [os.path.join(x, "applications") for x in GLib.get_system_data_dirs()]:
        c = os.path.join(d, f"com.archquilly.{app_id.capitalize()}.desktop")
        if os.path.exists(c):
            origem = c
            break
    area = GLib.get_user_special_dir(GLib.UserDirectory.DIRECTORY_DESKTOP) or os.path.expanduser("~/Desktop")
    os.makedirs(area, exist_ok=True)
    destino = os.path.join(area, f"{app_id.capitalize()}.desktop")
    if origem:
        shutil.copy(origem, destino)
    else:
        cmd = comando_do_app(app_id)
        if not cmd:
            raise RuntimeError("O app não está instalado.")
        open(destino, "w").write(f"[Desktop Entry]\nType=Application\nName={app_id.capitalize()}\n"
                                 f"Exec={' '.join(cmd)} %f\nTerminal=false\n")
    os.chmod(destino, 0o755)


class Janela(archhost.Host):
    NOME = "quilly"
    PASTA_UI = os.path.join(AQUI, "ui")
    TITULO = "Quilly Suite"
    TAMANHO = (1120, 740)
    SEM_BORDA = False
    FUNDO = "#14171A"

    def __init__(self, app):
        super().__init__(app)
        self.set_size_request(960, 620)
        self.instalando = False
        self.consultar_github(forcar=True)

    def consultar_github(self, forcar=False):
        """Lê a release mais recente em segundo plano e avisa a interface (os cartões se refazem)."""
        def trabalho():
            antes = atualizador.em_cache()
            depois = atualizador.consultar(forcar)
            if depois and depois != antes:
                GLib.idle_add(self.enviar, {"type": "apps_refresh"})
        threading.Thread(target=trabalho, daemon=True).start()

    def instalar(self, app_id):
        """Baixa (com progresso no cartão), confere e abre o terminal do pacman; ao fechar, refaz a lista."""
        def progresso(pct, texto):
            GLib.idle_add(self.enviar, {"type": "install_progress", "appId": app_id, "percent": pct, "status": texto})

        def trabalho():
            try:
                progresso(0, "Consultando o GitHub…")
                remoto, nomes = atualizador.plano(app_id)
                arquivos = []
                for i, nome in enumerate(nomes):
                    def p(lido, total, i=i):
                        fr = (i + (lido / total if total else 0)) / len(nomes)
                        progresso(int(fr * 95), f"Baixando {remoto['versao']}… {lido // 1_000_000} de {total // 1_000_000} MB")
                    arquivos.append(atualizador.baixar(remoto, nome, p))
                progresso(97, "Confirme a instalação no terminal")
                proc = atualizador.instalar_no_terminal(arquivos)
                proc.wait()
                progresso(100, "Concluído")
            except Exception as e:  # noqa: BLE001
                GLib.idle_add(self.enviar, {"type": "install_error", "appId": app_id, "error": str(e)})
            finally:
                self.instalando = False
                GLib.idle_add(self.enviar, {"type": "apps_refresh"})
        if self.instalando:
            raise RuntimeError("Já tem uma instalação em andamento.")
        self.instalando = True
        threading.Thread(target=trabalho, daemon=True).start()

    def abrir_arquivo_externo(self, caminho):
        """Arquivo passado pro Quilly: abre no app recomendado (como no original)."""
        try:
            abrir_com(caminho, "")
        except Exception as e:  # noqa: BLE001
            print("quilly:", e, file=sys.stderr)

    def ao_receber(self, msg):
        if not isinstance(msg, dict) or msg.get("type") != "rpc":
            return
        id_, metodo, a = msg.get("id"), msg.get("method"), msg.get("args") or {}
        try:
            dado = self.rpc(metodo, a)
            self.enviar({"type": "rpc-reply", "id": id_, "ok": True, "data": dado, "error": None})
        except Exception as e:  # noqa: BLE001
            self.enviar({"type": "rpc-reply", "id": id_, "ok": False, "data": None, "error": str(e)})

    def rpc(self, m, a):
        if m == "apps.list":
            self.consultar_github()                      # usa o cache; a cada 15 min consulta de novo
            return lista_apps()
        if m == "apps.checkUpdates":
            self.consultar_github(forcar=True)
            return {"success": True}
        if m == "apps.install":
            app = next((x for x in APPS if x["Id"] == a.get("appId")), None)
            if not app:
                raise RuntimeError("App desconhecido.")
            self.instalar(app["Id"])                     # instalar ou atualizar: o progresso vem por install_progress
            return {"success": True, "started": True}
        if m == "apps.uninstall":
            raise RuntimeError("Os apps da suíte são removidos pelo pacman (sudo pacman -R arch-quilly).")
        if m == "apps.launch":
            lancar(a.get("appId"), a.get("filePath") if a.get("filePath") and os.path.isfile(a["filePath"]) else None)
            return {"success": True}
        if m == "apps.openFolder":
            pasta = archhost.dados(a.get("appId") or "quilly")
            archhost.abrir_externo(pasta)
            return {"success": True}
        if m == "apps.createShortcut":
            criar_atalho(a.get("appId"))
            return {"success": True}
        if m == "files.list":
            return arquivos_recentes()
        if m == "files.open":
            abrir_com(a.get("path") or "", a.get("appId") or "")
            return {"success": True}
        if m == "files.showInExplorer":
            archhost.mostrar_na_pasta(a.get("path") or "")
            return {"success": True}
        if m == "files.remove":
            p = a.get("path")
            gravar_recentes([x for x in recentes_proprios() if x != p])
            # também some das listas do Noty e do Artry (senão volta na próxima atualização)
            for arq, chave in ((os.path.join(DADOS, "noty", "settings.json"), ("ui", "recent")),
                               (os.path.join(DADOS, "artry", "config.json"), ("RecentFiles",))):
                d = ler_json(arq, None)
                if not isinstance(d, dict):
                    continue
                alvo = d
                for k in chave[:-1]:
                    alvo = alvo.get(k, {})
                if isinstance(alvo.get(chave[-1]), list) and p in alvo[chave[-1]]:
                    alvo[chave[-1]] = [x for x in alvo[chave[-1]] if x != p]
                    with open(arq, "w", encoding="utf-8") as f:
                        json.dump(d, f, indent=2, ensure_ascii=False)
            return {"success": True, "files": arquivos_recentes()}
        if m == "suite.getFolder":
            return {"path": DADOS}
        if m == "suite.openFolder":
            archhost.abrir_externo(DADOS)
            return {"success": True}
        if m == "suite.chooseFolder":
            archhost.abrir_externo(RAIZ_INSTALACAO)
            return None
        if m == "system.devtools":
            if os.environ.get("ARCH_DEV"):
                self.web.get_inspector().show()
            return {"ok": True}
        raise RuntimeError(f"Método não implementado: {m}")


if __name__ == "__main__":
    archhost.rodar("com.archquilly.Quilly", Janela)
