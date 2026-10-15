<div align="center">

```
   █▀█ █ █ █ █   █   █▄█
   ▀▀█ █▄█ █ █▄▄ █▄▄  █
```

**Quilly** · a small suite of desktop apps for writing, images and video

Arch Linux first (`linux` packages in this repo) · the Windows originals live in `windows/`

</div>

| App | Command | What it does |
|---|---|---|
| **Quilly** | `quilly` | The hub: opens the apps and gathers every recent document of the suite in one place. |
| **Noty** | `noty file` | A plain, glassy text editor for `.hyp`, Markdown, `.txt` and `.html`, with a pt-BR spell checker built in. |
| **Artry** | `artry file` | Rich documents and code: a coordinated ruler with sub-rulers, page mode, `.artry`, `.hyp`, `.md`, `.rtf` and `.docx` with formatting, PDF and printing. |
| **Volury** | `volury file` | An EPUB 2/3 e-book editor: chapters, images, CSS, table of contents, validation; imports `.hyp` with its attachments. |
| **Fotry** | `fotry image` | A native image editor: layers, masks, adjustments, type, PSD, camera RAW and AI upscale (built on PhotoCraft). |
| **Takery** | `takery video` | A native video editor: timeline, color, audio, titles, hardware decoding/encoding and **one-click auto captions** on the GPU (built on FilmCraft). |

Every app has its own initial and the same icon design (q n a v f t). Quilly, Noty, Artry and Volury are GTK4 +
libadwaita windows around a WebKit view (HTML/JS interface, a small Python native side); Fotry and Takery are
native Rust apps drawn on the GPU. No emoji anywhere, neutral colors, dark by default, the desktop's accent.

## Install (Arch Linux)

Everything in one file (the hub plus the five apps):

```bash
sudo pacman -U arch-quilly-1.2.0-1-x86_64.pkg.tar.zst
```

Or a single app, without the hub (each one is independent):

```bash
sudo pacman -U arch-takery-1.2.0-1-x86_64.pkg.tar.zst
```

