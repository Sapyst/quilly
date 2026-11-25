"""
atualizador.py — o Quilly instala e atualiza a suíte direto do GitHub (Sapyst/quilly e repositórios individuais).

Modos suportados automaticamente conforme o sistema operacional (Linux / Windows) e ferramentas disponíveis:
  1. Compilação direta do Git por sub-app (Fotry, Takery, Kromar):
     - Puxa o código mais recente direto do repositório Git de cada app (Sapyst/fotry, Sapyst/takery, Sapyst/kromar).
     - Compila nativamente para o sistema atual (binário ELF no Linux; .exe no Windows).
     - Faz backup automático de segurança das configurações do usuário antes de trocar o binário,
       substitui apenas o executável velho pelo novo e mantém todas as configs/catálogos intactos.
     - Detecta commits novos em cada repositório para avisar e atualizar individualmente.
  2. Pacotes pré-compilados da Release (Arch Linux — .pkg.tar.zst + SHA256SUMS):
     - Usado para a base (arch-quilly), apps Python (Artry, Volury) ou quando o usuário não tem
       Rust/Git instalados na máquina.
"""
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import threading
import time
import urllib.request

IS_WINDOWS = os.name == "nt" or sys.platform.startswith("win")
EXE_EXT = ".exe" if IS_WINDOWS else ""

REPO = "Sapyst/quilly"
API = f"https://api.github.com/repos/{REPO}/releases/latest"
AGENTE = "arch-quilly-updater"

if IS_WINDOWS:
    _LOCAL = os.environ.get("LOCALAPPDATA") or os.path.expanduser("~\\AppData\\Local")
    _ROAMING = os.environ.get("APPDATA") or os.path.expanduser("~\\AppData\\Roaming")
    CACHE = os.path.join(_LOCAL, "Quilly", "cache", "atualizacoes")
    SRC_CACHE = os.path.join(_LOCAL, "Quilly", "cache", "src")
    CARGO_CACHE = os.path.join(_LOCAL, "Quilly", "cache", "cargo")
    BACKUP_DIR = os.path.join(_LOCAL, "Quilly", "cache", "backups")
    SUBAPP_ROOT = os.path.join(_LOCAL, "Quilly", "apps")
    BIN_DIR = os.path.join(_LOCAL, "Quilly", "bin")
else:
    _CACHE_BASE = os.environ.get("XDG_CACHE_HOME") or os.path.expanduser("~/.cache")
    _DATA_BASE = os.environ.get("XDG_DATA_HOME") or os.path.expanduser("~/.local/share")
    _CFG_BASE = os.environ.get("XDG_CONFIG_HOME") or os.path.expanduser("~/.config")
    CACHE = os.path.join(_CACHE_BASE, "arch-quilly", "atualizacoes")
    SRC_CACHE = os.path.join(_CACHE_BASE, "arch-quilly", "src")
    CARGO_CACHE = os.path.join(_CACHE_BASE, "arch-quilly-cargo")
    BACKUP_DIR = os.path.join(_CACHE_BASE, "arch-quilly", "backups")
    SUBAPP_ROOT = os.path.expanduser("~/.local/opt/arch-quilly")
    BIN_DIR = os.path.expanduser("~/.local/bin")

VALIDADE = 15 * 60          # segundos que uma consulta ao GitHub vale
BASE = ("quilly", "noty")                                     # vêm no arch-quilly
AVULSOS = ("artry", "volury", "fotry", "takery", "kromar")    # cada um no seu pacote ou sub-app Git

CRAFT_FONTS_REPO = "https://github.com/storytold/craft-fonts.git"

