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
| **Array** | `array image` | A native image editor: layers, masks, adjustments, type, PSD, camera RAW and AI upscale (built on PhotoCraft). |

The apps are GTK4 + libadwaita windows around a WebKit view; each interface is HTML/JS, the "native side" is a
small Python file. No emoji anywhere, neutral colors, dark by default.

## Install (Arch Linux)

Everything in one file (the hub plus the four apps):

```bash
sudo pacman -U arch-quilly-1.1.0-1-x86_64.pkg.tar.zst
```

Or a single app, without the hub (each one is independent):

```bash
sudo pacman -U arch-array-1.1.0-1-x86_64.pkg.tar.zst
```

Packages are on the [Releases](https://github.com/Sapyst/quilly/releases) page. `pacman` pulls the
dependencies (python-gobject, gtk4, libadwaita, webkitgtk-6.0, and for the writing apps hunspell/enchant,
python-docx and python-markdown; Vulkan, GTK3, curl and libarchive for Array). The suite package and the single-app packages do not mix: the suite
already contains the apps. Remove with `sudo pacman -R arch-quilly` (or `arch-array`, …).

The suite registers the file types `.hyp` (Hyper document), `.artry` and `.pcraft` (Array document) with their icons.

## Array

<img src="icones/array.png" width="64" align="right" alt="">

Array 2 is a native image editor (Rust, drawn on the GPU through wgpu/Vulkan), built on
[PhotoCraft](https://github.com/storytold/photocraft) by the ArtCraft team (MIT OR Apache-2.0). PhotoCraft brings the
engine: layers and groups, masks, adjustment layers, layer styles, live type, vectors and shapes, brushes (and `.abr`
brush import), smart objects, camera RAW, real PSD files, history, and a native `.pcraft` document. Array is our fork of
it, with:

- **the Aero look**: Aero Dark and Aero Light, the desktop's own greys, rounder corners and the desktop's accent colour
  (read from `hypr-tema`, monochrome by default); it follows the system's light/dark setting;
- **Upscale with AI** (below);
- the suite's name, icon and links, and its settings in `~/.config/arch-quilly/array`.

The fork lives outside this repository (`ARRAY_SRC`, by default `~/.local/src/array`, branch `array`, with
`upstream` = PhotoCraft); `construir.sh` builds it with `cargo build --release -p photocraft` when the binary is
missing or older than the code. To take PhotoCraft's improvements: `git fetch upstream && git rebase upstream/main`
in the fork, then run `construir.sh` again.

### Upscale (AI, Lanczos or Preserve Details)

Image → **Upscale…** (`Ctrl+Alt+U`) enlarges the whole document 2×, 3× or 4× as one background job and one undo
step: every layer, mask, vector, effect and guide scales together.

- **AI: photo** / **AI: art and drawings**: Real-ESRGAN (`realesrgan-x4plus` / `realesrgan-x4plus-anime`) on the
  GPU through Vulkan redraws every pixel layer from its original pixels, inventing fine detail instead of blurring.
  Image → **Install AI Upscale Engine…** downloads the official release (45 MB) into
  `~/.local/share/arch-quilly/array/realesrgan/` (Array 1's copy is reused).
- **Lanczos** and **Preserve Details**: classic resampling, no AI.

The AI engine is a separate process per layer that starts, works and exits: nothing stays loaded while you edit.
Its temporary files live in `~/.cache/arch-quilly/array/upscale/` (disk, not RAM) and are removed afterwards.

### Pen tablets

Pressure works on X11 and under XWayland (Preferences › Performance › Linux display server: X11). Native Wayland
tablet input is not there yet.

### Array 1

The first Array (miniPaint inside a GTK4 + WebKit shell) is still in `array/`, no longer packaged.

## Where things are

- Data: `~/.local/share/arch-quilly/<app>/` (settings, Noty's theme, recent files). Logs: `~/.cache/arch-quilly/<app>.log`.
- Code: `quilly/`, `noty/`, `artry/`, `volury/`, `array/` (each with `ui/` = the interface) and `comum/` (the shared
  GTK4 + WebKit shell, `archhost.py`). To run from the folder without installing: `python3 noty/noty.py`.
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
- Array 2: a fork of [PhotoCraft](https://github.com/storytold/photocraft) by the ArtCraft team, MIT OR Apache-2.0;
  its licenses, NOTICE and attributions are installed in `/usr/share/licenses/<package>/photocraft/`. The ArtCraft
  marks are not used.
- Array 1's engine: [miniPaint](https://github.com/viliusle/miniPaint) by Vilius L., MIT, vendored unmodified in
  `array/ui/dist` (see `array/ui/UPSTREAM.txt`).
- The pt-BR dictionary in `noty/hunspell/` is the Vero dictionary by its authors (see `README_pt_BR.txt` there).
- Design cues for Array's theme come from Pixelorama; no code is shared.
