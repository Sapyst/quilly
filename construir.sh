#!/usr/bin/env bash
# construir.sh — gera os pacotes do Arch da Arch Quilly em ./dist:
#   arch-quilly-<versão>-x86_64.pkg.tar.zst   a base: o Quilly (a central) com o Noty
#   arch-artry-…, arch-volury-…, arch-fotry-…, arch-takery-…   os outros apps, cada um no seu pacote;
#       o Quilly baixa da release do GitHub o que o usuário pedir pra instalar (atualizador.py)
#   arch-noty-…   o Noty sozinho, pra quem não quer o Quilly (não se mistura com a base, que já tem o Noty)
# Instalar:   sudo pacman -U dist/arch-quilly-*.pkg.tar.zst      (o pacman puxa as dependências)
# Remover:    sudo pacman -R arch-quilly arch-artry …
set -euo pipefail
RAIZ="$(cd "$(dirname "$0")" && pwd)"
VERSAO="$(cat "$RAIZ/apps/quilly/VERSAO")"
TRAB="${XDG_CACHE_HOME:-$HOME/.cache}/arch-quilly-build"
# Noty é nativo (Rust, GTK4 + GtkSourceView): o código mora em apps/noty, compilado aqui.
# Fotry e Takery são nativos (Rust): o código de cada um é um fork no GitHub, que mora aqui como submódulo.
#   Fotry  = fork do PhotoCraft (apps/fotry),  binário photocraft
#   Takery = fork do FilmCraft  (apps/takery), binário filmcraft (com Whisper)
#   as fontes que eles embutem: vendor/craft-fonts
# Clonou sem os submódulos? git submodule update --init
# O binário é compilado aqui se faltar ou se o código for mais novo que ele. A saída do cargo fica em
# ~/.cache/arch-quilly-cargo/<app>, fora do NTFS (compila bem mais rápido lá).
FOTRY_SRC="${FOTRY_SRC:-$RAIZ/apps/fotry}"
TAKERY_SRC="${TAKERY_SRC:-$RAIZ/apps/takery}"
KROMAR_SRC="${KROMAR_SRC:-$RAIZ/apps/kromar}"
CRAFT_FONTS_DIR="${CRAFT_FONTS_DIR:-$RAIZ/vendor/craft-fonts}"
CARGO_ALVO="${XDG_CACHE_HOME:-$HOME/.cache}/arch-quilly-cargo"
for d in "$FOTRY_SRC" "$TAKERY_SRC" "$KROMAR_SRC" "$CRAFT_FONTS_DIR"; do
    [ -n "$(ls -A "$d" 2>/dev/null)" ] || { echo "Falta o código em $d: rode git submodule update --init" >&2; exit 1; }
done
compilar() {   # compilar <pasta> <binário> <nome> [argumentos do cargo…]
    local src=$1 bin=$2 nome=$3; shift 3
    local alvo="$CARGO_ALVO/$(basename "$src")"
    if [ ! -x "$alvo/release/$bin" ] || [ -n "$(find "$src" -path "$src/target" -prune -o -path "$src/ui/node_modules" -prune -o \( -name '*.rs' -o -name Cargo.toml \) -newer "$alvo/release/$bin" -print -quit)" ]; then
        echo "Compilando o $nome (cargo build --release)…"
        (cd "$src" && CARGO_BUILD_JOBS="${CARGO_BUILD_JOBS:-6}" CRAFT_FONTS_DIR="$CRAFT_FONTS_DIR" CARGO_TARGET_DIR="$alvo" cargo build --release "$@")
    fi
}
compilar "$RAIZ/apps/noty" noty Noty
compilar "$FOTRY_SRC" photocraft Fotry -p photocraft
compilar "$TAKERY_SRC" filmcraft Takery -p filmcraft --features whisper
if [ ! -f "$KROMAR_SRC/ui/dist/index.html" ] || [ -n "$(find "$KROMAR_SRC/ui/src" -type f -newer "$KROMAR_SRC/ui/dist/index.html" -print -quit 2>/dev/null)" ]; then
    echo "Compilando a interface do Kromar (pnpm build)…"
    (cd "$KROMAR_SRC/ui" && pnpm install && pnpm run build)