# Repositórios individuais para compilação nativa adaptativa (Linux ou Windows)
SUBAPPS_GIT = {
    "fotry": {
        "name": "Fotry",
        "repo": "Sapyst/fotry",
        "url": "https://github.com/Sapyst/fotry.git",
        "branch": "fotry",
        "bin": "photocraft",
        "needs_fonts": True,
        "needs_pnpm": False,
        "cargo_args": ["build", "--release", "-p", "photocraft"],
        "config_dirs": (
            [os.path.join(os.environ.get("APPDATA") or "", "Quilly", "fotry")]
            if IS_WINDOWS else
            [os.path.join(os.environ.get("XDG_CONFIG_HOME") or os.path.expanduser("~/.config"), "arch-quilly", "fotry")]
        ),
        "categories": "Graphics;RasterGraphics;2DGraphics;",
        "comment": "Editor de imagens: camadas, máscaras, PSD, RAW e Upscale com IA",
        "mimes": "application/x-photocraft;image/vnd.adobe.photoshop;image/png;image/jpeg;image/webp;image/x-canon-cr3;",
    },
    "takery": {
        "name": "Takery",
        "repo": "Sapyst/takery",
        "url": "https://github.com/Sapyst/takery.git",
        "branch": "takery",
        "bin": "filmcraft",
        "needs_fonts": True,
        "needs_pnpm": False,
        "cargo_args": (
            ["build", "--release", "-p", "filmcraft"]
            if IS_WINDOWS else
            ["build", "--release", "-p", "filmcraft", "--features", "whisper"]
        ),
        "config_dirs": (
            [os.path.join(os.environ.get("APPDATA") or "", "Quilly", "takery")]
            if IS_WINDOWS else
            [os.path.join(os.environ.get("XDG_DATA_HOME") or os.path.expanduser("~/.local/share"), "arch-quilly", "takery")]
        ),
        "categories": "AudioVideo;Video;AudioVideoEditing;",
        "comment": "Editor de vídeo nativo com legendas automáticas pela GPU",
        "mimes": "application/x-filmcraft;video/mp4;video/quicktime;video/x-matroska;video/webm;",
    },
    "kromar": {
        "name": "Kromar",
        "repo": "Sapyst/kromar",
        "url": "https://github.com/Sapyst/kromar.git",
        "branch": "main",
        "bin": "kromar-app",
        "needs_fonts": False,
        "needs_pnpm": True,
        "cargo_args": ["build", "--release", "--manifest-path", "src-tauri/Cargo.toml"],
        "config_dirs": (
            [os.path.join(os.environ.get("APPDATA") or "", "app.kromar.studio")]
            if IS_WINDOWS else
            [os.path.join(os.environ.get("XDG_DATA_HOME") or os.path.expanduser("~/.local/share"), "app.kromar.studio")]
        ),
        "categories": "Graphics;Photography;RasterGraphics;",
        "comment": "Editor de fotos RAW não-destrutivo com Depth Map neural e IA local",
        "mimes": "image/jpeg;image/png;image/x-canon-cr2;image/x-canon-cr3;image/x-adobe-dng;image/x-sony-arw;image/x-nikon-nef;",
    },
}

_trava = threading.Lock()
_remoto = {"quando": 0.0, "dados": None, "erro": None}
_remoto_git = {"quando": 0.0, "commits": {}}


def _numeros(v):
    return tuple(int(x) for x in re.findall(r"\d+", v or "")[:3]) or (0,)


def mais_nova(remota, local):
    return _numeros(remota) > _numeros(local)


def _get(url, timeout=10):
    req = urllib.request.Request(url, headers={"User-Agent": AGENTE, "Accept": "application/vnd.github+json"})
    with urllib.request.urlopen(req, timeout=timeout) as r:
        return r.read()


def consultar(forcar=False):
    """A release mais recente do Quilly + último commit de cada sub-app Git; None sem internet."""
    consultar_commits_git(forcar=forcar)
    with _trava:
        if not forcar and _remoto["dados"] and time.time() - _remoto["quando"] < VALIDADE:
            return _remoto["dados"]
    try:
        rel = json.loads(_get(API))
        pacotes = {}
        somas = {}
        for a in rel.get("assets", []):
            nome = a.get("name", "")
            if nome == "SHA256SUMS":
                for linha in _get(a["browser_download_url"]).decode().splitlines():
                    partes = linha.split()
                    if len(partes) == 2:
                        somas[partes[1].lstrip("*")] = partes[0]
            elif nome.endswith(".pkg.tar.zst"):
                digest = (a.get("digest") or "").removeprefix("sha256:")
                pacotes[nome] = {"url": a["browser_download_url"], "tamanho": a.get("size", 0), "sha256": digest}
        for nome, soma in somas.items():
            if nome in pacotes:
                pacotes[nome]["sha256"] = soma
        dados = {"versao": (rel.get("tag_name") or "").lstrip("v"), "pacotes": pacotes}
        with _trava:
            _remoto.update(quando=time.time(), dados=dados, erro=None)
        return dados
    except Exception as e:  # noqa: BLE001  (sem internet, GitHub fora, limite da API)
        with _trava:
            _remoto.update(quando=time.time(), erro=str(e))
            return _remoto["dados"]


