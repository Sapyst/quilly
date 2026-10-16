// ═══════════════════════════════════════════════════════
// Volury — Editor de e-books · Quilly Suite
// app.js — Lógica da interface
// ═══════════════════════════════════════════════════════

(() => {
  'use strict';

  // ──────────────────────── State
  let currentChapterId = null;
  let currentView = 'wysiwyg'; // 'wysiwyg' | 'code'
  let hasDoc = false;

  // ──────────────────────── DOM refs
  const $ = id => document.getElementById(id);
  const chapterList     = $('chapter-list');
  const resourceList    = $('resource-list');
  const wysiwygEditor   = $('wysiwyg-editor');
  const codeEditor      = $('code-editor');
  const welcomeScreen   = $('welcome-screen');
  const formatBar       = $('format-bar');
  const statusText      = $('status-text');
  const wordCount       = $('word-count');
  const headingSelect   = $('heading-select');

  // ──────────────────────── C# Bridge
  function send(action, payload = {}) {
    window.chrome.webview.postMessage(JSON.stringify({ action, payload }));
  }

  window.chrome.webview.addEventListener('message', e => {
    const msg = typeof e.data === 'string' ? JSON.parse(e.data) : e.data;
    handleMessage(msg);
  });

  function handleMessage(msg) {
    switch (msg.action) {
      case 'update-ui':
        renderChapters(msg.payload.chapters);
        renderResources(msg.payload.resources);
        showEditor();
        setStatus(`${msg.payload.metadata?.title || 'EPUB'} carregado`);
        break;

      case 'chapter-content':
        loadChapterContent(msg.payload.id, msg.payload.content);
        break;

      case 'saved':
        setStatus(`Salvo: ${msg.payload.path}`);
        break;

      case 'metadata':
        fillMetadataModal(msg.payload);
        break;

      case 'validation-result':
        showValidation(msg.payload.errors);
        break;

      case 'toc-generated':
        setStatus('Sumário gerado com sucesso');
        break;

      case 'error':
        setStatus(`Erro: ${msg.payload.message}`, true);
        break;
    }
  }

  // ──────────────────────── Render chapters
  function renderChapters(chapters) {
    chapterList.innerHTML = '';
    chapters.forEach(ch => {
      const li = document.createElement('li');
      li.dataset.id = ch.Id;
      li.draggable = true;
      li.innerHTML = `
        <span class="chapter-icon"><svg class="ic so" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M14 3H7a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8z"/><path d="M14 3v5h5"/><path d="M9 13h6M9 17h6"/></svg></span>
        <span class="chapter-title">${esc(ch.Titulo)}</span>
        <button class="chapter-delete" title="Remover capítulo"><svg class="ic so" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M18 6L6 18M6 6l12 12"/></svg></button>
      `;

      if (ch.Id === currentChapterId) li.classList.add('active');

      li.addEventListener('click', e => {
        if (e.target.classList.contains('chapter-delete')) {
          send('delete-chapter', { id: ch.Id });
          if (currentChapterId === ch.Id) {
            currentChapterId = null;
            wysiwygEditor.innerHTML = '';
            codeEditor.value = '';
          }
          return;
        }
        saveCurrentChapter();
        currentChapterId = ch.Id;
        send('get-chapter-content', { id: ch.Id });
        highlightActive(ch.Id);
      });

      // Drag & drop
      li.addEventListener('dragstart', e => {
        e.dataTransfer.setData('text/plain', ch.Id);
        li.style.opacity = '0.5';
      });
      li.addEventListener('dragend', () => { li.style.opacity = '1'; });
      li.addEventListener('dragover', e => { e.preventDefault(); li.classList.add('drag-over'); });
      li.addEventListener('dragleave', () => { li.classList.remove('drag-over'); });
      li.addEventListener('drop', e => {
        e.preventDefault();
        li.classList.remove('drag-over');
        const fromId = e.dataTransfer.getData('text/plain');
        reorderChapter(fromId, ch.Id);
      });

      chapterList.appendChild(li);
    });

    // Auto-select first if none selected
    if (!currentChapterId && chapters.length > 0) {
      currentChapterId = chapters[0].Id;
      send('get-chapter-content', { id: currentChapterId });
      highlightActive(currentChapterId);
    }
  }

  function reorderChapter(fromId, beforeId) {
    const items = [...chapterList.children];
    const order = items.map(li => li.dataset.id);
    const fromIdx = order.indexOf(fromId);
    const toIdx = order.indexOf(beforeId);
    if (fromIdx < 0 || toIdx < 0 || fromIdx === toIdx) return;
    order.splice(fromIdx, 1);
    order.splice(toIdx, 0, fromId);
    send('reorder-chapters', { order });
    // Re-render will come from C# via update-ui
  }

  function highlightActive(id) {
    chapterList.querySelectorAll('li').forEach(li => {
      li.classList.toggle('active', li.dataset.id === id);
    });
  }

  // ──────────────────────── Render resources
  function renderResources(resources) {
    resourceList.innerHTML = '';
    resources.forEach(r => {
      const li = document.createElement('li');
      const icon = r.Tipo === 'image' ? '<svg class="ic so" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><rect x="3" y="3" width="18" height="18" rx="2"/><circle cx="9" cy="9" r="2"/><path d="M21 15l-5-5L5 21"/></svg>' : '<svg class="ic so" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M14 3H7a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8z"/><path d="M14 3v5h5"/><path d="M10 13l-2 2 2 2M14 13l2 2-2 2"/></svg>';
      li.innerHTML = `<span class="res-icon">${icon}</span> ${esc(r.NomeArquivo)}`;
      resourceList.appendChild(li);
    });
  }

  // ──────────────────────── Load chapter content
  function loadChapterContent(id, content) {
    currentChapterId = id;

    // Extract body content for WYSIWYG
    const bodyMatch = content.match(/<body[^>]*>([\s\S]*)<\/body>/i);
    const bodyHtml = bodyMatch ? bodyMatch[1].trim() : content;

    wysiwygEditor.innerHTML = bodyHtml;
    codeEditor.value = content;
    updateWordCount();
    highlightActive(id);
  }

  // ──────────────────────── Save current chapter
  function saveCurrentChapter() {
    if (!currentChapterId) return;

    let content;
    if (currentView === 'code') {
      content = codeEditor.value;
    } else {
      // Re-wrap body into full XHTML
      content = wrapBody(wysiwygEditor.innerHTML);
    }

    send('save-chapter-content', { id: currentChapterId, content });
  }

  function wrapBody(bodyHtml) {
    return `<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE html>
<html xmlns="http://www.w3.org/1999/xhtml">
<head>
  <title>Chapter</title>
  <link rel="stylesheet" type="text/css" href="../Styles/style.css"/>
</head>
<body>
${bodyHtml}
</body>
</html>`;
  }

  // ──────────────────────── Show/hide editor
  function showEditor() {
    hasDoc = true;
    welcomeScreen.classList.add('hidden');
    formatBar.classList.add('visible');
    if (currentView === 'wysiwyg') {
      wysiwygEditor.style.display = 'block';
      codeEditor.style.display = 'none';
    } else {
      wysiwygEditor.style.display = 'none';
      codeEditor.style.display = 'block';
    }
  }

  // ──────────────────────── View toggle
  $('btn-view-wysiwyg').addEventListener('click', () => {
    if (currentView === 'wysiwyg') return;
    // Sync code → wysiwyg
    const bodyMatch = codeEditor.value.match(/<body[^>]*>([\s\S]*)<\/body>/i);
    wysiwygEditor.innerHTML = bodyMatch ? bodyMatch[1].trim() : codeEditor.value;
    currentView = 'wysiwyg';
    wysiwygEditor.style.display = 'block';
    codeEditor.style.display = 'none';
    $('btn-view-wysiwyg').classList.add('active');
    $('btn-view-code').classList.remove('active');
  });

  $('btn-view-code').addEventListener('click', () => {
    if (currentView === 'code') return;
    // Sync wysiwyg → code
    codeEditor.value = wrapBody(wysiwygEditor.innerHTML);
    currentView = 'code';
    wysiwygEditor.style.display = 'none';
    codeEditor.style.display = 'block';
    $('btn-view-code').classList.add('active');
    $('btn-view-wysiwyg').classList.remove('active');
  });

  // ──────────────────────── Format bar commands
  formatBar.querySelectorAll('button[data-cmd]').forEach(btn => {
    btn.addEventListener('click', () => {
      const cmd = btn.dataset.cmd;
      if (cmd === 'createLink') {
        const url = prompt('URL do link:');
        if (url) document.execCommand('createLink', false, url);
      } else {
        document.execCommand(cmd, false, null);
      }
      wysiwygEditor.focus();
    });
  });

  headingSelect.addEventListener('change', () => {
    const val = headingSelect.value;
    if (val === 'blockquote') {
      document.execCommand('formatBlock', false, 'blockquote');
    } else {
      document.execCommand('formatBlock', false, val);
    }
    wysiwygEditor.focus();
  });

  // ──────────────────────── Word count
  function updateWordCount() {
    const text = wysiwygEditor.innerText || '';
    const words = text.trim().split(/\s+/).filter(w => w.length > 0).length;
    const chars = text.length;
    wordCount.textContent = `${words} palavras · ${chars} caracteres`;
  }

  wysiwygEditor.addEventListener('input', () => {
    updateWordCount();
  });

  // ──────────────────────── Toolbar buttons
  $('btn-new').addEventListener('click',     () => send('new-epub'));
  $('btn-open').addEventListener('click',    () => send('open-epub'));
  $('btn-save').addEventListener('click',    () => { saveCurrentChapter(); send('save-epub'); });
  $('btn-save-as').addEventListener('click', () => { saveCurrentChapter(); send('save-epub-as'); });
  $('btn-import-hyp').addEventListener('click', () => send('import-hyp'));
  $('btn-add-image').addEventListener('click',  () => send('add-image'));
  $('btn-add-css').addEventListener('click',    () => send('add-stylesheet'));
  $('btn-toc').addEventListener('click',     () => { saveCurrentChapter(); send('generate-toc'); });
  $('btn-validate').addEventListener('click',() => { saveCurrentChapter(); send('validate'); });
  $('btn-export').addEventListener('click',  () => { saveCurrentChapter(); send('export-epub'); });
  $('btn-add-chapter').addEventListener('click', () => {
    const title = prompt('Título do capítulo:');
    if (title) send('add-chapter', { title });
  });

  // Welcome screen buttons
  $('welcome-new').addEventListener('click',    () => send('new-epub'));
  $('welcome-open').addEventListener('click',   () => send('open-epub'));
  $('welcome-import').addEventListener('click', () => send('import-hyp'));

  // ──────────────────────── Metadata modal
  $('btn-metadata').addEventListener('click', () => {
    send('get-metadata');
    $('metadata-modal').style.display = 'flex';
  });

  function fillMetadataModal(meta) {
    $('meta-title').value       = meta.title || '';
    $('meta-author').value      = meta.author || '';
    $('meta-language').value    = meta.language || 'pt';
    $('meta-description').value = meta.description || '';
  }

  $('meta-save').addEventListener('click', () => {
    send('update-metadata', {
      title:       $('meta-title').value,
      author:      $('meta-author').value,
      language:    $('meta-language').value,
      description: $('meta-description').value
    });
    $('metadata-modal').style.display = 'none';
    setStatus('Metadados atualizados');
  });

  $('meta-cancel').addEventListener('click', () => {
    $('metadata-modal').style.display = 'none';
  });

  // ──────────────────────── Validation modal
  function showValidation(errors) {
    const list = $('validation-list');
    list.innerHTML = '';
    errors.forEach(err => {
      const li = document.createElement('li');
      li.dataset.severity = err.Severity;
      const icon = err.Severity === 'error' ? '<svg class="ic" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><circle cx="12" cy="12" r="9"/><path d="M15 9l-6 6M9 9l6 6"/></svg>' : err.Severity === 'warning' ? '<svg class="ic" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M10.3 3.9L1.8 18a2 2 0 0 0 1.7 3h17a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0z"/><path d="M12 9v4M12 17h.01"/></svg>' : '<svg class="ic" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><circle cx="12" cy="12" r="9"/><path d="M8 12l3 3 5-6"/></svg>';
      li.innerHTML = icon + esc(err.Message);
      list.appendChild(li);
    });
    $('validation-modal').style.display = 'flex';
  }

  $('validation-close').addEventListener('click', () => {
    $('validation-modal').style.display = 'none';
  });

  // Close modals on backdrop click
  document.querySelectorAll('.modal').forEach(modal => {
    modal.addEventListener('click', e => {
      if (e.target === modal) modal.style.display = 'none';
    });
  });

  // ──────────────────────── Keyboard shortcuts
  document.addEventListener('keydown', e => {
    if (e.ctrlKey || e.metaKey) {
      switch (e.key.toLowerCase()) {
        case 'n': e.preventDefault(); send('new-epub'); break;
        case 'o': e.preventDefault(); send('open-epub'); break;
        case 's':
          e.preventDefault();
          saveCurrentChapter();
          send(e.shiftKey ? 'save-epub-as' : 'save-epub');
          break;
      }
    }
  });

  // ──────────────────────── Helpers
  function setStatus(text, isError = false) {
    statusText.textContent = text;
    statusText.style.color = isError ? 'var(--danger)' : 'var(--fg-dim)';
  }

  function esc(s) {
    const d = document.createElement('div');
    d.textContent = s;
    return d.innerHTML;
  }

  // ──────────────────────── Auto-save on blur
  wysiwygEditor.addEventListener('blur', () => saveCurrentChapter());
  codeEditor.addEventListener('blur', () => saveCurrentChapter());

})();