Packages are on the [Releases](https://github.com/Sapyst/quilly/releases) page.

**Updates from the Quilly hub.** Quilly reads the latest release of this repository (GitHub's public API, no
account), shows "Atualizar vX.Y" on the cards when there is a newer version, downloads the package, checks it
against the release's `SHA256SUMS` and opens a terminal with `sudo pacman -U` (the administrator password is only
ever typed in the terminal). With the suite installed it updates everything at once; with single-app packages it
updates each installed one; "Instalar" installs a missing app. Publishing a release here updates everyone's suite. `pacman` pulls the
dependencies (python-gobject, gtk4, libadwaita, webkitgtk-6.0, and for the writing apps hunspell/enchant,
python-docx and python-markdown; Vulkan and GTK3 for Fotry and Takery, whisper-cpp and ggml-vulkan for Takery's captions). The suite package and the single-app packages do not mix: the suite
already contains the apps. Remove with `sudo pacman -R arch-quilly` (or `arch-fotry`, …).

The suite registers the file types `.hyp` (Hyper document), `.artry`, `.pcraft` (Fotry document) and `.fcproj` (Takery project) with their icons.

## Fotry

<img src="icones/fotry.png" width="64" align="right" alt="">

Fotry (formerly Array) is a native image editor (Rust, drawn on the GPU through wgpu/Vulkan), built on
[PhotoCraft](https://github.com/storytold/photocraft) by the ArtCraft team (MIT OR Apache-2.0). PhotoCraft brings the
engine: layers and groups, masks, adjustment layers, layer styles, live type, vectors and shapes, brushes (and `.abr`
brush import), smart objects, camera RAW, real PSD files, history, and a native `.pcraft` document. Fotry is our fork of
it, with:

- **the Aero look**: Aero Dark and Aero Light, the desktop's own greys, rounder corners and the desktop's accent colour
  (read from `hypr-tema`, monochrome by default); it follows the system's light/dark setting;
- **Upscale with AI** (below);
- the suite's name, icon and links, and its settings in `~/.config/arch-quilly/fotry`.

The fork lives outside this repository (`FOTRY_SRC`, by default `~/.local/src/fotry`, branch `fotry`, with
`upstream` = PhotoCraft); `construir.sh` builds it with `cargo build --release -p photocraft` when the binary is
missing or older than the code. To take PhotoCraft's improvements: `git fetch upstream && git rebase upstream/main`
in the fork, then run `construir.sh` again.

### Upscale (AI, Lanczos or Preserve Details)

Image → **Upscale…** (`Ctrl+Alt+U`) enlarges the whole document 2×, 3× or 4× as one background job and one undo
step: every layer, mask, vector, effect and guide scales together.

- **AI: photo** / **AI: art and drawings**: Real-ESRGAN (`realesrgan-x4plus` / `realesrgan-x4plus-anime`) on the
  GPU through Vulkan redraws every pixel layer from its original pixels, inventing fine detail instead of blurring.
  Image → **Install AI Upscale Engine…** downloads the official release (45 MB) into
  `~/.local/share/arch-quilly/fotry/realesrgan/` (Array's earlier copy is reused).
- **Lanczos** and **Preserve Details**: classic resampling, no AI.

The AI engine is a separate process per layer that starts, works and exits: nothing stays loaded while you edit.
Its temporary files live in `~/.cache/arch-quilly/fotry/upscale/` (disk, not RAM) and are removed afterwards.

### Pen tablets

Pressure works on X11 and under XWayland (Preferences › Performance › Linux display server: X11). Native Wayland
tablet input is not there yet.

### Array 1

The first image editor (Array: miniPaint inside a GTK4 + WebKit shell) is still in `array/`, no longer packaged.

## Takery

<img src="icones/takery.png" width="64" align="right" alt="">

Takery is a native video editor (Rust, wgpu/Vulkan), built on [FilmCraft](https://github.com/storytold/filmcraft)
by the ArtCraft team (MIT OR Apache-2.0): a timeline with tracks, trimming and ripple edits, color, audio mixing and
effects, titles, captions, and exports to H.264, HEVC, ProRes, DNxHR and more. Takery is our fork of it, with:

- **Auto Captions** (Sequence → Captions → Auto Captions…, `Ctrl+Alt+Shift+C`): one click transcribes the
  sequence's speech and lays it out as captions on a new caption track. The language is detected (or chosen);
  lines and characters per line are set in the dialog. It runs in the background with a progress card and Cancel.
  Speech recognition is OpenAI's Whisper (large-v3 turbo by default) on the **GPU** through whisper.cpp and Vulkan:
  14 s of speech take about 1 s on an RTX 5070. The model (574 MB) downloads once, pinned and checksum-checked.
  Captions can be exported (SRT, VTT…) or burned into the video at export.
- **Hardware acceleration by default**: VA-API and NVDEC decoding (H.264, HEVC), NVENC H.264 encoding and the GPU
  compositor for exports, each falling back to software when the hardware cannot take it.
- the Aero look (the desktop's greys and accent, rounder corners), the suite's name, icon and links, and its data in
  `~/.local/share/arch-quilly/takery`.

The fork lives in `TAKERY_SRC` (by default `~/.local/src/takery`, branch `takery`, `upstream` = FilmCraft); `construir.sh`
builds it with `cargo build --release -p filmcraft --features whisper`.

## Where things are

- Data: `~/.local/share/arch-quilly/<app>/` (settings, Noty's theme, recent files). Logs: `~/.cache/arch-quilly/<app>.log`.
- Code: `quilly/`, `noty/`, `artry/`, `volury/`, `array/` (each with `ui/` = the interface) and `comum/` (the shared
  GTK4 + WebKit shell, `archhost.py`). To run from the folder without installing: `python3 noty/noty.py`.
  `ARCH_DEV=1` turns on the web inspector.
- `windows/`: the original C# (.NET + WinForms + WebView2) sources of Quilly, Noty, Artry and Volury, as they were
  on Windows, without build output. `build.ps1` in each folder rebuilds them.
- `icones/`: the app icons; `icones/gerar.py` regenerates them all in the same design.
- `fotry/`, `takery/`: the two native apps' code, as git submodules of their forks ([Sapyst/fotry](https://github.com/Sapyst/fotry), [Sapyst/takery](https://github.com/Sapyst/takery)): `git clone --recursive`, or `git submodule update --init`.

## Build the packages again

```bash
./construir.sh
```

Writes `dist/arch-quilly-*.pkg.tar.zst`, one package per app and `SHA256SUMS`. Needs `base-devel` (makepkg),
python-gobject with GdkPixbuf for the icon sizes, and Rust (`rust`) for Fotry and Takery, built from
`FOTRY_SRC` / `TAKERY_SRC` (default `~/.local/src/…`, else the submodules). To publish a version: bump
`quilly/VERSAO`, run `./construir.sh`, then create the release `vX.Y.Z` with every `dist/*-X.Y.Z-*` package and
`dist/SHA256SUMS` (`gh release create vX.Y.Z dist/*-X.Y.Z-1-*.pkg.tar.zst dist/SHA256SUMS`).

## Credits and licenses

- Quilly, Noty, Artry, Volury, Array 1 and our changes in Fotry and Takery: [MIT](LICENSE).
- Fotry: a fork of [PhotoCraft](https://github.com/storytold/photocraft) by the ArtCraft team, MIT OR Apache-2.0;
  its licenses, NOTICE and attributions are installed in `/usr/share/licenses/<package>/photocraft/`.
- Takery: a fork of [FilmCraft](https://github.com/storytold/filmcraft) by the ArtCraft team, MIT OR Apache-2.0;
  licenses in `/usr/share/licenses/<package>/filmcraft/`. Whisper weights: MIT (OpenAI), ggml conversion by
  whisper.cpp (MIT), downloaded on first use, never bundled.
- The ArtCraft marks are not used in either fork.
- Array 1's engine: [miniPaint](https://github.com/viliusle/miniPaint) by Vilius L., MIT, vendored unmodified in
  `array/ui/dist` (see `array/ui/UPSTREAM.txt`).
- The pt-BR dictionary in `noty/hunspell/` is the Vero dictionary by its authors (see `README_pt_BR.txt` there).
- Design cues for Array 1's theme come from Pixelorama; no code is shared.