def consultar_commits_git(forcar=False):
    """Consulta o último commit (SHA curto) de cada sub-app no GitHub."""
    with _trava:
        if not forcar and _remoto_git["commits"] and time.time() - _remoto_git["quando"] < VALIDADE:
            return dict(_remoto_git["commits"])
    commits = {}
    for app_id, spec in SUBAPPS_GIT.items():
        try:
            url = f"https://api.github.com/repos/{spec['repo']}/commits/{spec['branch']}"
            info = json.loads(_get(url, timeout=8))
            sha = (info.get("sha") or "")[:7]
            if sha:
                commits[app_id] = {
                    "sha": sha,
                    "full_sha": info.get("sha") or "",
                    "date": ((info.get("commit") or {}).get("committer") or {}).get("date") or "",
                }
        except Exception:  # noqa: BLE001
            pass
    if commits:
        with _trava:
            _remoto_git["quando"] = time.time()
            _remoto_git["commits"].update(commits)
    with _trava:
        return dict(_remoto_git["commits"])


def commits_em_cache():
    with _trava:
        return dict(_remoto_git["commits"])


def em_cache():
    with _trava:
        return _remoto["dados"]


def subapp_dir(app_id):
    return os.path.join(SUBAPP_ROOT, app_id)


def subapp_bin(app_id):
    return os.path.join(subapp_dir(app_id), app_id + EXE_EXT)


def subapp_commit_local(app_id):
    """Lê o SHA curto instalado localmente (sub-app compilado via Git ou SOURCE.txt do pacote)."""
    p = os.path.join(subapp_dir(app_id), "commit.txt")
    if os.path.isfile(p):
        try:
            return open(p, encoding="utf-8").read().strip()[:7]
        except OSError:
            pass
    # Fallback: verifica SOURCE.txt de pacote Arch instalado em /usr/share/licenses/arch-<app>
    if not IS_WINDOWS:
        lic_root = f"/usr/share/licenses/arch-{app_id}"
        if os.path.isdir(lic_root):
            for root, _, files in os.walk(lic_root):
                if "SOURCE.txt" in files:
                    try:
                        txt = open(os.path.join(root, "SOURCE.txt"), encoding="utf-8").read()
                        m = re.search(r"at ([0-9a-f]{7,40})", txt)
                        if m:
                            return m.group(1)[:7]
                    except OSError:
                        pass
    return ""


def subapp_instalado_git(app_id):
    return os.path.isfile(subapp_bin(app_id))


def tem_atualizacao_git(app_id):
    """True se o app está instalado e o commit remoto no repo dele é diferente do local."""
    if app_id not in SUBAPPS_GIT:
        return False
    remoto = commits_em_cache().get(app_id)
    if not remoto or not remoto.get("sha"):
        return False
    local = subapp_commit_local(app_id)
    if not local:
        return False
    return local != remoto["sha"]


def pode_compilar_git(app_id):
    """Verifica se a máquina atual tem git + cargo (+ pnpm para Kromar) para compilar na hora."""
    spec = SUBAPPS_GIT.get(app_id)
    if not spec:
        return False
    if not shutil.which("git") or not shutil.which("cargo"):
        return False
    if spec.get("needs_pnpm") and not (shutil.which("pnpm") or shutil.which("npm")):
        return False
    return True


