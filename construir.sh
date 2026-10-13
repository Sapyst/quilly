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
rm -rf "$TRAB" && mkdir -p "$TRAB/arvore" "$RAIZ/dist"

# ---------- árvore de arquivos de cada app (o que vai pra /usr/share/arch-quilly/<app>) ----------
for app in quilly noty artry volury array; do
    destino="$TRAB/arvore/$app"
    mkdir -p "$destino"
    cp -r "$RAIZ/$app/." "$destino/"
    cp "$RAIZ/comum/archhost.py" "$RAIZ/comum/icones.py" "$destino/"      # cada app leva a sua casca
    find "$destino" -name "__pycache__" -prune -exec rm -rf {} +
    rm -f "$destino"/*.bak* "$destino/ui"/*.bak* 2>/dev/null || true
done
cp -r "$RAIZ/icones" "$TRAB/arvore/icones"
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
arch=('any')
url="https://github.com/Sapyst/quilly"
license=('MIT')
_comum=('python' 'python-gobject' 'gtk4' 'libadwaita' 'webkitgtk-6.0' 'xdg-utils' 'desktop-file-utils' 'shared-mime-info')
_noty=('hunspell' 'enchant')
_artry=('python-docx' 'python-markdown' 'fontconfig')
_volury=('python-markdown')
_array=('libvips')      # vips: o Lanczos do Upscale

# instala um app: arquivos, lançador, .desktop, ícones, tipos de arquivo
_instalar_app() {
    local app=\$1 Nome=\$2 mimes=\$3 descricao=\$4 categorias=\$5
    install -d "\$pkgdir/usr/share/arch-quilly/\$app"
    cp -r "\$startdir/arvore/\$app/." "\$pkgdir/usr/share/arch-quilly/\$app/"
    install -Dm755 /dev/stdin "\$pkgdir/usr/bin/\$app" <<LANCADOR
#!/bin/sh
# \$Nome (Arch Quilly). Mensagens do app vão pra ~/.cache/arch-quilly/\$app.log
mkdir -p "\\\${XDG_CACHE_HOME:-\\\$HOME/.cache}/arch-quilly"
exec python3 /usr/share/arch-quilly/\$app/\$app.py "\\\$@" 2>>"\\\${XDG_CACHE_HOME:-\\\$HOME/.cache}/arch-quilly/\$app.log"
LANCADOR
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
        extra_mime='  <mime-type type="application/x-array">
    <comment>Array image (layers)</comment>
    <sub-class-of type="application/json"/>
    <icon name="com.archquilly.Array"/>
    <glob pattern="*.array"/>
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
    _instalar_app array Array "application/x-array;image/png;image/jpeg;image/webp;image/gif;image/bmp;image/tiff;" "Image editor with layers and Photoshop-style shortcuts" "Graphics;RasterGraphics;2DGraphics;"
}
package_arch-noty() {
    pkgdesc="Noty (Arch Quilly): editor de texto simples com vidro — .hyp, Markdown e texto"
    depends=("\${_comum[@]}" "\${_noty[@]}")
    optdepends=('hunspell-en_us: corretor em inglês' 'hunspell-es_any: corretor em espanhol' 'hunspell-fr-comprehensive: corretor em francês')
    conflicts=(arch-quilly)
    _instalar_app noty Noty "application/x-hyp;text/markdown;text/plain;text/html;" "Editor de texto simples com vidro: .hyp, Markdown e texto" "Office;TextEditor;Utility;"
}
package_arch-artry() {
    pkgdesc="Artry (Arch Quilly): editor de documentos ricos e código, com régua coordenada"
    depends=("\${_comum[@]}" "\${_artry[@]}")
    conflicts=(arch-quilly)
    _instalar_app artry Artry "application/x-hyp;application/x-artry;text/plain;text/markdown;application/rtf;application/vnd.openxmlformats-officedocument.wordprocessingml.document;" "Editor de documentos ricos e código, com régua coordenada" "Office;WordProcessor;TextEditor;"
}
package_arch-array() {
    pkgdesc="Array (Arch Quilly): image editor with layers and Photoshop-style shortcuts"
    depends=("\${_comum[@]}" "\${_array[@]}")
    conflicts=(arch-quilly)
    _instalar_app array Array "application/x-array;image/png;image/jpeg;image/webp;image/gif;image/bmp;image/tiff;" "Image editor with layers and Photoshop-style shortcuts" "Graphics;RasterGraphics;2DGraphics;"
}
package_arch-volury() {
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