fi
compilar "$KROMAR_SRC" kromar-app Kromar --manifest-path src-tauri/Cargo.toml
rm -rf "$TRAB" && mkdir -p "$TRAB/arvore" "$RAIZ/dist"

# ---------- árvore de arquivos de cada app (o que vai pra /usr/share/arch-quilly/<app>) ----------
for app in quilly artry volury; do
    destino="$TRAB/arvore/$app"
    mkdir -p "$destino"
    cp -r "$RAIZ/apps/$app/." "$destino/"
    cp "$RAIZ/comum/archhost.py" "$RAIZ/comum/icones.py" "$destino/"      # cada app leva a sua casca
    find "$destino" -name "__pycache__" -prune -exec rm -rf {} +
    rm -f "$destino"/*.bak* "$destino/ui"/*.bak* 2>/dev/null || true
done
# Noty: o binário, o dicionário pt-BR embutido e a versão
mkdir -p "$TRAB/arvore/noty"
cp "$CARGO_ALVO/noty/release/noty" "$TRAB/arvore/noty/noty"
cp -r "$RAIZ/apps/noty/hunspell" "$RAIZ/apps/noty/VERSAO" "$TRAB/arvore/noty/"
# Fotry, Takery e Kromar: o binário e as licenças do projeto de origem
nativo() {   # nativo <app> <pasta> <binário> <origem> <url da origem>
    local app=$1 src=$2 bin=$3 origem=$4 url=$5
    mkdir -p "$TRAB/arvore/$app/licencas/$origem"
    cp "$CARGO_ALVO/$(basename "$src")/release/$bin" "$TRAB/arvore/$app/$app"
    for f in LICENSE LICENSE-MIT LICENSE-APACHE NOTICE ATTRIBUTION.md; do [ -f "$src/$f" ] && cp "$src/$f" "$TRAB/arvore/$app/licencas/$origem/"; done
    echo "$VERSAO" > "$TRAB/arvore/$app/VERSAO"
    (cd "$src" && git log -1 --format="$app: source $url at %H" > "$TRAB/arvore/$app/licencas/$origem/SOURCE.txt")
}
nativo fotry "$FOTRY_SRC" photocraft photocraft https://github.com/storytold/photocraft
nativo takery "$TAKERY_SRC" filmcraft filmcraft https://github.com/storytold/filmcraft
nativo kromar "$KROMAR_SRC" kromar-app kromar https://github.com/Sapyst/kromar
cp -r "$RAIZ/icones" "$TRAB/arvore/icones"
chmod -R u=rwX,go=rX "$TRAB/arvore"        # modos do NTFS (ex.: 0600) não podem entrar no pacote
# ícones em vários tamanhos: gerados aqui (dentro do fakeroot do makepkg o carregador de imagens não roda)
python3 - "$TRAB/arvore/icones" <<'PY'
import os, sys, gi
gi.require_version("GdkPixbuf", "2.0")
from gi.repository import GdkPixbuf
pasta = sys.argv[1]
for app in ("quilly", "noty", "artry", "volury", "fotry", "takery", "kromar"):
    for t in (256, 128, 64, 48, 32):
        os.makedirs(os.path.join(pasta, str(t)), exist_ok=True)
        GdkPixbuf.Pixbuf.new_from_file_at_scale(os.path.join(pasta, app + ".png"), t, t, True).savev(
            os.path.join(pasta, str(t), app + ".png"), "png", [], [])
PY

cat > "$TRAB/PKGBUILD" <<PKG
# Gerado por construir.sh (Arch Quilly). Não edite aqui: edite construir.sh.
pkgbase=arch-quilly
pkgname=(arch-quilly arch-noty arch-artry arch-volury arch-fotry arch-takery arch-kromar)
pkgver=$VERSAO
pkgrel=1
arch=('x86_64')
url="https://github.com/Sapyst/quilly"
license=('GPL-3.0-or-later')
_comum=('python' 'python-gobject' 'gtk4' 'libadwaita' 'webkitgtk-6.0' 'xdg-utils' 'desktop-file-utils' 'shared-mime-info')
_nativo=('xdg-utils' 'desktop-file-utils' 'shared-mime-info')
_noty=('gtk4' 'libadwaita' 'gtksourceview5' 'libspelling' 'enchant' 'hunspell')   # nativo (Rust)
_artry=('python-docx' 'python-markdown' 'fontconfig')
_volury=('python-markdown')
_fotry=('vulkan-icd-loader' 'libxkbcommon' 'wayland' 'gtk3' 'curl' 'libarchive' 'xdg-utils' 'desktop-file-utils' 'shared-mime-info')   # nativo (wgpu); curl+bsdtar instalam o motor de IA do Upscale
_takery=('vulkan-icd-loader' 'libxkbcommon' 'wayland' 'gtk3' 'alsa-lib' 'libva' 'whisper-cpp' 'ggml-vulkan' 'xdg-utils' 'desktop-file-utils' 'shared-mime-info')   # nativo; libva: decodificação por hardware; whisper-cpp+ggml-vulkan: legendas na GPU
_kromar=('gtk3' 'webkit2gtk-4.1' 'cairo' 'gdk-pixbuf2' 'glib2' 'xdg-utils' 'desktop-file-utils' 'shared-mime-info')   # nativo (Rust + Tauri v2)

# instala um app: arquivos, lançador, .desktop, ícones, tipos de arquivo
_instalar_app() {
    local app=\$1 Nome=\$2 mimes=\$3 descricao=\$4 categorias=\$5
    if [ "\$app" = noty ] || [ "\$app" = fotry ] || [ "\$app" = takery ] || [ "\$app" = kromar ]; then
        # nativos: binário em /usr/lib, licenças do projeto de origem (forks) em /usr/share/licenses
        install -Dm755 "\$startdir/arvore/\$app/\$app" "\$pkgdir/usr/lib/arch-quilly/\$app/\$app"
        install -Dm644 "\$startdir/arvore/\$app/VERSAO" "\$pkgdir/usr/share/arch-quilly/\$app/VERSAO"
        if [ -d "\$startdir/arvore/\$app/licencas" ]; then
            install -d "\$pkgdir/usr/share/licenses/\$pkgname"
            cp -r "\$startdir/arvore/\$app/licencas/"* "\$pkgdir/usr/share/licenses/\$pkgname/"
        fi
        if [ -d "\$startdir/arvore/\$app/hunspell" ]; then
            install -d "\$pkgdir/usr/share/arch-quilly/\$app"
            cp -r "\$startdir/arvore/\$app/hunspell" "\$pkgdir/usr/share/arch-quilly/\$app/"
        fi
        install -Dm755 /dev/stdin "\$pkgdir/usr/bin/\$app" <<LANCADOR
#!/bin/sh
# \$Nome (Arch Quilly), nativo. Mensagens em ~/.cache/arch-quilly/\$app.log
mkdir -p "\\\${XDG_CACHE_HOME:-\\\$HOME/.cache}/arch-quilly"
if [ -d "\\\$HOME/.local/opt/kromar/libs" ] && [ "\$app" = kromar ]; then
  export LD_LIBRARY_PATH="\\\$HOME/.local/opt/kromar/libs\\\${LD_LIBRARY_PATH:+:\\\$LD_LIBRARY_PATH}"
fi
export GTK_USE_PORTAL=1
exec /usr/lib/arch-quilly/\$app/\$app "\\\$@" 2>>"\\\${XDG_CACHE_HOME:-\\\$HOME/.cache}/arch-quilly/\$app.log"
LANCADOR
    else
    install -d "\$pkgdir/usr/share/arch-quilly/\$app"
    cp -r "\$startdir/arvore/\$app/." "\$pkgdir/usr/share/arch-quilly/\$app/"
    install -Dm755 /dev/stdin "\$pkgdir/usr/bin/\$app" <<LANCADOR
#!/bin/sh
# \$Nome (Arch Quilly). Mensagens do app vão pra ~/.cache/arch-quilly/\$app.log
mkdir -p "\\\${XDG_CACHE_HOME:-\\\$HOME/.cache}/arch-quilly"
exec python3 /usr/share/arch-quilly/\$app/\$app.py "\\\$@" 2>>"\\\${XDG_CACHE_HOME:-\\\$HOME/.cache}/arch-quilly/\$app.log"
LANCADOR
    fi
    install -Dm644 /dev/stdin "\$pkgdir/usr/share/applications/com.archquilly.\$Nome.desktop" <<DESKTOP
[Desktop Entry]
Type=Application
Name=\$Nome
Comment=\$descricao
Exec=\$app %F
Icon=com.archquilly.\$Nome
Terminal=false
StartupWMClass=com.archquilly.\$Nome
Categories=\$categorias
MimeType=\$mimes
DESKTOP
    local t
    for t in 256 128 64 48 32; do
        install -Dm644 "\$startdir/arvore/icones/\$t/\$app.png" "\$pkgdir/usr/share/icons/hicolor/\${t}x\${t}/apps/com.archquilly.\$Nome.png"
    done
    # tipos de arquivo da suíte (.hyp e .artry), com o ícone do documento Hyper; mais o documento de cada app nativo
    local extra_mime=""
    if [ "\$app" = fotry ]; then
        extra_mime='  <mime-type type="application/x-photocraft">
    <comment>Fotry document (layers)</comment>
    <icon name="com.archquilly.Fotry"/>
    <glob pattern="*.pcraft"/>
  </mime-type>'
    elif [ "\$app" = takery ]; then
        extra_mime='  <mime-type type="application/x-filmcraft">
    <comment>Takery project</comment>
    <sub-class-of type="application/json"/>
    <icon name="com.archquilly.Takery"/>
    <glob pattern="*.fcproj"/>
  </mime-type>'
    fi
    install -Dm644 /dev/stdin "\$pkgdir/usr/share/mime/packages/arch-quilly-\$app.xml" <<MIME
<?xml version="1.0" encoding="UTF-8"?>
<mime-info xmlns="http://www.freedesktop.org/standards/shared-mime-info">
  <mime-type type="application/x-hyp">
    <comment>Documento Hyper</comment>
    <comment xml:lang="pt_BR">Documento Hyper</comment>
    <sub-class-of type="text/plain"/>
    <icon name="com.archquilly.\$Nome.hyp"/>
    <glob pattern="*.hyp"/>
  </mime-type>
  <mime-type type="application/x-artry">
    <comment>Documento do Artry</comment>
    <sub-class-of type="text/plain"/>
    <glob pattern="*.artry"/>
  </mime-type>
\${extra_mime}
</mime-info>
MIME
    install -Dm644 "\$startdir/arvore/icones/hyp.png" "\$pkgdir/usr/share/icons/hicolor/256x256/mimetypes/com.archquilly.\$Nome.hyp.png"
}

package_arch-quilly() {
    arch=('x86_64')
    pkgdesc="Arch Quilly: the suite hub (Quilly) with Noty; installs Artry, Volury, Fotry and Takery from GitHub"
    depends=("\${_comum[@]}" "\${_noty[@]}")
    optdepends=('hunspell-en_us: corretor em inglês no Noty' 'hunspell-es_any: corretor em espanhol no Noty'
                'hunspell-fr-comprehensive: corretor em francês no Noty')
    provides=(arch-noty)
    conflicts=(arch-noty arch-array)
    _instalar_app quilly Quilly "" "Central da suíte de escrita: apps e documentos recentes" "Office;Utility;"
    _instalar_app noty Noty "application/x-hyp;text/markdown;text/plain;text/html;" "Editor de texto simples com vidro: .hyp, Markdown e texto" "Office;TextEditor;Utility;"
}
package_arch-noty() {
    arch=('x86_64')
    pkgdesc="Noty (Arch Quilly): editor de texto simples com vidro — .hyp, Markdown e texto"
    depends=("\${_nativo[@]}" "\${_noty[@]}")
    optdepends=('hunspell-en_us: corretor em inglês' 'hunspell-es_any: corretor em espanhol' 'hunspell-fr-comprehensive: corretor em francês')
    conflicts=(arch-quilly)
    _instalar_app noty Noty "application/x-hyp;text/markdown;text/plain;text/html;" "Editor de texto simples com vidro: .hyp, Markdown e texto" "Office;TextEditor;Utility;"
}
package_arch-artry() {
    arch=('any')
    pkgdesc="Artry (Arch Quilly): editor de documentos ricos e código, com régua coordenada"
    depends=("\${_comum[@]}" "\${_artry[@]}")
    _instalar_app artry Artry "application/x-hyp;application/x-artry;text/plain;text/markdown;application/rtf;application/vnd.openxmlformats-officedocument.wordprocessingml.document;" "Editor de documentos ricos e código, com régua coordenada" "Office;WordProcessor;TextEditor;"
}
package_arch-fotry() {
    arch=('x86_64')
    pkgdesc="Fotry (Arch Quilly): image editor with layers, masks, PSD, camera RAW and AI upscale"
    depends=("\${_comum[@]}" "\${_fotry[@]}")
    conflicts=(arch-array)
    replaces=(arch-array)
    _instalar_app fotry Fotry "application/x-photocraft;image/vnd.adobe.photoshop;image/x-psd;image/png;image/jpeg;image/webp;image/gif;image/bmp;image/tiff;image/avif;image/x-tga;image/x-exr;image/openraster;application/x-krita;image/x-canon-cr2;image/x-canon-cr3;image/x-nikon-nef;image/x-sony-arw;image/x-adobe-dng;image/x-fuji-raf;image/x-olympus-orf;image/x-panasonic-rw2;image/x-pentax-pef;image/x-samsung-srw;image/x-dcraw;" "Image editor: layers, masks, PSD, camera RAW and AI upscale" "Graphics;RasterGraphics;2DGraphics;"
}
package_arch-takery() {
    arch=('x86_64')
    pkgdesc="Takery (Arch Quilly): video editor with GPU auto captions and hardware acceleration"
    depends=("\${_comum[@]}" "\${_takery[@]}")
    optdepends=('nvidia-utils: NVENC hardware encoding and NVDEC decoding' 'mesa: VA-API decoding on AMD and Intel')
    _instalar_app takery Takery "application/x-filmcraft;video/mp4;video/x-m4v;video/quicktime;video/x-matroska;video/webm;video/vnd.avi;video/x-msvideo;application/mxf;video/mp2t;video/mpeg;video/3gpp;" "Video editor: timeline, color, audio and GPU auto captions" "AudioVideo;Video;AudioVideoEditing;"
}
package_arch-volury() {
    arch=('any')
    pkgdesc="Volury (Arch Quilly): editor de e-books EPUB 2/3"
    depends=("\${_comum[@]}" "\${_volury[@]}")
    _instalar_app volury Volury "application/epub+zip;application/x-hyp;" "Editor de e-books EPUB" "Office;Publishing;"
}
package_arch-kromar() {
    arch=('x86_64')
    pkgdesc="Kromar (Arch Quilly): non-destructive RAW photo darkroom with neural Depth Map and local AI"
    depends=("\${_kromar[@]}")
    _instalar_app kromar Kromar "image/jpeg;image/png;image/x-canon-cr2;image/x-canon-cr3;image/x-adobe-dng;image/x-sony-arw;image/x-nikon-nef;" "Editor de fotos RAW não-destrutivo com Depth Map neural e IA local" "Graphics;Photography;RasterGraphics;"
}
PKG

cd "$TRAB"
makepkg -f --nodeps --noconfirm >/dev/null
for p in "$TRAB"/*.pkg.tar.zst; do
    case "$p" in *-debug-*) continue ;; esac
    cp "$p" "$RAIZ/dist/"
done
# a lista de SHA-256 que o Quilly confere antes de instalar (vai na release junto com os pacotes)
(cd "$RAIZ/dist" && sha256sum *-"$VERSAO"-1-*.pkg.tar.zst > "SHA256SUMS-$VERSAO" && cp "SHA256SUMS-$VERSAO" SHA256SUMS)
echo "Pacotes em $RAIZ/dist:"
ls -1 "$RAIZ/dist"