def _backup_configs(app_id):
    """Cria uma cópia de segurança das configs do usuário antes de atualizar o binário."""
    spec = SUBAPPS_GIT.get(app_id)
    if not spec:
        return None
    stamp = time.strftime("%Y%m%d-%H%M%S")
    dest_root = os.path.join(BACKUP_DIR, f"{app_id}-{stamp}")
    copied = False
    for cfg in spec.get("config_dirs", []):
        if cfg and os.path.isdir(cfg):
            os.makedirs(dest_root, exist_ok=True)
            dst = os.path.join(dest_root, os.path.basename(cfg.rstrip("/\\")))
            shutil.copytree(cfg, dst, dirs_exist_ok=True, ignore=shutil.ignore_patterns("cache", "thumbs", "browser_thumbs", "models", "*.onnx"))
            copied = True
    # Mantém apenas os 3 backups mais recentes por app
    if copied and os.path.isdir(BACKUP_DIR):
        antigos = sorted(
            d for d in os.listdir(BACKUP_DIR)
            if d.startswith(f"{app_id}-") and os.path.isdir(os.path.join(BACKUP_DIR, d))
        )
        for velho in antigos[:-3]:
            shutil.rmtree(os.path.join(BACKUP_DIR, velho), ignore_errors=True)
    return dest_root if copied else None


def _run_cmd(cmd, cwd=None, env=None):
    proc = subprocess.run(cmd, cwd=cwd, env=env, capture_output=True, text=True)
    if proc.returncode != 0:
        err = (proc.stderr or proc.stdout or "").strip().splitlines()[-8:]
        raise RuntimeError(f"Comando falhou ({' '.join(cmd[:3])}):\n" + "\n".join(err))
    return proc.stdout.strip()


def _sync_git_repo(url, branch, dest):
    os.makedirs(os.path.dirname(dest), exist_ok=True)
    if os.path.isdir(os.path.join(dest, ".git")):
        _run_cmd(["git", "fetch", "origin", branch, "--depth=1"], cwd=dest)
        _run_cmd(["git", "checkout", "-B", branch, f"origin/{branch}"], cwd=dest)
        _run_cmd(["git", "reset", "--hard", f"origin/{branch}"], cwd=dest)
    else:
        if os.path.exists(dest):
            shutil.rmtree(dest, ignore_errors=True)
        _run_cmd(["git", "clone", "--depth=1", "--branch", branch, url, dest])
    return _run_cmd(["git", "rev-parse", "HEAD"], cwd=dest)


def _instalar_atalho_linux(app_id, spec, bin_dest):
    os.makedirs(BIN_DIR, exist_ok=True)
    wrapper = os.path.join(BIN_DIR, app_id)
    app_folder = subapp_dir(app_id)
    with open(wrapper, "w", encoding="utf-8") as f:
        f.write(
            "#!/usr/bin/env bash\n"
            "set -euo pipefail\n"
            f'APP_DIR="{app_folder}"\n'
            'if [[ -d "${APP_DIR}/libs" ]]; then\n'
            '  export LD_LIBRARY_PATH="${APP_DIR}/libs${LD_LIBRARY_PATH:+:${LD_LIBRARY_PATH}}"\n'
            "fi\n"
            'export GTK_USE_PORTAL=1\n'
            'mkdir -p "${XDG_CACHE_HOME:-$HOME/.cache}/arch-quilly"\n'
            f'exec "{bin_dest}" "$@" 2>>"${{XDG_CACHE_HOME:-$HOME/.cache}}/arch-quilly/{app_id}.log"\n'
        )
    os.chmod(wrapper, 0o755)

    # Copia ícone da suíte e grava entrada .desktop no Menu Iniciar do usuário
    nome = spec["name"]
    app_dir = os.path.join(os.environ.get("XDG_DATA_HOME") or os.path.expanduser("~/.local/share"), "applications")
    icon_dir = os.path.join(os.environ.get("XDG_DATA_HOME") or os.path.expanduser("~/.local/share"), "icons", "hicolor", "256x256", "apps")
    os.makedirs(app_dir, exist_ok=True)
    os.makedirs(icon_dir, exist_ok=True)

    aqui = os.path.dirname(os.path.abspath(__file__))
    for cand in (
        os.path.join(aqui, "ui", "assets", f"{app_id}.png"),
        os.path.join(os.path.dirname(aqui), "..", "icones", f"{app_id}.png"),
    ):
        if os.path.isfile(cand):
            shutil.copy2(cand, os.path.join(icon_dir, f"com.archquilly.{nome}.png"))
            break

    desktop = os.path.join(app_dir, f"com.archquilly.{nome}.desktop")
    with open(desktop, "w", encoding="utf-8") as f:
        f.write(
            "[Desktop Entry]\n"
            "Type=Application\n"
            f"Name={nome}\n"
            f"Comment={spec['comment']}\n"
            f"Exec={wrapper} %F\n"
            f"Icon=com.archquilly.{nome}\n"
            "Terminal=false\n"
            f"StartupWMClass=com.archquilly.{nome}\n"
            f"Categories={spec['categories']}\n"
            f"MimeType={spec['mimes']}\n"
        )
    if shutil.which("update-desktop-database"):
        subprocess.run(["update-desktop-database", app_dir], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)


