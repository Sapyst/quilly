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
      * com a suíte (arch-quilly) instalada: baixa o pacote da suíte, que atualiza tudo de uma vez;
      * com apps avulsos (arch-noty, arch-fotry…): baixa o pacote de cada app instalado;
      * app não instalado: "Instalar" baixa o pacote avulso dele.
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
    if "arch-quilly" in tem:
        alvos = ["arch-quilly"]                         # a suíte inteira, num pacote só
    else:
        alvos = sorted(n for n in tem if n != "arch-array")
        if f"arch-{app_id}" not in alvos:
            alvos.append(f"arch-{app_id}")
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


def instalar_no_terminal(arquivos):
    """Abre um terminal com `sudo pacman -U` dos pacotes; devolve o processo (espera-se ele fechar)."""
    term = _terminal()
    if not term:
        raise RuntimeError("Nenhum terminal encontrado. Rode: sudo pacman -U " + " ".join(arquivos))
    aspas = " ".join("'" + a.replace("'", "'\\''") + "'" for a in arquivos)
    script = ("printf '\\nArch Quilly: instalando a atualização. O pacman pede a sua senha de administrador.\\n\\n'; "
              f"sudo pacman -U {aspas}; s=$?; echo; "
              "if [ $s -eq 0 ]; then echo 'Pronto. Pode fechar esta janela.'; else echo 'A instalação não terminou.'; fi; "
              "printf 'Enter para fechar…'; read _")
    return subprocess.Popen([*term, "sh", "-c", script], start_new_session=True,
                            stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
