#!/usr/bin/env bash
# construir.sh — gera os pacotes do Arch da Arch Quilly em ./dist:
#   arch-quilly-<versão>-x86_64.pkg.tar.zst   a suíte inteira (Quilly + Noty + Artry + Volury + Fotry + Takery) num arquivo só
#   arch-noty-…, arch-artry-…, arch-volury-…, arch-fotry-…, arch-takery-…   cada app sozinho, sem o Quilly
# Instalar:   sudo pacman -U dist/arch-quilly-*.pkg.tar.zst      (o pacman puxa as dependências)
# Remover:    sudo pacman -R arch-quilly
# Os avulsos e a suíte não se misturam: a suíte já contém os apps (ela "provides" e "conflicts" com eles).
set -euo pipefail
RAIZ="$(cd "$(dirname "$0")" && pwd)"
VERSAO="$(cat "$RAIZ/quilly/VERSAO")"
TRAB="${XDG_CACHE_HOME:-$HOME/.cache}/arch-quilly-build"
# Fotry e Takery são nativos (Rust): o código de cada um mora num fork, fora deste repositório.
#   Fotry  = fork do PhotoCraft (FOTRY_SRC,  ~/.local/src/fotry),  binário photocraft
#   Takery = fork do FilmCraft  (TAKERY_SRC, ~/.local/src/takery), binário filmcraft (com Whisper)
# O binário é compilado aqui se faltar ou se o código for mais novo que ele.
FOTRY_SRC="${FOTRY_SRC:-$HOME/.local/src/fotry}"
TAKERY_SRC="${TAKERY_SRC:-$HOME/.local/src/takery}"
compilar() {   # compilar <pasta> <binário> <nome> [argumentos do cargo…]
    local src=$1 bin=$2 nome=$3; shift 3
    if [ ! -x "$src/target/release/$bin" ] || [ -n "$(find "$src/crates" "$src/apps" -name '*.rs' -newer "$src/target/release/$bin" -print -quit)" ]; then
        echo "Compilando o $nome (cargo build --release)…"
        (cd "$src" && CRAFT_FONTS_DIR="${CRAFT_FONTS_DIR:-$(dirname "$src")/craft-fonts}" cargo build --release "$@")
    fi
}
compilar "$FOTRY_SRC" photocraft Fotry -p photocraft
compilar "$TAKERY_SRC" filmcraft Takery -p filmcraft --features whisper
rm -rf "$TRAB" && mkdir -p "$TRAB/arvore" "$RAIZ/dist"