def instalar_via_git(app_id, progresso):
    """
    Baixa/atualiza do Git, compila nativamente para o SO atual (Linux ou Windows),
    faz backup das configs do usuário e substitui apenas o binário antigo pelo novo.
    """
    spec = SUBAPPS_GIT.get(app_id)
    if not spec:
        raise RuntimeError(f"App {app_id} não possui compilação via Git configurada.")

    so_label = "Windows" if IS_WINDOWS else "Linux"
    progresso(5, f"Baixando código de {spec['name']} ({spec['repo']})…")
    src_dir = os.path.join(SRC_CACHE, app_id)
    full_sha = _sync_git_repo(spec["url"], spec["branch"], src_dir)
    short_sha = full_sha[:7]

    env = os.environ.copy()
    env.setdefault("CARGO_BUILD_JOBS", "6")
    alvo_cargo = os.path.join(CARGO_CACHE, app_id)
    os.makedirs(alvo_cargo, exist_ok=True)
    env["CARGO_TARGET_DIR"] = alvo_cargo

    if spec.get("needs_fonts"):
        progresso(18, "Sincronizando fontes (craft-fonts)…")
        fonts_dir = os.path.join(SRC_CACHE, "craft-fonts")
        _sync_git_repo(CRAFT_FONTS_REPO, "main", fonts_dir)
        env["CRAFT_FONTS_DIR"] = fonts_dir

    if spec.get("needs_pnpm"):
        progresso(28, f"Compilando interface de {spec['name']} ({so_label})…")
        pkg_mgr = "pnpm" if shutil.which("pnpm") else "npm"
        ui_dir = os.path.join(src_dir, "ui")
        _run_cmd([pkg_mgr, "install"], cwd=ui_dir, env=env)
        _run_cmd([pkg_mgr, "run", "build"], cwd=ui_dir, env=env)

    progresso(45, f"Compilando {spec['name']} para {so_label} (Rust release)…")
    _run_cmd(["cargo", *spec["cargo_args"]], cwd=src_dir, env=env)

    built_bin = os.path.join(alvo_cargo, "release", spec["bin"] + EXE_EXT)
    if not os.path.isfile(built_bin):
        raise RuntimeError(f"Binário compilado não encontrado em {built_bin}")

    progresso(88, "Salvando backup das suas configurações…")
    _backup_configs(app_id)

    progresso(93, f"Instalando nova versão ({short_sha}) sem alterar suas configs…")
    dest_dir = subapp_dir(app_id)
    os.makedirs(dest_dir, exist_ok=True)

    # Preserva libs isoladas locais existentes (ex.: libjxl 0.13 do Kromar no Arch/Hyprland)
    if not IS_WINDOWS and app_id == "kromar":
        dest_libs = os.path.join(dest_dir, "libs")
        if not os.path.isdir(dest_libs):
            for cand_libs in (
                os.path.expanduser("~/.local/opt/kromar/libs"),
                "/mnt/d/DATA/Trabalhos/Projeto/Apps/kromar/libs",
            ):
                if os.path.isdir(cand_libs):
                    shutil.copytree(cand_libs, dest_libs, symlinks=True, dirs_exist_ok=True)
                    break

    final_bin = subapp_bin(app_id)
    tmp_bin = final_bin + ".new"
    shutil.copy2(built_bin, tmp_bin)
    if not IS_WINDOWS:
        os.chmod(tmp_bin, 0o755)
    os.replace(tmp_bin, final_bin)

    with open(os.path.join(dest_dir, "commit.txt"), "w", encoding="utf-8") as f:
        f.write(full_sha + "\n")

    if not IS_WINDOWS:
        _instalar_atalho_linux(app_id, spec, final_bin)

    # Atualiza cache de commit remoto para refletir estado instalado
    with _trava:
        _remoto_git["commits"][app_id] = {"sha": short_sha, "full_sha": full_sha, "date": ""}

    progresso(100, f"{spec['name']} atualizado ({short_sha}) — configs preservadas!")
    return short_sha


