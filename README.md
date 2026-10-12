<div align="center">

```
   █▀█ █ █ █ █   █   █▄█
   ▀▀█ █▄█ █ █▄▄ █▄▄  █
```

**Quilly** · a small suite of desktop apps for writing and images

Arch Linux first (`linux` packages in this repo) · the Windows originals live in `windows/`

</div>

| App | Command | What it does |
|---|---|---|
| **Quilly** | `quilly` | The hub: opens the apps and gathers every recent document of the suite in one place. |
| **Noty** | `noty file` | A plain, glassy text editor for `.hyp`, Markdown, `.txt` and `.html`, with a pt-BR spell checker built in. |
| **Artry** | `artry file` | Rich documents and code: a coordinated ruler with sub-rulers, page mode, `.artry`, `.hyp`, `.md`, `.rtf` and `.docx` with formatting, PDF and printing. |
| **Volury** | `volury file` | An EPUB 2/3 e-book editor: chapters, images, CSS, table of contents, validation; imports `.hyp` with its attachments. |
| **Array** | `array image` | An image editor with layers, selection, brushes, text and effects, driven by **Photoshop shortcuts** and tool names. |

The apps are GTK4 + libadwaita windows around a WebKit view; each interface is HTML/JS, the "native side" is a
small Python file. No emoji anywhere, neutral colors, dark by default.

## Install (Arch Linux)

Everything in one file (the hub plus the four apps):

```bash
sudo pacman -U arch-quilly-1.0.0-1-any.pkg.tar.zst
```

Or a single app, without the hub (each one is independent):

```bash
sudo pacman -U arch-array-1.0.0-1-any.pkg.tar.zst
```

Packages are on the [Releases](https://github.com/Sapyst/quilly/releases) page. `pacman` pulls the
dependencies (python-gobject, gtk4, libadwaita, webkitgtk-6.0, and for the writing apps hunspell/enchant,
python-docx and python-markdown). The suite package and the single-app packages do not mix: the suite
already contains the apps. Remove with `sudo pacman -R arch-quilly` (or `arch-array`, …).

The suite registers the file types `.hyp` (Hyper document), `.artry` and `.array` with their icons.

## Array

<img src="icones/array.png" width="64" align="right" alt="">

Array is for people who have Photoshop in their fingers. The single-key tools and the Ctrl combinations are the
Photoshop defaults; the tools carry the Photoshop names; `Space` pans, `Z` zooms, `Tab` hides the panels,
`[` and `]` change the brush size, `X` swaps the colors, `D` resets them.

The engine is [miniPaint](https://github.com/viliusle/miniPaint) (MIT), used as an unmodified build: layers,
selection, brushes, clone stamp, text, shapes, gradients, effects, history. Array adds the theme (inspired by
[Pixelorama](https://github.com/Orama-Interactive/Pixelorama)'s flat dark look), the keymap, the tool bar order,
the system open/save dialogs and the `.array` format (miniPaint's JSON with the layers intact).

| Key | Tool | Key | Tool |
|---|---|---|---|
| `V` | Move | `S` | Clone Stamp |
| `M` | Rectangular Marquee | `E` / `Shift+E` | Eraser / Magic Eraser |
| `C` | Crop | `G` / `Shift+G` | Paint Bucket / Gradient |
| `I` | Eyedropper | `O` | Sponge (desaturate) |
| `B` / `Shift+B` | Brush / Pencil | `T` | Type |
| `U` / `Shift+U` | Shapes / next shape | `P` | Pen (Bezier curve) |
| `H`, `Space` | Hand (pan) | `Z` (+`Alt`) | Zoom in (out) |

| Keys | Action | Keys | Action |
|---|---|---|---|
| `Ctrl+N` `Ctrl+O` `Ctrl+S` | New, Open, Save | `Ctrl+Shift+S` | Save As |
| `Ctrl+Alt+Shift+S` | Export (quality, preview) | `Ctrl+P` `Ctrl+Q` | Print, Quit |
| `Ctrl+Z` `Ctrl+Shift+Z` | Undo, Redo | `Ctrl+A` `Ctrl+D` | Select All, Deselect |
| `Ctrl+C` `Ctrl+X` `Ctrl+V` | Copy, Cut, Paste | `Delete` | Clear selection |
| `Ctrl+T` | Transform (Move tool handles) | `Ctrl+Shift+N` `Ctrl+J` | New layer, Duplicate layer |
| `Ctrl+E` `Ctrl+Shift+E` | Merge down, Flatten | `Ctrl+]` `Ctrl+[` `Ctrl+,` | Layer up, down, hide/show |
| `Ctrl+I` `Ctrl+Shift+U` | Invert, Desaturate | `Ctrl+U` `Ctrl+L` `Ctrl+B` | Color corrections |
| `Ctrl+Shift+L` `Ctrl+Shift+B` | Auto adjust | `Ctrl+Alt+Shift+B` | Black and White |
| `Ctrl+Alt+I` `Ctrl+Alt+C` | Image size, Canvas size | `Ctrl+R` `Ctrl+'` `Ctrl+;` | Rulers, Grid, Guides |
| `Ctrl++` `Ctrl+-` `Ctrl+0` `Ctrl+1` | Zoom in, out, fit, 100% | `F6` `F7` `F8` | Color, Layers, Info panels |
| `Ctrl+K` | Preferences | `Ctrl+Alt+Shift+K` | Keyboard shortcuts |
| `Ctrl+Shift+X` | Liquify (bulge/pinch) | `Shift+F5` | Content fill |

Keys that Photoshop uses for tools Array does not have (Lasso `L`, Magic Wand `W`, Healing `J`, …) do nothing
instead of triggering something else. The full list is in the app: Help → Keyboard Shortcuts.

## Where things are

- Data: `~/.local/share/arch-quilly/<app>/` (settings, Noty's theme, recent files). Logs: `~/.cache/arch-quilly/<app>.log`.
- Code: `quilly/`, `noty/`, `artry/`, `volury/`, `array/` (each with `ui/` = the interface) and `comum/` (the shared
  GTK4 + WebKit shell, `archhost.py`). To run from the folder without installing: `python3 array/array.py`.
  `ARCH_DEV=1` turns on the web inspector.
- `windows/`: the original C# (.NET + WinForms + WebView2) sources of Quilly, Noty, Artry and Volury, as they were
  on Windows, without build output. `build.ps1` in each folder rebuilds them.
- `icones/`: the app icons; `array/icon.py` regenerates Array's.

## Build the packages again

```bash
./construir.sh
```

Writes `dist/arch-quilly-*.pkg.tar.zst` and one package per app. Needs `base-devel` (makepkg) and
python-gobject with GdkPixbuf for the icon sizes.

## Credits and licenses

- Quilly, Noty, Artry, Volury and Array: [MIT](LICENSE).
- Array's engine: [miniPaint](https://github.com/viliusle/miniPaint) by Vilius L., MIT, vendored unmodified in
  `array/ui/dist` (see `array/ui/UPSTREAM.txt`).
- The pt-BR dictionary in `noty/hunspell/` is the Vero dictionary by its authors (see `README_pt_BR.txt` there).
- Design cues for Array's theme come from Pixelorama; no code is shared.