# ---------- árvore de arquivos de cada app (o que vai pra /usr/share/arch-quilly/<app>) ----------
for app in quilly noty artry volury; do
    destino="$TRAB/arvore/$app"
    mkdir -p "$destino"
    cp -r "$RAIZ/$app/." "$destino/"
    cp "$RAIZ/comum/archhost.py" "$RAIZ/comum/icones.py" "$destino/"      # cada app leva a sua casca
    find "$destino" -name "__pycache__" -prune -exec rm -rf {} +
    rm -f "$destino"/*.bak* "$destino/ui"/*.bak* 2>/dev/null || true
done
# Fotry e Takery: o binário e as licenças do projeto de origem (MIT OR Apache-2.0, NOTICE, atribuições)
nativo() {   # nativo <app> <pasta> <binário> <origem> <url da origem>
    local app=$1 src=$2 bin=$3 origem=$4 url=$5
    mkdir -p "$TRAB/arvore/$app/licencas/$origem"
    cp "$src/target/release/$bin" "$TRAB/arvore/$app/$app"
    for f in LICENSE-MIT LICENSE-APACHE NOTICE ATTRIBUTION.md; do [ -f "$src/$f" ] && cp "$src/$f" "$TRAB/arvore/$app/licencas/$origem/"; done
    echo "$VERSAO" > "$TRAB/arvore/$app/VERSAO"
    (cd "$src" && git log -1 --format="$app: fork of $url at %H" > "$TRAB/arvore/$app/licencas/$origem/SOURCE.txt")
}
nativo fotry "$FOTRY_SRC" photocraft photocraft https://github.com/storytold/photocraft
nativo takery "$TAKERY_SRC" filmcraft filmcraft https://github.com/storytold/filmcraft
cp -r "$RAIZ/icones" "$TRAB/arvore/icones"
chmod -R u=rwX,go=rX "$TRAB/arvore"        # modos do NTFS (ex.: 0600) não podem entrar no pacote
# ícones em vários tamanhos: gerados aqui (dentro do fakeroot do makepkg o carregador de imagens não roda)
python3 - "$TRAB/arvore/icones" <<'PY'
import os, sys, gi
gi.require_version("GdkPixbuf", "2.0")
from gi.repository import GdkPixbuf
pasta = sys.argv[1]
for app in ("quilly", "noty", "artry", "volury", "fotry", "takery"):
    for t in (256, 128, 64, 48, 32):
        os.makedirs(os.path.join(pasta, str(t)), exist_ok=True)
        GdkPixbuf.Pixbuf.new_from_file_at_scale(os.path.join(pasta, app + ".png"), t, t, True).savev(
            os.path.join(pasta, str(t), app + ".png"), "png", [], [])
PY

cat > "$TRAB/PKGBUILD" <<PKG
# Gerado por construir.sh (Arch Quilly). Não edite aqui: edite construir.sh.
pkgbase=arch-quilly
pkgname=(arch-quilly arch-noty arch-artry arch-volury arch-fotry arch-takery)
pkgver=$VERSAO
pkgrel=1
arch=('x86_64')
url="https://github.com/Sapyst/quilly"
license=('MIT')
_comum=('python' 'python-gobject' 'gtk4' 'libadwaita' 'webkitgtk-6.0' 'xdg-utils' 'desktop-file-utils' 'shared-mime-info')
_noty=('hunspell' 'enchant')
_artry=('python-docx' 'python-markdown' 'fontconfig')
_volury=('python-markdown')
_fotry=('vulkan-icd-loader' 'libxkbcommon' 'wayland' 'gtk3' 'curl' 'libarchive' 'xdg-utils' 'desktop-file-utils' 'shared-mime-info')   # nativo (wgpu); curl+bsdtar instalam o motor de IA do Upscale
_takery=('vulkan-icd-loader' 'libxkbcommon' 'wayland' 'gtk3' 'alsa-lib' 'libva' 'whisper-cpp' 'ggml-vulkan' 'xdg-utils' 'desktop-file-utils' 'shared-mime-info')   # nativo; libva: decodificação por hardware; whisper-cpp+ggml-vulkan: legendas na GPU

# instala um app: arquivos, lançador, .desktop, ícones, tipos de arquivo
_instalar_app() {
    local app=\$1 Nome=\$2 mimes=\$3 descricao=\$4 categorias=\$5
    if [ "\$app" = fotry ] || [ "\$app" = takery ]; then
        # nativos: binário em /usr/lib, licenças do projeto de origem em /usr/share/licenses
        install -Dm755 "\$startdir/arvore/\$app/\$app" "\$pkgdir/usr/lib/arch-quilly/\$app/\$app"
        install -Dm644 "\$startdir/arvore/\$app/VERSAO" "\$pkgdir/usr/share/arch-quilly/\$app/VERSAO"
        install -d "\$pkgdir/usr/share/licenses/\$pkgname"
        cp -r "\$startdir/arvore/\$app/licencas/"* "\$pkgdir/usr/share/licenses/\$pkgname/"
        install -Dm755 /dev/stdin "\$pkgdir/usr/bin/\$app" <<LANCADOR
#!/bin/sh
# \$Nome (Arch Quilly), nativo. Mensagens em ~/.cache/arch-quilly/\$app.log
mkdir -p "\\\${XDG_CACHE_HOME:-\\\$HOME/.cache}/arch-quilly"
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
    arch=('x86_64')      # leva o Fotry e o Takery, nativos
    pkgdesc="Arch Quilly: writing, image and video suite (Quilly, Noty, Artry, Volury, Fotry and Takery) in one package"
    depends=("\${_comum[@]}" "\${_noty[@]}" "\${_artry[@]}" "\${_volury[@]}" "\${_fotry[@]}" "\${_takery[@]}")
    optdepends=('hunspell-en_us: corretor em inglês no Noty' 'hunspell-es_any: corretor em espanhol no Noty'
                'hunspell-fr-comprehensive: corretor em francês no Noty')
    provides=(arch-noty arch-artry arch-volury arch-fotry arch-takery)
    conflicts=(arch-noty arch-artry arch-volury arch-fotry arch-takery arch-array)
    _instalar_app quilly Quilly "" "Central da suíte de escrita: apps e documentos recentes" "Office;Utility;"
    _instalar_app noty Noty "application/x-hyp;text/markdown;text/plain;text/html;" "Editor de texto simples com vidro: .hyp, Markdown e texto" "Office;TextEditor;Utility;"
    _instalar_app artry Artry "application/x-hyp;application/x-artry;text/plain;text/markdown;application/rtf;application/vnd.openxmlformats-officedocument.wordprocessingml.document;" "Editor de documentos ricos e código, com régua coordenada" "Office;WordProcessor;TextEditor;"
    _instalar_app volury Volury "application/epub+zip;application/x-hyp;" "Editor de e-books EPUB" "Office;Publishing;"
    _instalar_app fotry Fotry "application/x-photocraft;image/vnd.adobe.photoshop;image/x-psd;image/png;image/jpeg;image/webp;image/gif;image/bmp;image/tiff;image/avif;image/x-tga;image/x-exr;image/openraster;application/x-krita;image/x-canon-cr2;image/x-canon-cr3;image/x-nikon-nef;image/x-sony-arw;image/x-adobe-dng;image/x-fuji-raf;image/x-olympus-orf;image/x-panasonic-rw2;image/x-pentax-pef;image/x-samsung-srw;image/x-dcraw;" "Image editor: layers, masks, PSD, camera RAW and AI upscale" "Graphics;RasterGraphics;2DGraphics;"
    _instalar_app takery Takery "application/x-filmcraft;video/mp4;video/x-m4v;video/quicktime;video/x-matroska;video/webm;video/vnd.avi;video/x-msvideo;application/mxf;video/mp2t;video/mpeg;video/3gpp;" "Video editor: timeline, color, audio and GPU auto captions" "AudioVideo;Video;AudioVideoEditing;"
}
package_arch-noty() {
    arch=('any')
    pkgdesc="Noty (Arch Quilly): editor de texto simples com vidro — .hyp, Markdown e texto"
    depends=("\${_comum[@]}" "\${_noty[@]}")
    optdepends=('hunspell-en_us: corretor em inglês' 'hunspell-es_any: corretor em espanhol' 'hunspell-fr-comprehensive: corretor em francês')
    conflicts=(arch-quilly)
    _instalar_app noty Noty "application/x-hyp;text/markdown;text/plain;text/html;" "Editor de texto simples com vidro: .hyp, Markdown e texto" "Office;TextEditor;Utility;"
}
package_arch-artry() {
    arch=('any')
    pkgdesc="Artry (Arch Quilly): editor de documentos ricos e código, com régua coordenada"
    depends=("\${_comum[@]}" "\${_artry[@]}")
    conflicts=(arch-quilly)
    _instalar_app artry Artry "application/x-hyp;application/x-artry;text/plain;text/markdown;application/rtf;application/vnd.openxmlformats-officedocument.wordprocessingml.document;" "Editor de documentos ricos e código, com régua coordenada" "Office;WordProcessor;TextEditor;"
}
package_arch-fotry() {
    arch=('x86_64')
    pkgdesc="Fotry (Arch Quilly): image editor with layers, masks, PSD, camera RAW and AI upscale"
    depends=("\${_comum[@]}" "\${_fotry[@]}")
    conflicts=(arch-quilly arch-array)
    replaces=(arch-array)
    _instalar_app fotry Fotry "application/x-photocraft;image/vnd.adobe.photoshop;image/x-psd;image/png;image/jpeg;image/webp;image/gif;image/bmp;image/tiff;image/avif;image/x-tga;image/x-exr;image/openraster;application/x-krita;image/x-canon-cr2;image/x-canon-cr3;image/x-nikon-nef;image/x-sony-arw;image/x-adobe-dng;image/x-fuji-raf;image/x-olympus-orf;image/x-panasonic-rw2;image/x-pentax-pef;image/x-samsung-srw;image/x-dcraw;" "Image editor: layers, masks, PSD, camera RAW and AI upscale" "Graphics;RasterGraphics;2DGraphics;"
}
package_arch-takery() {
    arch=('x86_64')
    pkgdesc="Takery (Arch Quilly): video editor with GPU auto captions and hardware acceleration"
    depends=("\${_comum[@]}" "\${_takery[@]}")
    optdepends=('nvidia-utils: NVENC hardware encoding and NVDEC decoding' 'mesa: VA-API decoding on AMD and Intel')
    conflicts=(arch-quilly)
    _instalar_app takery Takery "application/x-filmcraft;video/mp4;video/x-m4v;video/quicktime;video/x-matroska;video/webm;video/vnd.avi;video/x-msvideo;application/mxf;video/mp2t;video/mpeg;video/3gpp;" "Video editor: timeline, color, audio and GPU auto captions" "AudioVideo;Video;AudioVideoEditing;"
}
package_arch-volury() {
    arch=('any')
    pkgdesc="Volury (Arch Quilly): editor de e-books EPUB 2/3"
    depends=("\${_comum[@]}" "\${_volury[@]}")
    conflicts=(arch-quilly)
    _instalar_app volury Volury "application/epub+zip;application/x-hyp;" "Editor de e-books EPUB" "Office;Publishing;"
}
PKG

cd "$TRAB"
makepkg -f --nodeps --noconfirm >/dev/null
for p in "$TRAB"/*.pkg.tar.zst; do
    case "$p" in *-debug-*) continue ;; esac
    cp "$p" "$RAIZ/dist/"
done
echo "Pacotes em $RAIZ/dist:"
ls -1 "$RAIZ/dist"
