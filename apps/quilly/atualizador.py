"""
atualizador.py — o Quilly instala e atualiza a suíte direto do GitHub (Sapyst/quilly).

Como funciona:
  - Cada versão da suíte é uma *release* do repositório (tag vX.Y.Z), com os pacotes do Arch e um
    SHA256SUMS. Publicou release nova = todo Quilly que abrir (ou recarregar a aba Apps) vê a
    atualização.
  - O Quilly lê a release mais recente pela API pública do GitHub (sem conta, sem token), compara com
    a versão instalada e mostra "Atualizar vX.Y" nos cartões.
  - Atualizar baixa o pacote certo pra ~/.cache/arch-quilly/atualizacoes, confere o SHA-256 e abre um
    TERMINAL com `sudo pacman -U …`: a senha de administrador só é digitada no terminal, nunca no app.
      * a base (arch-quilly) traz o Quilly e o Noty; Artry, Volury, Fotry e Takery são pacotes à parte;
      * atualizar baixa a base e o pacote de cada app instalado, tudo num `pacman -U` só;
      * app não instalado: "Instalar" baixa o pacote dele (e o que já estiver instalado, se tiver versão nova);
      * vindo da 1.2 (a suíte inteira num pacote só): a atualização traz também os quatro apps, pra nenhum sumir;
      * remover um app abre o terminal com `sudo pacman -R` do pacote dele.
"""
import hashlib
import json
import os
import re
import shutil
import subprocess
import threading
import time
import urllib.request

REPO = "Sapyst/quilly"
API = f"https://api.github.com/repos/{REPO}/releases/latest"
AGENTE = "arch-quilly-updater"
CACHE = os.path.join(os.environ.get("XDG_CACHE_HOME") or os.path.expanduser("~/.cache"), "arch-quilly", "atualizacoes")
VALIDADE = 15 * 60          # segundos que uma consulta à release vale
BASE = ("quilly", "noty")                            # vêm no arch-quilly
AVULSOS = ("artry", "volury", "fotry", "takery")     # cada um no seu pacote, instalados pelo Quilly

_trava = threading.Lock()
_remoto = {"quando": 0.0, "dados": None, "erro": None}


def _numeros(v):
    return tuple(int(x) for x in re.findall(r"\d+", v or "")[:3]) or (0,)


def mais_nova(remota, local):
    return _numeros(remota) > _numeros(local)


def _get(url, timeout=10):
    req = urllib.request.Request(url, headers={"User-Agent": AGENTE, "Accept": "application/vnd.github+json"})
    with urllib.request.urlopen(req, timeout=timeout) as r:
        return r.read()


def consultar(forcar=False):
    """A release mais recente: {versao, pacotes: {nome: {url, tamanho, sha256}}}; None sem internet."""
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


def em_cache():
    with _trava:
        return _remoto["dados"]


def instalados():
    """Pacotes da suíte instalados no sistema: {nome: versão} (ex.: {'arch-quilly': '1.2.0'})."""
    try:
        saida = subprocess.run(["pacman", "-Q"], capture_output=True, text=True, timeout=10).stdout
    except (OSError, subprocess.TimeoutExpired):
        return {}
    out = {}
    for linha in saida.splitlines():
        nome, _, versao = linha.partition(" ")
        if nome.startswith("arch-") and nome[5:] in ("quilly", "noty", "artry", "volury", "fotry", "takery", "array"):
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
        alvos += [f"arch-{a}" for a in AVULSOS]                    # saem da base: vêm cada um no seu pacote
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
