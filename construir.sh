#!/usr/bin/env bash
# construir.sh — gera os pacotes do Arch da Arch Quilly em ./dist:
#   arch-quilly-<versão>-any.pkg.tar.zst   a suíte inteira (Quilly + Noty + Artry + Volury + Array) num arquivo só
#   arch-noty-…, arch-artry-…, arch-volury-…, arch-array-…   cada app sozinho, sem o Quilly
# Instalar:   sudo pacman -U dist/arch-quilly-*.pkg.tar.zst      (o pacman puxa as dependências)
# Remover:    sudo pacman -R arch-quilly
# Os avulsos e a suíte não se misturam: a suíte já contém os apps (ela "provides" e "conflicts" com eles).
set -euo pipefail
RAIZ="$(cd "$(dirname "$0")" && pwd)"
VERSAO="$(cat "$RAIZ/quilly/VERSAO")"
TRAB="${XDG_CACHE_HOME:-$HOME/.cache}/arch-quilly-build"
# Array 2 é nativo (Rust): o código mora num fork do PhotoCraft, fora deste repositório (ARRAY_SRC).
# O binário é compilado aqui se faltar ou se o código for mais novo que ele.
ARRAY_SRC="${ARRAY_SRC:-$HOME/.local/src/array}"
ARRAY_BIN="$ARRAY_SRC/target/release/photocraft"
if [ ! -x "$ARRAY_BIN" ] || [ -n "$(find "$ARRAY_SRC/crates" "$ARRAY_SRC/apps" -name '*.rs' -newer "$ARRAY_BIN" -print -quit)" ]; then
    echo "Compilando o Array (cargo build --release)…"
    (cd "$ARRAY_SRC" && CRAFT_FONTS_DIR="${CRAFT_FONTS_DIR:-$(dirname "$ARRAY_SRC")/craft-fonts}" cargo build --release -p photocraft)
fi
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
# Array: o binário e as licenças do PhotoCraft (MIT OR Apache-2.0, com o NOTICE e as atribuições)
mkdir -p "$TRAB/arvore/array/licencas"
cp "$ARRAY_BIN" "$TRAB/arvore/array/array"
for f in LICENSE-MIT LICENSE-APACHE NOTICE ATTRIBUTION.md; do cp "$ARRAY_SRC/$f" "$TRAB/arvore/array/licencas/"; done
echo "$VERSAO" > "$TRAB/arvore/array/VERSAO"
(cd "$ARRAY_SRC" && git log -1 --format='Array: fork of PhotoCraft (https://github.com/storytold/photocraft) at %H' > "$TRAB/arvore/array/licencas/SOURCE.txt")
cp -r "$RAIZ/icones" "$TRAB/arvore/icones"
chmod -R u=rwX,go=rX "$TRAB/arvore"        # modos do NTFS (ex.: 0600) não podem entrar no pacote
# ícones em vários tamanhos: gerados aqui (dentro do fakeroot do makepkg o carregador de imagens não roda)
python3 - "$TRAB/arvore/icones" <<'PY'
import os, sys, gi
gi.require_version("GdkPixbuf", "2.0")
from gi.repository import GdkPixbuf
pasta = sys.argv[1]
for app in ("quilly", "noty", "artry", "volury", "array"):
    for t in (256, 128, 64, 48, 32):
        os.makedirs(os.path.join(pasta, str(t)), exist_ok=True)
        GdkPixbuf.Pixbuf.new_from_file_at_scale(os.path.join(pasta, app + ".png"), t, t, True).savev(
            os.path.join(pasta, str(t), app + ".png"), "png", [], [])
PY

cat > "$TRAB/PKGBUILD" <<PKG
# Gerado por construir.sh (Arch Quilly). Não edite aqui: edite construir.sh.
pkgbase=arch-quilly
pkgname=(arch-quilly arch-noty arch-artry arch-volury arch-array)
pkgver=$VERSAO
pkgrel=1
arch=('x86_64')
url="https://github.com/Sapyst/quilly"
license=('MIT')
_comum=('python' 'python-gobject' 'gtk4' 'libadwaita' 'webkitgtk-6.0' 'xdg-utils' 'desktop-file-utils' 'shared-mime-info')
_noty=('hunspell' 'enchant')
_artry=('python-docx' 'python-markdown' 'fontconfig')
_volury=('python-markdown')
_array=('vulkan-icd-loader' 'libxkbcommon' 'wayland' 'gtk3' 'curl' 'libarchive' 'xdg-utils' 'desktop-file-utils' 'shared-mime-info')   # Array 2: nativo (wgpu); curl+bsdtar instalam o motor de IA do Upscale