def remover_subapp_git(app_id):
    """Remove o sub-app compilado localmente preservando as configs do usuário."""
    dest_dir = subapp_dir(app_id)
    if os.path.isdir(dest_dir):
        shutil.rmtree(dest_dir, ignore_errors=True)
    if not IS_WINDOWS:
        wrapper = os.path.join(BIN_DIR, app_id)
        if os.path.isfile(wrapper):
            os.remove(wrapper)
        spec = SUBAPPS_GIT.get(app_id)
        if spec:
            app_dir = os.path.join(os.environ.get("XDG_DATA_HOME") or os.path.expanduser("~/.local/share"), "applications")
            desktop = os.path.join(app_dir, f"com.archquilly.{spec['name']}.desktop")
            if os.path.isfile(desktop):
                os.remove(desktop)


def instalados():
    """Pacotes da suíte instalados no sistema: {nome: versão} (ex.: {'arch-quilly': '1.5.0'})."""
    if IS_WINDOWS or not shutil.which("pacman"):
        return {}
    try:
        saida = subprocess.run(["pacman", "-Q"], capture_output=True, text=True, timeout=10).stdout
    except (OSError, subprocess.TimeoutExpired):
        return {}
    out = {}
    for linha in saida.splitlines():
        nome, _, versao = linha.partition(" ")
        if nome.startswith("arch-") and nome[5:] in ("quilly", "noty", "artry", "volury", "fotry", "takery", "kromar", "array"):
            out[nome] = versao.split("-")[0]
    return out


def suite_antiga(tem):
    """arch-quilly até a 1.2: a suíte inteira num pacote só (ela "provides" os quatro apps)."""
    return "arch-quilly" in tem and _numeros(tem["arch-quilly"]) < (1, 3, 0)


def pacote_do_app(app_id, tem):
    """O pacote que traz `app_id` neste sistema (o Noty vem na base, se ela estiver instalada)."""
    if app_id == "quilly" or (app_id == "noty" and "arch-quilly" in tem):
        return "arch-quilly"
    if app_id in AVULSOS and suite_antiga(tem) and f"arch-{app_id}" not in tem:
        return "arch-quilly"
    return f"arch-{app_id}"


def _arquivo(remoto, pacote):
    """O asset `pacote-<versão>-1-<arch>.pkg.tar.zst` da release."""
    for nome in remoto["pacotes"]:
        if re.fullmatch(re.escape(pacote) + r"-[\d.]+-\d+-(x86_64|any)\.pkg\.tar\.zst", nome):
            return nome
    return None


