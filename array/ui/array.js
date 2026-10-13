/* array.js — o Array por cima do miniPaint (que fica intacto em dist/bundle.js).
 *
 * O que vive aqui:
 *   - o mapa de atalhos do Photoshop (ferramentas de uma tecla + combinações com Ctrl), capturado ANTES dos
 *     atalhos do miniPaint, que são silenciados quando conflitam (ex.: no miniPaint "S" exporta, aqui é o carimbo);
 *   - nomes e ordem das ferramentas no estilo Photoshop, rótulos de atalho nos menus;
 *   - mão (Espaço / H / botão do meio) e zoom por clique (Z, Alt+Z);
 *   - abrir e salvar pelos diálogos do sistema (ponte com array.py), formato .array, título da janela,
 *     "há alterações não salvas?" ao fechar;
 *   - cor secundária (X troca, D volta ao preto/branco), [ ] tamanho do pincel, Tab esconde os painéis.
 * Acesso ao miniPaint: window.Layers (camadas), Layers.Base_gui (GUI: módulos, ferramentas, zoom, cores),
 * window.AppConfig, window.State (histórico), window.FileOpen / window.FileSave.
 */
(() => {
	'use strict';

	const host = window.chrome && window.chrome.webview;
	const send = (m) => { if (host) host.postMessage(m); };
	let L, G, cfg, tools, preview, FileOpen, FileSave, State;
	const doc = { path: null, name: 'untitled', dirty: false };
	let secondary = '#ffffff';
	let pending = null;                 // o que está esperando um caminho do diálogo "Salvar como"

	// ---------- ferramentas: nome Photoshop, tecla e ordem na barra ----------
	const TOOLS = [
		['select', 'Move Tool (V)'],
		['selection', 'Rectangular Marquee Tool (M)'],
		['crop', 'Crop Tool (C)'],
		['pick_color', 'Eyedropper Tool (I)'],
		null,
		['brush', 'Brush Tool (B)'],
		['pencil', 'Pencil Tool (B)'],
		['clone', 'Clone Stamp Tool (S)'],
		['erase', 'Eraser Tool (E)'],
		['magic_erase', 'Magic Eraser Tool (E)'],
		['fill', 'Paint Bucket Tool (G)'],
		['gradient', 'Gradient Tool (G)'],
		['blur', 'Blur Tool'],
		['sharpen', 'Sharpen Tool'],
		['desaturate', 'Sponge Tool (O)'],
		null,
		['text', 'Type Tool (T)'],
		['shape', 'Shape Tools (U)'],
		['bulge_pinch', 'Liquify (Ctrl+Shift+X)'],
		null,
		['media', 'Stock Images'],
		['animation', 'Animation'],
	];
	const SHAPES = ['rectangle', 'ellipse', 'line', 'arrow'];

	// rótulos de atalho nos menus (nome do item -> texto); null = tira o rótulo que o miniPaint mostra
	const MENU_SHORTCUTS = {
		'New': 'Ctrl+N', 'Open File': 'Ctrl+O', 'Export': 'Ctrl+Alt+Shift+S', 'Save As': 'Ctrl+Shift+S',
		'Print': 'Ctrl+P', 'Undo': 'Ctrl+Z', 'Redo': 'Ctrl+Shift+Z', 'Delete Selection': 'Backspace',
		'Copy to Clipboard': 'Ctrl+C', 'Paste': 'Ctrl+V', 'Select All': 'Ctrl+A', 'Zoom In': 'Ctrl++',
		'Zoom Out': 'Ctrl+-', 'Original Size': 'Ctrl+1', 'Fit Window': 'Ctrl+0', 'Grid': "Ctrl+'",
		'Ruler': 'Ctrl+R', 'Full Screen': 'F', 'Information': 'Ctrl+Alt+Shift+I', 'Canvas Size': 'Ctrl+Alt+C',
		'Trim': null, 'Resize': 'Ctrl+Alt+I', 'Color Corrections': 'Ctrl+U', 'Auto Adjust Colors': 'Ctrl+Shift+L',
		'Duplicate': 'Ctrl+J', 'Show / Hide': 'Ctrl+,', 'Merge Down': 'Ctrl+E', 'Flatten Image': 'Ctrl+Shift+E',
		'Grayscale': 'Ctrl+Shift+U', 'Negative': 'Ctrl+I', 'Black and White': 'Ctrl+Alt+Shift+B',
		'Content Fill': 'Shift+F5', 'Search': 'Ctrl+F', 'Settings': 'Ctrl+K', 'Keyboard Shortcuts': 'Ctrl+Alt+Shift+K',
		'Up': 'Ctrl+]', 'Down': 'Ctrl+[', 'Upscale…': 'Ctrl+Alt+U',
	};
	// "New" do menu Layer (mesmo nome do File > New): tratado pelo pai
	const MENU_SHORTCUTS_BY_PARENT = { 'Layer/New': 'Ctrl+Shift+N' };
	const MENU_HIDE = new Set(['Open Test Template', 'Open from Webcam', 'Quick Save', 'Quick Load']);
	const MENU_RENAME = { 'Composition': 'Blending Mode' };

	// ---------- utilidades ----------
	const $ = (s) => document.querySelector(s);
	const mod = (k) => G.modules[k];
	const run = (k, fn, ...a) => { const m = mod(k); if (m && m[fn]) return m[fn](...a); toast('Not available: ' + k, 'error'); };
	const visivel = (el) => !!el && el.getClientRects().length > 0;      // offsetParent não serve: os popups são position:fixed
	const popupOpen = () => [...document.querySelectorAll('#popups .popup')].some(visivel);
	let statusTimer = null;
	function status(text, ms = 1600) {
		const el = $('#array_status');
		if (!el) return;
		el.textContent = text;
		el.classList.add('on');
		clearTimeout(statusTimer);
		statusTimer = setTimeout(() => el.classList.remove('on'), ms);
	}
	function toast(text, kind = 'message') {
		if (window.alertify && window.alertify[kind]) window.alertify[kind](text);
		else status(text, 2500);
	}
	function isTyping(el) {
		if (!el || el === document.body) return false;
		const tag = el.tagName;
		if (tag === 'TEXTAREA' || el.isContentEditable) return true;
		if (tag === 'INPUT') return !['radio', 'checkbox', 'button', 'range', 'color', 'submit'].includes(el.type);
		return false;
	}
	function visiblePopup() {
		return [...document.querySelectorAll('#popups .popup')].filter(visivel).pop() || null;
	}

	// ---------- ferramentas ----------
	function setTool(name) {
		if (tools.active_tool === name) return;
		tools.activate_tool(name);
		const t = TOOLS.find((x) => x && x[0] === name);
		status(t ? t[1] : name);
	}
	const T = (name) => () => setTool(name);
	function cycle(names) {
		return () => {
			const i = names.indexOf(tools.active_tool);
			setTool(names[(i + 1) % names.length]);
		};
	}
	function arrangeTools() {
		const bar = $('#tools_container');
		if (!bar) return;
		for (const t of TOOLS) {
			if (t === null) {
				const sep = document.createElement('span');
				sep.className = 'array-sep';
				bar.appendChild(sep);
				continue;
			}
			const el = document.getElementById(t[0]);
			if (!el) continue;
			el.title = t[1];
			bar.appendChild(el);              // appendChild move: a ordem final é a desta lista
		}
		// o que sobrou (formas escondidas etc.) fica no fim, como estava
	}

	// tamanho do pincel: [ e ] (passo cresce com o tamanho, como no Photoshop)
	function brushSize(direction) {
		const t = tools.action_data();
		const attrs = t.attributes || {};
		if (!('size' in attrs)) { status('This tool has no size'); return; }
		const obj = typeof attrs.size === 'object';
		let v = obj ? attrs.size.value : attrs.size;
		const step = v < 10 ? 1 : v < 100 ? 5 : 10;
		v = Math.max(1, Math.min(999, v + direction * step));
		if (obj) attrs.size.value = v; else attrs.size = v;
		tools.show_action_attributes();
		if (t.on_update && tools.tools_modules[t.name]) {
			tools.tools_modules[t.name].object[t.on_update]({ key: 'size', value: v });
		}
		status('Size ' + v);
	}

	// ---------- cores ----------
	function setColor(hex) { G.GUI_colors.set_color({ hex }); }
	function swapColors() { const c = cfg.COLOR; setColor(secondary); secondary = c; status('Colors swapped'); }
	function defaultColors() { setColor('#000000'); secondary = '#ffffff'; status('Default colors'); }

	// ---------- vista: zoom e mão ----------
	function zoomAt(direction, clientX, clientY) {
		const c = $('#canvas_minipaint');
		if (c && clientX != null) {
			const r = c.getBoundingClientRect();
			preview.zoom_data.x = clientX - r.left;
			preview.zoom_data.y = clientY - r.top;
		} else {
			preview.set_center_zoom();
		}
		preview.zoom(direction);
	}
	function zoomTo(percent) { preview.set_center_zoom(); preview.zoom(percent); }
	function pan(dx, dy) {
		const p = L.get_world_coords(0, 0);
		preview.zoom_data.move_pos = { x: p.x - dx / cfg.ZOOM, y: p.y - dy / cfg.ZOOM };
		cfg.need_render = true;
	}
	let hand = false, zoomMode = false, spaceHeld = false, panning = null;
	function setHand(on) {
		hand = on;
		document.body.classList.toggle('array-hand', on);
		if (on) { zoomMode = false; document.body.classList.remove('array-zoom'); }
	}
	function setZoomMode(on) {
		zoomMode = on;
		document.body.classList.toggle('array-zoom', on);
		if (on) { hand = false; document.body.classList.remove('array-hand'); }
	}
	function viewEvents() {
		const area = $('#main_wrapper');
		if (!area) return;
		area.addEventListener('mousedown', (e) => {
			const middle = e.button === 1;
			if (!(hand || spaceHeld || middle || zoomMode) || popupOpen()) return;
			e.preventDefault();
			e.stopPropagation();
			if (zoomMode && !middle) {
				zoomAt(e.altKey ? -1 : +1, e.clientX, e.clientY);
				return;
			}
			panning = { x: e.clientX, y: e.clientY };
			document.body.classList.add('array-panning');
		}, true);
		window.addEventListener('mousemove', (e) => {
			if (!panning) return;
			e.stopPropagation();
			pan(e.clientX - panning.x, e.clientY - panning.y);
			panning = { x: e.clientX, y: e.clientY };
		}, true);
		window.addEventListener('mouseup', (e) => {
			if (!panning) return;
			e.stopPropagation();
			panning = null;
			document.body.classList.remove('array-panning');
		}, true);
		// Alt + roda = zoom (o miniPaint já faz roda = zoom; o Alt só muda o cursor): nada a fazer
		document.body.addEventListener('mousemove', (e) => {
			if (zoomMode) document.body.classList.toggle('array-zoom-out', e.altKey);
		});
	}

	// ---------- layout dinâmico: a janela muda, o canvas acompanha ----------
	// O miniPaint só redimensiona o canvas ao dar zoom; encolher a janela deixava o canvas saindo pela borda.
	// Aqui, a cada resize (ou barra de opções que cresce), o canvas é refeito no espaço que há; se o zoom era
	// "ajustar à janela", ajusta de novo.
	let fitZoom = null, relayoutPending = false;
	function relayout() {
		if (relayoutPending) return;
		relayoutPending = true;
		requestAnimationFrame(() => {
			relayoutPending = false;
			const wasFit = fitZoom != null && Math.abs(cfg.ZOOM - fitZoom) < 0.005;
			G.prepare_canvas();
			if (wasFit) preview.zoom_auto(); else cfg.need_render = true;
		});
	}
	function layoutEvents() {
		window.addEventListener('resize', relayout);
		const za = preview.zoom_auto.bind(preview);
		preview.zoom_auto = function (...a) { const r = za(...a); fitZoom = cfg.ZOOM; return r; };
		const saa = tools.show_action_attributes.bind(tools);
		tools.show_action_attributes = function (...a) { const r = saa(...a); relayout(); return r; };
		new ResizeObserver(relayout).observe($('#main_wrapper'));
	}

	// ---------- navigator: proporção certa e zona visível em vermelho acinzentado ----------
	// O miniPaint desenha a prévia num retângulo fixo de 176x100 (imagem esticada) e marca a zona visível em
	// verde puro. Toda a conta dele (desenho, retângulo, clique) usa PREVIEW_SIZE, então basta manter esse
	// tamanho na proporção do documento; a cor é a nossa versão do mesmo desenho.
	const PREVIEW_BOX = { w: 176, h: 132 };
	function fitPreview() {
		const W = cfg.WIDTH, H = cfg.HEIGHT;
		if (!W || !H) return;
		const k = Math.min(PREVIEW_BOX.w / W, PREVIEW_BOX.h / H);
		const w = Math.max(24, Math.round(W * k)), h = Math.max(24, Math.round(H * k));
		if (preview.PREVIEW_SIZE.w === w && preview.PREVIEW_SIZE.h === h) return;
		preview.PREVIEW_SIZE = { w, h };
		const c = $('#canvas_preview');
		c.width = w; c.height = h;                       // zera o contexto: prepare_canvas refaz as opções
		const wrap = $('.canvas_preview_wrapper');
		if (wrap) { wrap.style.width = w + 'px'; wrap.style.height = h + 'px'; wrap.style.margin = '6px auto 8px auto'; }
		preview.prepare_canvas();
		cfg.need_render = true;
	}
	function navigatorEvents() {
		preview.render_preview_active_zone = function () {
			if (this.canvas_preview == undefined) this.canvas_preview = $('#canvas_preview').getContext('2d');
			const visible_w = cfg.visible_width / cfg.ZOOM, visible_h = cfg.visible_height / cfg.ZOOM;
			let rw = this.PREVIEW_SIZE.w * visible_w / cfg.WIDTH, rh = this.PREVIEW_SIZE.h * visible_h / cfg.HEIGHT;
			const start = L.get_world_coords(0, 0);
			let rx = Math.max(0, start.x / cfg.WIDTH * this.PREVIEW_SIZE.w), ry = Math.max(0, start.y / cfg.HEIGHT * this.PREVIEW_SIZE.h);
			rw = Math.min(this.PREVIEW_SIZE.w - 1, rw); rh = Math.min(this.PREVIEW_SIZE.h - 1, rh);
			if (rx + rw > this.PREVIEW_SIZE.w) rx = this.PREVIEW_SIZE.w - rw;
			if (ry + rh > this.PREVIEW_SIZE.h) ry = this.PREVIEW_SIZE.h - rh;
			if (rx == 0 && ry == 0 && rw == this.PREVIEW_SIZE.w - 1 && rh == this.PREVIEW_SIZE.h - 1) return;   // tudo visível
			const ctx = this.canvas_preview;
			ctx.lineWidth = 1;
			ctx.beginPath();
			ctx.rect(Math.round(rx) + 0.5, Math.round(ry) + 0.5, rw, rh);
			ctx.fillStyle = 'rgba(196, 118, 118, 0.26)';
			ctx.strokeStyle = '#c98080';
			ctx.fill();
			ctx.stroke();
		};
		// a cada quadro desenhado, confere se o documento mudou de tamanho (novo, abrir, cortar, redimensionar)
		const render = L.render.bind(L);
		L.render = function (force) { if (force === true && cfg.need_render) fitPreview(); return render(force); };
		fitPreview();
	}

	// ---------- upscale: Lanczos (vips) ou IA com detalhe fino (Real-ESRGAN) ----------
	// O trabalho pesado roda em processo externo disparado pelo array.py, que termina ao fim de cada camada:
	// nada de modelo carregado enquanto você edita. Camadas de imagem vão pro motor; texto e formas são
	// reescalados pela própria lógica de Resize do miniPaint (continuam editáveis). Tudo numa ação só (Ctrl+Z).
	const UP_MAX_SIDE = 8192, UP_MAX_MP = 40;
	const UP_METHODS = ['Lanczos (sharp, no AI)', 'AI – fine detail (Real-ESRGAN)'];
	const UP_SCALES = ['2x', '3x', '4x'];
	const UP_MODELS = ['Photo', 'Illustration / anime'];
	let up = null, engineWaiter = null;
	const POPUP = () => mod('help/about').POP;
	function imageLayers() { return cfg.layers.filter((l) => l.type === 'image' && (l.link || l.link_canvas)); }
	function layerBitmap(layer) {
		const src = layer.link_canvas || layer.link;
		const c = document.createElement('canvas');
		c.width = layer.width_original || src.width; c.height = layer.height_original || src.height;
		c.getContext('2d').drawImage(src, 0, 0, c.width, c.height);
		return c;
	}
	function upOutcome(scale) {
		let mp = 0, tooBig = false;
		for (const l of imageLayers()) {
			const w = (l.width_original || l.link.width) * scale, h = (l.height_original || l.link.height) * scale;
			mp += w * h / 1e6;
			if (w > UP_MAX_SIDE || h > UP_MAX_SIDE || w * h > UP_MAX_MP * 1e6) tooBig = true;
		}
		return { w: Math.round(cfg.WIDTH * scale), h: Math.round(cfg.HEIGHT * scale), mp, tooBig };
	}
	function upInfoHtml() {
		const rows = [2, 3, 4].map((s) => {
			const o = upOutcome(s);
			return `<div style="display:flex;justify-content:space-between;padding:2px 0${o.tooBig ? ';opacity:.5' : ''}">` +
				`<span>${s}x</span><span>${o.w} × ${o.h} px · ${o.mp.toFixed(1)} MP in layers${o.tooBig ? ' · too big' : ''}</span></div>`;
		}).join('');
		return `<div style="font-size:12px;color:var(--text-color-muted)">${rows}` +
			`<div style="margin-top:6px">Limit per layer: ${UP_MAX_SIDE} px per side, ${UP_MAX_MP} MP. The AI runs in a separate process on the GPU and is unloaded when done.</div></div>`;
	}
	function upscaleDialog() {
		if (up) { toast('An upscale is already running', 'warning'); return; }
		if (!imageLayers().length) { toast('No image layer to upscale', 'error'); return; }
		POPUP().show({
			title: 'Upscale',
			className: 'array-upscale',
			params: [
				{ name: 'method', title: 'Method:', type: 'select', values: UP_METHODS, value: UP_METHODS[0] },
				{ name: 'scale', title: 'Scale:', type: 'select', values: UP_SCALES, value: UP_SCALES[0] },
				{ name: 'model', title: 'AI model:', type: 'select', values: UP_MODELS, value: UP_MODELS[0] },
				{ title: '', html: upInfoHtml() },
			],
			on_finish: (p) => runUpscale(p),
		});
		const primeiro = document.getElementById('pop_data_method');
		if (primeiro) primeiro.focus();
	}
	function askEngine() {
		return new Promise((resolve) => { engineWaiter = resolve; send({ type: 'upscale-engine?' }); });
	}
	function progressPopup(text) {
		POPUP().show({
			title: 'Upscaling',
			className: 'array-progress',
			params: [{ title: '', html: `<div id="array_up_txt" style="padding:6px 0 2px">${text}</div>` }],
			on_cancel: () => cancelUpscale(),
		});
	}
	function setProgress(text) {
		const el = document.getElementById('array_up_txt');
		if (el) el.textContent = text; else status(text, 3000);
	}
	function cancelUpscale() {
		if (!up) return;
		up.cancelled = true;
		send({ type: 'upscale-cancel' });
		send({ type: 'upscale-cleanup', job: up.job });
		up = null;
		status('Upscale cancelled');
	}
	async function runUpscale(p) {
		const scale = parseInt(p.scale, 10) || 2;
		const method = String(p.method).startsWith('AI') ? 'ai' : 'lanczos';
		const model = String(p.model).startsWith('Illus') ? 'art' : 'photo';
		const o = upOutcome(scale);
		if (o.tooBig) { toast(`Too big at ${scale}x (limit ${UP_MAX_SIDE} px per side, ${UP_MAX_MP} MP per layer). Try a smaller scale.`, 'error'); return; }
		if (method === 'ai') {
			const eng = await askEngine();
			if (!eng.ready) {
				const ok = await new Promise((resolve) => POPUP().show({
					title: 'AI engine',
					params: [{ title: '', html: `<p style="margin:4px 0 8px">The AI upscale uses Real-ESRGAN, which is not installed yet.</p>` +
						`<p style="margin:0;color:var(--text-color-muted)">Download it once (${eng.size_mb} MB, the official release from GitHub) into your Array profile?</p>` }],
					on_finish: () => resolve(true), on_cancel: () => resolve(false),
				}));
				if (!ok) return;
				progressPopup('Downloading the AI engine…');
				const r = await askEngineInstall();
				if (!r.ready) { POPUP().hide(); toast('The AI engine could not be installed', 'error'); return; }
			}
		}
		up = { job: String(Date.now()), scale, method, results: new Map(), cancelled: false };
		progressPopup('Preparing layers…');
		const items = [];
		for (const l of imageLayers()) {
			const c = layerBitmap(l);
			const blob = await new Promise((ok) => c.toBlob(ok, 'image/png'));
			items.push({ id: l.id, data: await blobToBase64(blob) });
		}
		if (!up || up.cancelled) return;
		setProgress(method === 'ai' ? 'AI · starting the engine…' : 'Lanczos…');
		send({ type: 'upscale', job: up.job, method, scale, model, items });
	}
	function askEngineInstall() {
		return new Promise((resolve) => { engineWaiter = resolve; send({ type: 'upscale-engine-install' }); });
	}
	function imageLayerAction(layer, img, s) {
		const id = layer.id;
		const before = { link: layer.link, link_canvas: layer.link_canvas, width_original: layer.width_original, height_original: layer.height_original,
			width: layer.width, height: layer.height, x: layer.x, y: layer.y };
		const after = { link: img, link_canvas: null, width_original: img.width, height_original: img.height,
			width: Math.round(layer.width * s), height: Math.round(layer.height * s), x: Math.round(layer.x * s), y: Math.round(layer.y * s) };
		const apply = (v) => { const l = L.get_layer(id); if (l) Object.assign(l, v); cfg.need_render = true; };
		return { action_id: 'array_upscale_layer', action_description: 'Upscale layer', is_done: false, memory_estimate: 0, database_estimate: 0,
			async do() { apply(after); this.is_done = true; }, async undo() { apply(before); this.is_done = false; },
			free() { before.link = before.link_canvas = null; } };
	}
	async function applyUpscale() {
		const s = up.scale, actions = [];
		const resize = mod('image/resize');
		const params = { width_percent: s * 100, height_percent: s * 100, mode: 'Basic', sharpen: false, layers: 'All' };
		for (const layer of cfg.layers) {
			if (layer.type === 'image') {
				const img = up.results.get(layer.id);
				if (img) actions.push(imageLayerAction(layer, img, s));
			} else if (layer.type === 'text' || layer.is_vector) {
				try { actions.push(...await resize.resize_layer(layer, params)); } catch (e) { console.warn('layer skipped', layer.name, e); }
			}
		}
		actions.push(...resize.resize_gui({ width_percent: s * 100, height_percent: s * 100 }));
		const bundle = { action_id: 'array_upscale', action_description: 'Upscale', is_done: false, memory_estimate: 0, database_estimate: 0,
			async do() { for (const a of actions) await a.do(); this.is_done = true; },
			async undo() { for (const a of actions.slice().reverse()) await a.undo(); this.is_done = false; },
			async free() { for (const a of actions) if (a.free) await a.free(); } };
		await State.do_action(bundle);
		send({ type: 'upscale-cleanup', job: up.job });
		POPUP().hide();
		preview.zoom_auto();
		status(`Upscaled ${s}x (${up.method === 'ai' ? 'AI' : 'Lanczos'}): ${cfg.WIDTH} × ${cfg.HEIGHT} px`, 3000);
		up = null;
	}
	function onUpscaleMessage(m) {
		switch (m.type) {
			case 'upscale-engine':
				if (engineWaiter) { const w = engineWaiter; engineWaiter = null; w(m); }
				break;
			case 'upscale-progress': setProgress(m.text || ''); break;
			case 'upscaled': {
				if (!up || up.cancelled || m.job !== up.job) break;
				const img = new Image();
				img.onload = () => { if (up && m.job === up.job) up.results.set(parseInt(m.id, 10), img); };
				img.onerror = () => { if (up && m.job === up.job) { POPUP().hide(); toast('Could not read the upscaled layer', 'error'); cancelUpscale(); } };
				img.src = m.url;
				break;
			}
			case 'upscale-done': {
				if (!up || up.cancelled || m.job !== up.job) break;
				const esperado = imageLayers().length;
				const espera = () => {
					if (!up) return;
					const prontos = [...up.results.values()].filter((i) => i.complete && i.naturalWidth > 0).length;
					if (prontos >= esperado) applyUpscale().catch((e) => { POPUP().hide(); toast('Upscale failed: ' + e.message, 'error'); up = null; });
					else setTimeout(espera, 60);
				};
				setProgress('Applying…');
				espera();
				break;
			}
			case 'upscale-error':
				if (engineWaiter) { const w = engineWaiter; engineWaiter = null; w({ ready: false }); }
				POPUP().hide();
				if (up && !up.cancelled) toast('Upscale: ' + (m.text || 'error'), 'error');
				up = null;
				break;
		}
	}

	// ---------- painéis ----------
	function togglePanel(target) {
		const h = document.querySelector('.block h2.toggle[data-target="' + target + '"]');
		if (h) h.click();
	}
	function toggleLayers() {
		const b = $('.sidebar_right .block.layers');
		if (b) b.style.display = b.style.display === 'none' ? '' : 'none';
	}
	function togglePanels() {
		document.body.classList.toggle('array-panels-hidden');
		relayout();
	}

	// ---------- seleção ----------
	function deselect() {
		// o miniPaint limpa a seleção no Esc da ferramenta de seleção: mandamos um Esc "de mentira"
		const ev = new KeyboardEvent('keydown', { key: 'Escape', code: 'Escape', bubbles: true });
		Object.defineProperty(ev, 'keyCode', { get: () => 27 });
		document.dispatchEvent(ev);
		cfg.need_render = true;
	}
	async function cut() {
		await run('edit/copy', 'copy_to_clipboard');
		run('edit/selection', 'delete');
	}

	// ---------- documento: abrir, salvar, título ----------
	function setTitle() {
		send({ type: 'title', text: (doc.name || 'untitled') + (doc.dirty ? '*' : '') + ' – Array' });
	}
	function markDirty(v = true) {
		if (doc.dirty === v) return;
		doc.dirty = v;
		setTitle();
	}
	function isEmptyDocument() {
		try {
			return cfg.layers.length <= 1 && L.is_layer_empty(cfg.layer.id);
		} catch (e) { return false; }
	}
	function extOf(name) { const m = /\.([a-z0-9]+)$/i.exec(name || ''); return m ? m[1].toLowerCase() : ''; }
	function flattened(fillWhite) {
		const c = document.createElement('canvas');
		c.width = cfg.WIDTH;
		c.height = cfg.HEIGHT;
		const ctx = c.getContext('2d');
		if (fillWhite) { ctx.fillStyle = '#ffffff'; ctx.fillRect(0, 0, c.width, c.height); }
		L.convert_layers_to_canvas(ctx, null, false);
		return c;
	}
	function blobToBase64(blob) {
		return new Promise((ok, bad) => {
			const r = new FileReader();
			r.onload = () => ok(String(r.result).split(',', 2)[1]);
			r.onerror = () => bad(r.error);
			r.readAsDataURL(blob);
		});
	}
	function utf8Base64(text) {
		const bytes = new TextEncoder().encode(text);
		let bin = '';
		for (let i = 0; i < bytes.length; i += 0x8000) bin += String.fromCharCode.apply(null, bytes.subarray(i, i + 0x8000));
		return btoa(bin);
	}
	// gera os dados do documento no formato da extensão; devolve base64 (ou null se o formato não dá aqui)
	async function render(ext) {
		if (ext === 'array' || ext === 'json') return utf8Base64(FileSave.export_as_json());
		const mime = { png: 'image/png', jpg: 'image/jpeg', jpeg: 'image/jpeg', webp: 'image/webp' }[ext];
		if (!mime) return null;
		const fillWhite = mime === 'image/jpeg' || cfg.TRANSPARENCY == false;
		const canvas = flattened(fillWhite);
		const blob = await new Promise((ok) => canvas.toBlob(ok, mime, 0.92));
		return blob ? blobToBase64(blob) : null;
	}
	function suggestedName(ext) {
		const base = (doc.name || (cfg.layers[0] && cfg.layers[0].name) || 'untitled').replace(/\.[a-z0-9]+$/i, '');
		return base + '.' + ext;
	}
	function save() {
		const ext = extOf(doc.path || '');
		if (doc.path && ['array', 'json', 'png', 'jpg', 'jpeg', 'webp'].includes(ext)) {
			render(ext).then((data) => {
				if (data) send({ type: 'save', path: doc.path, name: doc.name, data });
				else saveAs();
			});
			return;
		}
		saveAs();
	}
	function saveAs() {
		pending = { kind: 'doc' };
		send({ type: 'save-dialog', name: suggestedName(extOf(doc.path || '') === 'png' ? 'png' : 'array') });
	}
	async function onSavePath(path) {
		const p = pending;
		pending = null;
		if (!p || !path) return;
		let data = null;
		if (p.kind === 'blob') data = await blobToBase64(p.blob);
		else data = await render(extOf(path));
		if (!data) { toast('Use File › Export for this format', 'error'); return; }
		send({ type: 'save', path, name: path.split('/').pop(), data });
	}
	// o Export do miniPaint "baixa" um arquivo (FileSaver cria um <a download> e clica): a gente intercepta o
	// clique, pega o blob e manda pro diálogo do sistema
	function interceptDownloads() {
		const grab = (a) => {
			const name = a.download || 'image.png';
			fetch(a.href).then((r) => r.blob()).then((blob) => {
				pending = { kind: 'blob', blob, name };
				send({ type: 'save-dialog', name });
			}).catch(() => toast('Could not read the exported image', 'error'));
		};
		const isDownload = (a) => a.hasAttribute('download') && /^blob:/.test(a.href);
		const origDispatch = HTMLAnchorElement.prototype.dispatchEvent;
		HTMLAnchorElement.prototype.dispatchEvent = function (ev) {
			if (ev && ev.type === 'click' && isDownload(this)) { grab(this); return true; }
			return origDispatch.call(this, ev);
		};
		const origClick = HTMLAnchorElement.prototype.click;
		HTMLAnchorElement.prototype.click = function () {
			if (isDownload(this)) { grab(this); return; }
			return origClick.call(this);
		};
	}
	async function openFromHost(m) {
		if (m.kind === 'json') {
			await FileOpen.load_json(m.data);
		} else {
			const blob = await (await fetch(m.data)).blob();
			const file = new File([blob], m.name, { type: blob.type || 'image/png' });
			await FileOpen.open_handler({ target: { files: [file] } });
		}
		doc.path = m.path;
		doc.name = m.name;
		doc.dirty = false;
		setTitle();
		setTimeout(() => markDirty(false), 600);     // a camada entra de forma assíncrona e marcaria "alterado"
		status('Opened ' + m.name);
	}

	// ---------- diálogos nossos (usam o popup do miniPaint) ----------
	function showShortcuts() {
		const rows = [
			['V', 'Move'], ['M', 'Rectangular Marquee'], ['C', 'Crop'], ['I', 'Eyedropper'],
			['B / Shift+B', 'Brush / Pencil'], ['S', 'Clone Stamp'], ['E / Shift+E', 'Eraser / Magic Eraser'],
			['G / Shift+G', 'Paint Bucket / Gradient'], ['O', 'Sponge (desaturate)'], ['T', 'Type'],
			['U / Shift+U', 'Shapes / next shape'], ['P', 'Pen (Bezier curve)'], ['H, Space', 'Hand (pan)'],
			['Z, Alt+Z', 'Zoom in / out (click)'], ['X / D', 'Swap colors / default colors'],
			['[ / ]', 'Brush size'], ['Tab', 'Hide / show panels'], ['F', 'Full screen'],
			['Ctrl+N / O / S', 'New / Open / Save'], ['Ctrl+Shift+S', 'Save As'], ['Ctrl+Alt+Shift+S', 'Export'],
			['Ctrl+P / Ctrl+Q', 'Print / Quit'], ['Ctrl+Z / Ctrl+Shift+Z', 'Undo / Redo'],
			['Ctrl+A / Ctrl+D', 'Select all / Deselect'], ['Ctrl+C / X / V', 'Copy / Cut / Paste'],
			['Backspace', 'Clear selection'], ['Ctrl+T', 'Transform (Move tool handles)'],
			['Ctrl+Shift+N / Ctrl+J', 'New layer / Duplicate layer'], ['Ctrl+E / Ctrl+Shift+E', 'Merge down / Flatten'],
			['Ctrl+] / Ctrl+[ / Ctrl+,', 'Layer up / down / hide'], ['Ctrl+I / Ctrl+Shift+U', 'Invert / Desaturate'],
			['Ctrl+U, Ctrl+L, Ctrl+B', 'Color corrections'], ['Ctrl+Shift+L / B', 'Auto adjust'],
			['Ctrl+Alt+Shift+B', 'Black and white'], ['Ctrl+Alt+I / Ctrl+Alt+C', 'Image size / Canvas size'],
			["Ctrl+R / Ctrl+' / Ctrl+;", 'Rulers / Grid / Guides'], ['Ctrl++ / - / 0 / 1', 'Zoom in / out / fit / 100%'],
			['F6 / F7 / F8', 'Color / Layers / Info panels'], ['Ctrl+K', 'Preferences'],
			['Ctrl+Shift+X', 'Liquify'], ['Shift+F5', 'Content fill'], ['Ctrl+F', 'Search'],
			['L, W, J, Y, K, A, Q', 'Photoshop tools Array does not have: nothing happens'],
		];
		mod('help/shortcuts').POP.show({
			title: 'Keyboard Shortcuts',
			className: 'shortcuts',
			params: rows.map(([title, value]) => ({ title, value })),
		});
	}
	function showAbout() {
		mod('help/about').POP.show({
			title: 'About Array',
			params: [
				{ title: '', html: '<img style="width:56px" alt="" src="icon.png">' },
				{ title: 'Name:', html: '<span class="about-name">Array</span>' },
				{ title: 'Version:', value: '1.0.0' },
				{ title: 'What:', value: 'Image editor with layers and Photoshop-style shortcuts. Part of Quilly.' },
				{ title: 'Engine:', html: 'miniPaint by Vilius L. (MIT) — <a href="https://github.com/viliusle/miniPaint">github.com/viliusle/miniPaint</a>' },
				{ title: 'Source:', html: '<a href="https://github.com/Sapyst/quilly">github.com/Sapyst/quilly</a>' },
				{ title: 'License:', value: 'MIT' },
			],
		});
	}

	// ---------- menus: rótulos de atalho e itens escondidos ----------
	function fixMenus() {
		const nav = $('#main_menu');
		if (!nav) return;
		const parentName = () => {
			const opener = nav.querySelector('ul.menu_bar a[aria-expanded="true"] .name');
			return opener ? opener.textContent.trim() : '';
		};
		const patch = (ul) => {
			const parent = parentName();
			for (const a of ul.querySelectorAll('li > a')) {
				const nameEl = a.querySelector('.name');
				if (!nameEl) continue;
				const name = nameEl.textContent.trim();
				if (MENU_HIDE.has(name)) { a.parentElement.remove(); continue; }
				if (MENU_RENAME[name]) nameEl.textContent = MENU_RENAME[name];
				let label = MENU_SHORTCUTS_BY_PARENT[parent + '/' + name];
				if (label === undefined) label = MENU_SHORTCUTS[name];
				if (label === undefined) continue;
				let sc = a.querySelector('.shortcut');
				if (label === null) { if (sc) sc.remove(); continue; }
				if (!sc) { sc = document.createElement('span'); sc.className = 'shortcut'; a.appendChild(sc); }
				sc.textContent = label;
			}
			if (parent === 'Image' && !ul.querySelector('.array-menu-item')) {
				const resize = [...ul.querySelectorAll('li > a')].find((a) => (a.querySelector('.name') || {}).textContent === 'Resize');
				if (resize) {
					const li = document.createElement('li');
					li.innerHTML = '<span class="array-menu-item" role="menuitem" tabindex="-1"><span class="name">Upscale…</span><span class="shortcut">Ctrl+Alt+U</span></span>';
					li.firstChild.addEventListener('click', () => setTimeout(upscaleDialog, 0));
					resize.parentElement.after(li);
				}
			}
		};
		new MutationObserver((muts) => {
			for (const m of muts) for (const n of m.addedNodes) {
				if (n.nodeType === 1 && n.matches('ul.menu_dropdown')) patch(n);
			}
		}).observe(nav, { childList: true });
	}

	// ---------- o mapa de teclas ----------
	const noop = (what) => () => status(what + ': not available in Array');
	const KEYMAP = {
		// ferramentas
		'v': T('select'), 'm': T('selection'), 'c': T('crop'), 'i': T('pick_color'),
		'b': T('brush'), 'shift+b': cycle(['brush', 'pencil']),
		's': T('clone'), 'e': T('erase'), 'shift+e': cycle(['erase', 'magic_erase']),
		'g': T('fill'), 'shift+g': cycle(['fill', 'gradient']), 'o': T('desaturate'), 't': T('text'),
		'u': T('shape'), 'shift+u': cycle(SHAPES), 'p': T('bezier_curve'),
		'h': () => { setHand(!hand); status(hand ? 'Hand Tool (H): drag to pan' : 'Hand off'); },
		'z': () => { setZoomMode(!zoomMode); status(zoomMode ? 'Zoom Tool (Z): click to zoom in, Alt+click out' : 'Zoom off'); },
		'ctrl+shift+x': T('bulge_pinch'),
		'x': swapColors, 'd': defaultColors,
		'[': () => brushSize(-1), ']': () => brushSize(+1),
		'tab': togglePanels, 'f': () => run('view/full_screen', 'fs'),
		'l': noop('Lasso'), 'w': noop('Magic Wand'), 'j': noop('Healing Brush'), 'y': noop('History Brush'),
		'k': noop('Frame'), 'a': noop('Path Selection'), 'q': noop('Quick Mask'), 'n': noop('Notes'), 'r': noop('Rotate View'),
		// arquivo
		'ctrl+n': () => run('file/new', 'new'), 'ctrl+o': () => send({ type: 'open-dialog' }),
		'ctrl+s': save, 'ctrl+shift+s': saveAs,
		'ctrl+alt+shift+s': () => run('file/save', 'export'), 'ctrl+alt+shift+w': () => run('file/save', 'export'),
		'ctrl+p': () => run('file/print', 'print'), 'ctrl+q': () => send({ type: 'quit' }),
		'ctrl+w': () => run('file/new', 'new'),
		// edição
		'ctrl+z': () => State.undo(), 'ctrl+shift+z': () => State.redo(), 'ctrl+alt+z': () => State.undo(),
		'ctrl+a': () => run('edit/selection', 'select_all'), 'ctrl+d': deselect, 'ctrl+shift+i': noop('Inverse selection'),
		'ctrl+c': () => run('edit/copy', 'copy_to_clipboard'), 'ctrl+x': cut,
		'backspace': () => run('edit/selection', 'delete'),
		'ctrl+t': () => { setTool('select'); status('Transform: drag the handles, Enter applies'); },
		// camadas
		'ctrl+shift+n': () => run('layer/new', 'new'), 'ctrl+alt+shift+n': () => run('layer/new', 'new'),
		'ctrl+j': () => run('layer/duplicate', 'duplicate'), 'ctrl+e': () => run('layer/merge', 'merge'),
		'ctrl+shift+e': () => run('layer/flatten', 'flatten'), 'ctrl+]': () => run('layer/move', 'up'),
		'ctrl+[': () => run('layer/move', 'down'), 'ctrl+,': () => run('layer/visibility', 'toggle'),
		'ctrl+g': noop('Layer groups'),
		// imagem e ajustes
		'ctrl+i': () => run('effects/common/invert', 'invert'), 'ctrl+u': () => run('image/color_corrections', 'color_corrections'),
		'ctrl+l': () => run('image/color_corrections', 'color_corrections'), 'ctrl+b': () => run('image/color_corrections', 'color_corrections'),
		'ctrl+m': noop('Curves'), 'ctrl+shift+u': () => run('effects/common/grayscale', 'grayscale'),
		'ctrl+shift+l': () => run('image/auto_adjust', 'auto_adjust'), 'ctrl+shift+b': () => run('image/auto_adjust', 'auto_adjust'),
		'ctrl+alt+shift+l': () => run('image/auto_adjust', 'auto_adjust'),
		'ctrl+alt+shift+b': () => run('effects/black_and_white', 'black_and_white'),
		'ctrl+alt+i': () => run('image/resize', 'resize'), 'ctrl+alt+c': () => run('image/size', 'size'),
		'ctrl+alt+u': () => upscaleDialog(),
		'ctrl+alt+shift+i': () => run('image/information', 'information'),
		'shift+f5': () => run('tools/content_fill', 'content_fill'),
		// vista
		'ctrl+r': () => run('view/ruler', 'ruler'), "ctrl+'": () => run('view/grid', 'grid'),
		'ctrl+;': () => { cfg.guides_enabled = !cfg.guides_enabled; cfg.need_render = true; status(cfg.guides_enabled ? 'Guides on' : 'Guides off'); },
		'ctrl+h': () => { cfg.guides_enabled = !cfg.guides_enabled; cfg.need_render = true; },
		'ctrl+=': () => zoomAt(+1), 'ctrl++': () => zoomAt(+1), 'ctrl+-': () => zoomAt(-1),
		'ctrl+0': () => preview.zoom_auto(), 'ctrl+1': () => zoomTo(100), 'ctrl+alt+0': () => zoomTo(100),
		'ctrl+space': () => zoomAt(+1), 'alt+space': () => zoomAt(-1),
		'f6': () => togglePanel('toggle_colors'), 'f7': toggleLayers, 'f8': () => togglePanel('toggle_info'),
		// outros
		'ctrl+k': () => run('tools/settings', 'settings'), 'ctrl+alt+shift+k': showShortcuts,
		'ctrl+f': () => run('tools/search', 'search'), 'f1': showShortcuts,
		'f12': () => send({ type: 'devtools' }),
	};
	function comboOf(e) {
		let key = e.key;
		if (key === ' ') key = 'space';
		else if (key.length === 1) key = key.toLowerCase();
		else key = key.toLowerCase();
		const parts = [];
		if (e.ctrlKey || e.metaKey) parts.push('ctrl');
		if (e.altKey) parts.push('alt');
		if (e.shiftKey) parts.push('shift');
		parts.push(key);
		return parts.join('+');
	}
	function onKeyDown(e) {
		if (!e.isTrusted) return;
		const combo = comboOf(e);
		if (isTyping(e.target) && !visiblePopup()) return;
		if (isTyping(e.target) && visiblePopup() && combo !== 'enter' && combo !== 'escape') return;
		if (combo === 'space') {                       // mão enquanto segura o espaço
			if (!spaceHeld) { spaceHeld = true; document.body.classList.add('array-hand'); }
			e.preventDefault();
			e.stopImmediatePropagation();
			return;
		}
		const pop = visiblePopup();
		if (pop) {                                       // diálogo aberto = modal: Enter confirma, Esc cancela, o resto fica nele
			if (combo === 'enter' && e.target.tagName !== 'TEXTAREA') {
				const botao = pop.querySelector('[data-id="popup_ok"]');
				if (visivel(botao)) { e.preventDefault(); e.stopImmediatePropagation(); botao.click(); }
			} else if (combo === 'escape') {
				const botao = pop.querySelector('[data-id="popup_cancel"]'), fechar = pop.querySelector('.close');
				const alvo = visivel(botao) ? botao : fechar;
				if (alvo) { e.preventDefault(); e.stopImmediatePropagation(); alvo.click(); }
			} else if (!isTyping(e.target) && e.target.tagName !== 'SELECT') {
				e.stopImmediatePropagation();           // nem os atalhos do miniPaint
			}
			return;
		}
		const act = KEYMAP[combo];
		if (!act) return;
		e.preventDefault();
		e.stopImmediatePropagation();                 // os atalhos do próprio miniPaint não rodam
		try { act(e); } catch (err) { console.error(err); toast(String(err.message || err), 'error'); }
	}
	function onKeyUp(e) {
		if (e.key === ' ' && spaceHeld) {
			spaceHeld = false;
			if (!hand) document.body.classList.remove('array-hand');
		}
	}

	// ---------- mensagens do host ----------
	function hostEvents() {
		if (!host) return;
		host.addEventListener('message', (ev) => {
			const m = ev.data || {};
			if (String(m.type).startsWith('upscale')) { onUpscaleMessage(m); return; }
			switch (m.type) {
				case 'open': openFromHost(m).catch((err) => toast('Could not open: ' + err.message, 'error')); break;
				case 'save-path': onSavePath(m.path); break;
				case 'save-cancel': pending = null; break;
				case 'saved':
					doc.path = m.path; doc.name = m.name; doc.dirty = false; setTitle();
					if (m.silent) status('Saved ' + m.name); else toast('Saved ' + m.name, 'success');
					break;
				case 'error': toast(m.text || 'Error', 'error'); break;
				case 'confirm-exit':
					if (!doc.dirty || isEmptyDocument()) { send({ type: 'exit-reply', force: true }); break; }
					mod('help/about').POP.show({
						title: 'Quit Array?',
						params: [{ title: '', html: '<p style="margin:0 0 6px">Unsaved changes will be lost.</p>' }],
						on_finish: () => send({ type: 'exit-reply', force: true }),
						on_cancel: () => {},
					});
					break;
			}
		});
	}

	// ---------- painel de camadas: modo de mesclagem e opacidade (como no Photoshop) ----------
	const BLENDS = [['Normal', 'source-over'], ['Multiply', 'multiply'], ['Screen', 'screen'], ['Overlay', 'overlay'],
		['Darken', 'darken'], ['Lighten', 'lighten'], ['Color Dodge', 'color-dodge'], ['Color Burn', 'color-burn'],
		['Hard Light', 'hard-light'], ['Soft Light', 'soft-light'], ['Difference', 'difference'], ['Exclusion', 'exclusion'],
		['Hue', 'hue'], ['Saturation', 'saturation'], ['Color', 'color'], ['Luminosity', 'luminosity']];
	// uma ação no histórico do miniPaint (do/undo trocam campos da camada): entra no Ctrl+Z como as outras
	function layerAction(id, changes, description) {
		const layer = L.get_layer(id);
		const before = {};
		for (const k in changes) before[k] = layer ? layer[k] : undefined;
		const apply = (vals) => {
			const l = L.get_layer(id);
			if (!l) return;
			Object.assign(l, vals);
			cfg.need_render = true;
			G.GUI_layers.render_layers();
			G.GUI_details.render_details();
			refreshBlendRow();
		};
		return {
			action_id: 'array_layer_' + Object.keys(changes).join('_'), action_description: description,
			is_done: false, memory_estimate: 0, database_estimate: 0,
			async do() { apply(changes); this.is_done = true; },
			async undo() { apply(before); this.is_done = false; },
			free() {},
		};
	}
	function refreshBlendRow() {
		const sel = $('#array_blend_mode'), op = $('#array_blend_opacity');
		if (!sel || !op) return;
		const l = cfg.layer;
		sel.disabled = op.disabled = !l;
		if (!l) return;
		const comp = l.composition || 'source-over';
		if (![...sel.options].some((o) => o.value === comp)) {
			const o = document.createElement('option');
			o.value = comp; o.textContent = comp; sel.appendChild(o);     // modo fora da lista (ex.: xor do diálogo)
		}
		sel.value = comp;
		if (document.activeElement !== op) op.value = Math.round(l.opacity == null ? 100 : l.opacity);
	}
	function blendRow() {
		const sel = $('#array_blend_mode'), op = $('#array_blend_opacity');
		if (!sel || !op) return;
		for (const [name, value] of BLENDS) {
			const o = document.createElement('option');
			o.value = value; o.textContent = name; sel.appendChild(o);
		}
		sel.addEventListener('change', () => {
			if (!cfg.layer) return;
			State.do_action(layerAction(cfg.layer.id, { composition: sel.value }, 'Blending mode'));
			status('Blending mode: ' + sel.options[sel.selectedIndex].textContent);
		});
		op.addEventListener('change', () => {
			if (!cfg.layer) return;
			const v = Math.max(0, Math.min(100, parseInt(op.value, 10) || 0));
			op.value = v;
			State.do_action(layerAction(cfg.layer.id, { opacity: v }, 'Layer opacity'));
		});
		// a lista é redesenhada pelo miniPaint a cada mudança: acompanhamos
		const orig = G.GUI_layers.render_layers.bind(G.GUI_layers);
		G.GUI_layers.render_layers = function (...a) { const r = orig(...a); refreshBlendRow(); return r; };
		refreshBlendRow();
	}

	// ---------- histórico -> "há alterações" ----------
	function trackChanges() {
		const orig = State.do_action.bind(State);
		const soTroca = (act) => /activate_tool/i.test(String(act && (act.action_id || (act.constructor && act.constructor.name))));
		const infoSize = () => { try { G.GUI_information.show_size(); } catch (e) { /* painel Info fechado */ } };   // o miniPaint só atualiza o tamanho ao mover o mouse
		State.do_action = async function (...a) { const r = await orig(...a); if (!soTroca(a[0])) markDirty(); refreshBlendRow(); infoSize(); return r; };
		for (const fn of ['undo', 'redo']) {
			const o = State[fn].bind(State);
			State[fn] = async function (...a) { const r = await o(...a); markDirty(); refreshBlendRow(); infoSize(); return r; };
		}
	}

	// ---------- início ----------
	function init() {
		L = window.Layers; G = L.Base_gui; cfg = window.AppConfig;
		tools = G.GUI_tools; preview = G.GUI_preview; FileOpen = window.FileOpen; FileSave = window.FileSave; State = window.State;
		arrangeTools();
		fixMenus();
		interceptDownloads();
		viewEvents();
		layoutEvents();
		navigatorEvents();
		hostEvents();
		trackChanges();
		blendRow();
		// abrir/salvar e ajuda: os nossos no lugar dos do miniPaint
		mod('file/open').open_file = () => send({ type: 'open-dialog' });
		mod('file/save').save = saveAs;
		mod('help/shortcuts').shortcuts = showShortcuts;
		mod('help/about').about = showAbout;
		window.addEventListener('keydown', onKeyDown, true);
		window.addEventListener('keyup', onKeyUp, true);
		// links externos (menu Tools > External, Sobre): abrem no navegador do sistema
		document.addEventListener('click', (e) => {
			const a = e.target.closest && e.target.closest('a[href^="http"]');
			if (a) { e.preventDefault(); send({ type: 'open-external', url: a.href }); }
		}, true);
		if (!/(^|;\s*)color=/.test(document.cookie)) setColor('#000000');   // como no Photoshop; o miniPaint começa verde
		setTitle();
		status('Array: Photoshop shortcuts are on (F1 lists them)', 2600);
	}
	window.addEventListener('load', () => {
		if (window.Layers && window.Layers.Base_gui) init();
		else setTimeout(() => window.Layers && init(), 300);
	});
})();