# instala um app: arquivos, lançador, .desktop, ícones, tipos de arquivo
_instalar_app() {
    local app=\$1 Nome=\$2 mimes=\$3 descricao=\$4 categorias=\$5
    if [ "\$app" = array ]; then
        # Array 2: binário nativo em /usr/lib, licenças do PhotoCraft em /usr/share/licenses
        install -Dm755 "\$startdir/arvore/array/array" "\$pkgdir/usr/lib/arch-quilly/array/array"
        install -Dm644 "\$startdir/arvore/array/VERSAO" "\$pkgdir/usr/share/arch-quilly/array/VERSAO"
        install -d "\$pkgdir/usr/share/licenses/\$pkgname/photocraft"
        cp "\$startdir/arvore/array/licencas/"* "\$pkgdir/usr/share/licenses/\$pkgname/photocraft/"
        install -Dm755 /dev/stdin "\$pkgdir/usr/bin/array" <<LANCADOR
#!/bin/sh
# Array (Arch Quilly): editor de imagens nativo, baseado no PhotoCraft. Mensagens em ~/.cache/arch-quilly/array.log
mkdir -p "\\\${XDG_CACHE_HOME:-\\\$HOME/.cache}/arch-quilly"
exec /usr/lib/arch-quilly/array/array "\\\$@" 2>>"\\\${XDG_CACHE_HOME:-\\\$HOME/.cache}/arch-quilly/array.log"
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
    # tipos de arquivo da suíte (.hyp e .artry), com o ícone do documento Hyper; o .array só no Array
    local extra_mime=""
    if [ "\$app" = array ]; then
        extra_mime='  <mime-type type="application/x-photocraft">
    <comment>Array document (layers)</comment>
    <icon name="com.archquilly.Array"/>
    <glob pattern="*.pcraft"/>
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
    arch=('x86_64')      # leva o Array 2, nativo
    pkgdesc="Arch Quilly: writing and image suite (Quilly, Noty, Artry, Volury and Array) in one package"
    depends=("\${_comum[@]}" "\${_noty[@]}" "\${_artry[@]}" "\${_volury[@]}" "\${_array[@]}")
    optdepends=('hunspell-en_us: corretor em inglês no Noty' 'hunspell-es_any: corretor em espanhol no Noty'
                'hunspell-fr-comprehensive: corretor em francês no Noty')
    provides=(arch-noty arch-artry arch-volury arch-array)
    conflicts=(arch-noty arch-artry arch-volury arch-array)
    _instalar_app quilly Quilly "" "Central da suíte de escrita: apps e documentos recentes" "Office;Utility;"
    _instalar_app noty Noty "application/x-hyp;text/markdown;text/plain;text/html;" "Editor de texto simples com vidro: .hyp, Markdown e texto" "Office;TextEditor;Utility;"
    _instalar_app artry Artry "application/x-hyp;application/x-artry;text/plain;text/markdown;application/rtf;application/vnd.openxmlformats-officedocument.wordprocessingml.document;" "Editor de documentos ricos e código, com régua coordenada" "Office;WordProcessor;TextEditor;"
    _instalar_app volury Volury "application/epub+zip;application/x-hyp;" "Editor de e-books EPUB" "Office;Publishing;"
    _instalar_app array Array "application/x-photocraft;image/vnd.adobe.photoshop;image/x-psd;image/png;image/jpeg;image/webp;image/gif;image/bmp;image/tiff;image/avif;image/x-tga;image/x-exr;image/openraster;application/x-krita;image/x-canon-cr2;image/x-canon-cr3;image/x-nikon-nef;image/x-sony-arw;image/x-adobe-dng;image/x-fuji-raf;image/x-olympus-orf;image/x-panasonic-rw2;image/x-pentax-pef;image/x-samsung-srw;image/x-dcraw;" "Image editor: layers, masks, PSD, camera RAW and AI upscale" "Graphics;RasterGraphics;2DGraphics;"
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
package_arch-array() {
    arch=('x86_64')
    pkgdesc="Array (Arch Quilly): image editor with layers, masks, PSD, camera RAW and AI upscale"
    depends=("\${_comum[@]}" "\${_array[@]}")
    conflicts=(arch-quilly)
    _instalar_app array Array "application/x-photocraft;image/vnd.adobe.photoshop;image/x-psd;image/png;image/jpeg;image/webp;image/gif;image/bmp;image/tiff;image/avif;image/x-tga;image/x-exr;image/openraster;application/x-krita;image/x-canon-cr2;image/x-canon-cr3;image/x-nikon-nef;image/x-sony-arw;image/x-adobe-dng;image/x-fuji-raf;image/x-olympus-orf;image/x-panasonic-rw2;image/x-pentax-pef;image/x-samsung-srw;image/x-dcraw;" "Image editor: layers, masks, PSD, camera RAW and AI upscale" "Graphics;RasterGraphics;2DGraphics;"
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