def plano(app_id):
    """Quais pacotes baixar pra instalar/atualizar `app_id`. Erro com mensagem clara se não der."""
    remoto = consultar()
    if not remoto:
        raise RuntimeError("Sem conexão com o GitHub agora. Tente de novo em instantes.")
    tem = instalados()
    alvos = sorted(n for n in tem if n != "arch-array"           # o que já está instalado e tem versão nova
                   and mais_nova(remoto["versao"], tem[n]))
    if suite_antiga(tem):
        alvos += [f"arch-{a}" for a in ("artry", "volury", "fotry", "takery")]
    pedido = "arch-quilly" if app_id in BASE and ("arch-quilly" in tem or app_id == "quilly") else f"arch-{app_id}"
    alvos.append(pedido)
    if "arch-quilly" in alvos:
        alvos = [n for n in alvos if n != "arch-noty"]             # a base já traz o Noty
    alvos = list(dict.fromkeys(alvos))
    nomes = []
    for p in alvos:
        n = _arquivo(remoto, p)
        if not n:
            raise RuntimeError(f"A versão {remoto['versao']} no GitHub não tem o pacote {p}.")
        nomes.append(n)
    return remoto, nomes


def baixar(remoto, nome, progresso):
    """Baixa um pacote pro cache, conferindo o SHA-256; devolve o caminho."""
    info = remoto["pacotes"][nome]
    os.makedirs(CACHE, exist_ok=True)
    destino = os.path.join(CACHE, nome)
    if os.path.isfile(destino) and info["sha256"] and _sha(destino) == info["sha256"]:
        return destino
    parcial = destino + ".part"
    req = urllib.request.Request(info["url"], headers={"User-Agent": AGENTE})
    h = hashlib.sha256()
    with urllib.request.urlopen(req, timeout=60) as r, open(parcial, "wb") as f:
        total = int(r.headers.get("Content-Length") or info["tamanho"] or 0)
        lido = 0
        while True:
            bloco = r.read(1 << 18)
            if not bloco:
                break
            f.write(bloco)
            h.update(bloco)
            lido += len(bloco)
            progresso(lido, total)
    if not info["sha256"]:
        os.remove(parcial)
        raise RuntimeError(f"{nome}: a release não publicou o SHA-256; por segurança não instalo.")
    if h.hexdigest() != info["sha256"]:
        os.remove(parcial)
        raise RuntimeError(f"{nome}: o arquivo baixado não confere com o SHA-256 publicado.")
    os.replace(parcial, destino)
    return destino


def _sha(caminho):
    h = hashlib.sha256()
    with open(caminho, "rb") as f:
        for bloco in iter(lambda: f.read(1 << 20), b""):
            h.update(bloco)
    return h.hexdigest()


def _terminal():
    """O emulador de terminal disponível e como mandar um comando pra ele."""
    preferido = os.environ.get("TERMINAL")
    opcoes = [(preferido, ["-e"])] if preferido else []
    opcoes += [("kitty", []), ("foot", []), ("alacritty", ["-e"]), ("wezterm", ["start", "--"]),
               ("ghostty", ["-e"]), ("gnome-terminal", ["--"]), ("konsole", ["-e"]), ("xfce4-terminal", ["-x"]),
               ("xterm", ["-e"])]
    for nome, args in opcoes:
        caminho = shutil.which(nome or "")
        if caminho:
            return [caminho, *args]
    return None


def _no_terminal(acao, args, aviso, falhou):
    """Abre um terminal com `sudo pacman <acao> <args>`; devolve o processo (espera-se ele fechar)."""
    term = _terminal()
    if not term:
        raise RuntimeError(f"Nenhum terminal encontrado. Rode: sudo pacman {acao} " + " ".join(args))
    aspas = " ".join("'" + a.replace("'", "'\\''") + "'" for a in args)
    script = (f"printf '\\nArch Quilly: {aviso} O pacman pede a sua senha de administrador.\\n\\n'; "
              f"sudo pacman {acao} {aspas}; s=$?; echo; "
              f"if [ $s -eq 0 ]; then echo 'Pronto. Pode fechar esta janela.'; else echo '{falhou}'; fi; "
              "printf 'Enter para fechar…'; read _")
    return subprocess.Popen([*term, "sh", "-c", script], start_new_session=True,
                            stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)


def instalar_no_terminal(arquivos):
    return _no_terminal("-U", arquivos, "instalando a atualização.", "A instalação não terminou.")


def remover_no_terminal(pacote):
    return _no_terminal("-R", [pacote], f"removendo o {pacote}.", "A remoção não terminou.")
