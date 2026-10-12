(function () {
  'use strict';

  // --- RPC Bridge com C# ---
  var rpcSeq = 0;
  var rpcPending = {};

  function rpc(method, args) {
    return new Promise(function (resolve, reject) {
      var id = ++rpcSeq;
      rpcPending[id] = { resolve: resolve, reject: reject };
      window.chrome.webview.postMessage({
        type: 'rpc',
        id: id,
        method: method,
        args: args || {}
      });
    });
  }

  window.chrome.webview.addEventListener('message', function (ev) {
    var msg = ev.data || {};
    if (msg.type === 'rpc-reply') {
      var p = rpcPending[msg.id];
      if (p) {
        delete rpcPending[msg.id];
        if (msg.ok) p.resolve(msg.data);
        else p.reject(new Error(msg.error || 'Erro RPC'));
      }
    } else if (msg.type === 'install_progress') {
      onInstallProgress(msg.appId, msg.percent, msg.status);
    }
  });

  // --- Estado da Aplicação ---
  var state = {
    apps: [],
    files: [],
    suitePath: '',
    searchQuery: '',
    selectedFile: null,
    currentTab: 'tab-apps'
  };

  // --- Elementos do DOM ---
  var appsGrid = document.getElementById('apps-grid');
  var filesContainer = document.getElementById('files-container');
  var suitePathDisplay = document.getElementById('suite-path-display');
  var searchInput = document.getElementById('search-input');
  var fileContextMenu = document.getElementById('file-context-menu');

  // --- Inicialização ---
  function init() {
    setupNav();
    setupSearch();
    setupContextMenu();
    setupSettingsEvents();
    loadAll();
  }

  function loadAll() {
    loadApps();
    loadFiles();
    loadSuiteFolder();
  }

  function loadApps() {
    rpc('apps.list').then(function (apps) {
      state.apps = apps || [];
      renderApps();
    }).catch(console.error);
  }

  function loadFiles() {
    rpc('files.list').then(function (files) {
      state.files = files || [];
      renderFiles();
    }).catch(console.error);
  }

  function loadSuiteFolder() {
    rpc('suite.getFolder').then(function (res) {
      if (res && res.path) {
        state.suitePath = res.path;
        suitePathDisplay.textContent = res.path;
      }
    }).catch(console.error);
  }

  // --- Navegação por Abas ---
  function setupNav() {
    document.querySelectorAll('.nav-item').forEach(function (btn) {
      btn.addEventListener('click', function () {
        var tabId = btn.getAttribute('data-tab');
        document.querySelectorAll('.nav-item').forEach(function (b) { b.classList.remove('active'); });
        document.querySelectorAll('.tab-content').forEach(function (t) { t.classList.remove('active'); });

        btn.classList.add('active');
        var target = document.getElementById(tabId);
        if (target) target.classList.add('active');
        state.currentTab = tabId;

        // Limpa busca ao trocar de aba
        searchInput.value = '';
        state.searchQuery = '';
        renderApps();
        renderFiles();
      });
    });

    document.getElementById('btn-refresh').addEventListener('click', function () {
      loadAll();
    });

    document.getElementById('btn-open-suite').addEventListener('click', function () {
      rpc('suite.openFolder').catch(console.error);
    });
  }

  // --- Busca ---
  function setupSearch() {
    searchInput.addEventListener('input', function () {
      state.searchQuery = searchInput.value.toLowerCase().trim();
      if (state.currentTab === 'tab-apps') renderApps();
      else if (state.currentTab === 'tab-files') renderFiles();
    });
  }

  // --- Renderização de Aplicativos ---
  function renderApps() {
    appsGrid.innerHTML = '';
    var filtered = state.apps.filter(function (app) {
      if (!state.searchQuery) return true;
      return app.Name.toLowerCase().includes(state.searchQuery) ||
             app.Description.toLowerCase().includes(state.searchQuery) ||
             app.Formats.some(function (f) { return f.toLowerCase().includes(state.searchQuery); });
    });

    if (filtered.length === 0) {
      appsGrid.innerHTML = '<div class="empty-state">Nenhum aplicativo encontrado para a busca.</div>';
      return;
    }

    filtered.forEach(function (app) {
      var card = document.createElement('div');
      card.className = 'app-card';
      card.id = 'app-card-' + app.Id;

      var badgeClass = 'available';
      var badgeText = 'Disponível';
      var btnActionClass = 'btn-install';
      var btnActionText = 'Instalar';

      if (app.Installed) {
        if (app.UpdateAvailable) {
          badgeClass = 'update';
          var sVer = app.SourceVersion ? ('v' + (app.SourceVersion.endsWith('.0') ? app.SourceVersion.slice(0, -2) : app.SourceVersion)) : '';
          badgeText = sVer ? ('Atualizar ' + sVer) : 'Atualização';
          btnActionClass = 'btn-update';
          btnActionText = 'Atualizar';
        } else {
          badgeClass = 'installed';
          var iVer = app.InstalledVersion ? ('v' + (app.InstalledVersion.endsWith('.0') ? app.InstalledVersion.slice(0, -2) : app.InstalledVersion)) : '';
          badgeText = iVer || 'Instalado';
          btnActionClass = 'btn-open';
          btnActionText = 'Abrir';
        }
      }

      var formatsHtml = app.Formats.map(function (fmt) {
        var isHyp = fmt === '.hyp';
        return '<span class="format-tag ' + (isHyp ? 'hyp-tag' : '') + '">' + fmt + '</span>';
      }).join('');

      card.innerHTML =
        '<div class="app-header">' +
          '<img src="' + app.Icon + '" alt="' + app.Name + '" class="app-icon-img">' +
          '<div class="app-meta">' +
            '<div class="app-title-row">' +
              '<span class="app-name">' + app.Name + '</span>' +
              '<span class="badge-status ' + badgeClass + '" id="badge-' + app.Id + '">' + badgeText + '</span>' +
            '</div>' +
            '<div class="app-subtitle">' + app.Subtitle + '</div>' +
          '</div>' +
        '</div>' +
        '<div class="app-description">' + app.Description + '</div>' +
        '<div class="app-formats">' + formatsHtml + '</div>' +
        '<div class="install-progress-box" id="progress-box-' + app.Id + '">' +
          '<div class="install-status-text">' +
            '<span id="progress-status-' + app.Id + '">Instalando…</span>' +
            '<span id="progress-pct-' + app.Id + '">0%</span>' +
          '</div>' +
          '<div class="install-progress-bar-bg">' +
            '<div class="install-progress-bar-fill" id="progress-bar-' + app.Id + '"></div>' +
          '</div>' +
        '</div>' +
        '<div class="app-footer">' +
          '<button class="btn-primary ' + btnActionClass + '" id="btn-action-' + app.Id + '">' + btnActionText + '</button>' +
          '<button class="btn-menu-options" id="btn-menu-' + app.Id + '" title="Mais opções">•••</button>' +
          '<div class="app-card-menu" id="menu-dropdown-' + app.Id + '">' +
            '<div class="card-menu-item" data-action="folder" data-appid="' + app.Id + '">' +
              '<span>Abrir pasta de instalação</span>' +
            '</div>' +
            '<div class="card-menu-item" data-action="shortcut" data-appid="' + app.Id + '">' +
              '<span>Criar atalho na Área de Trabalho</span>' +
            '</div>' +
            (app.Installed ? '<div class="card-menu-item danger" data-action="uninstall" data-appid="' + app.Id + '"><span>Desinstalar da Suite</span></div>' : '') +
          '</div>' +
        '</div>';

      // Evento de Ação Principal
      var btnAction = card.querySelector('#btn-action-' + app.Id);
      btnAction.addEventListener('click', function () {
        if (!app.Installed || app.UpdateAvailable) {
          iniciaInstalacao(app.Id);
        } else {
          rpc('apps.launch', { appId: app.Id }).catch(function (err) {
            alert('Não foi possível abrir o app: ' + err.message);
          });
        }
      });

      // Evento do menu (...)
      var btnMenu = card.querySelector('#btn-menu-' + app.Id);
      var dropdown = card.querySelector('#menu-dropdown-' + app.Id);

      btnMenu.addEventListener('click', function (e) {
        e.stopPropagation();
        var wasOpen = dropdown.classList.contains('open');
        fecharTodosMenus();
        if (!wasOpen) dropdown.classList.add('open');
      });

      dropdown.querySelectorAll('.card-menu-item').forEach(function (item) {
        item.addEventListener('click', function (e) {
          e.stopPropagation();
          dropdown.classList.remove('open');
          var action = item.getAttribute('data-action');
          var appId = item.getAttribute('data-appid');

          if (action === 'folder') {
            rpc('apps.openFolder', { appId: appId }).catch(console.error);
          } else if (action === 'shortcut') {
            rpc('apps.createShortcut', { appId: appId })
              .then(function () { alert('Atalho criado na Área de Trabalho!'); })
              .catch(function (err) { alert('Erro: ' + err.message); });
          } else if (action === 'uninstall') {
            if (confirm('Deseja desinstalar ' + app.Name + ' da suite?')) {
              rpc('apps.uninstall', { appId: appId }).then(function () {
                loadApps();
              }).catch(function (err) { alert('Erro: ' + err.message); });
            }
          }
        });
      });

      appsGrid.appendChild(card);
    });
  }

  function fecharTodosMenus() {
    document.querySelectorAll('.app-card-menu').forEach(function (m) {
      m.classList.remove('open');
    });
  }

  document.addEventListener('click', function () {
    fecharTodosMenus();
    fecharMenuContexto();
  });

  // --- Instalação Silenciosa em Background ---
  function iniciaInstalacao(appId) {
    var card = document.getElementById('app-card-' + appId);
    var pBox = document.getElementById('progress-box-' + appId);
    var btn = document.getElementById('btn-action-' + appId);

    if (pBox) pBox.classList.add('active');
    if (btn) {
      btn.disabled = true;
      btn.textContent = 'Processando…';
    }

    rpc('apps.install', { appId: appId }).then(function (res) {
      if (res && res.apps) state.apps = res.apps;
      renderApps();
    }).catch(function (err) {
      alert('Falha na instalação: ' + err.message);
      if (pBox) pBox.classList.remove('active');
      if (btn) btn.disabled = false;
      loadApps();
    });
  }

  function onInstallProgress(appId, percent, status) {
    var pBox = document.getElementById('progress-box-' + appId);
    var bar = document.getElementById('progress-bar-' + appId);
    var pctText = document.getElementById('progress-pct-' + appId);
    var statusText = document.getElementById('progress-status-' + appId);

    if (pBox) pBox.classList.add('active');
    if (bar) bar.style.width = percent + '%';
    if (pctText) pctText.textContent = percent + '%';
    if (statusText) statusText.textContent = status || 'Instalando…';
  }

  // --- Renderização de Arquivos ---
  function renderFiles() {
    filesContainer.innerHTML = '';
    var filtered = state.files.filter(function (file) {
      if (!state.searchQuery) return true;
      return file.Name.toLowerCase().includes(state.searchQuery) ||
             file.Path.toLowerCase().includes(state.searchQuery);
    });

    if (filtered.length === 0) {
      filesContainer.innerHTML = '<div class="empty-state">Nenhum arquivo recente encontrado. Documentos criados no Noty e Artry aparecerão aqui automaticamente.</div>';
      return;
    }

    var header = document.createElement('div');
    header.className = 'files-table-header';
    header.innerHTML =
      '<span>Nome e Local</span>' +
      '<span>App Sugerido</span>' +
      '<span>Modificado</span>' +
      '<span style="text-align:right">Ação</span>';
    filesContainer.appendChild(header);

    filtered.forEach(function (file) {
      var row = document.createElement('div');
      row.className = 'file-row';

      var extClass = 'ext-txt';
      if (file.Extension === '.hyp') extClass = 'ext-hyp';
      else if (file.Extension === '.md') extClass = 'ext-md';
      else if (file.Extension === '.artry') extClass = 'ext-artry';
      else if (file.Extension === '.docx') extClass = 'ext-docx';

      var extBadgeHtml = file.Extension === '.hyp'
        ? '<img src="assets/hyp.png" class="file-icon-img" alt=".hyp" title="Documento Hyper (.hyp)">'
        : '<span class="file-ext-badge ' + extClass + '">' + file.Extension.replace('.', '') + '</span>';

      row.innerHTML =
        '<div class="file-cell-name">' +
          extBadgeHtml +
          '<div class="file-name-block">' +
            '<span class="file-title">' + file.Name + '</span>' +
            '<span class="file-path" title="' + file.Path + '">' + file.Path + '</span>' +
          '</div>' +
        '</div>' +
        '<div class="file-cell-app">' + file.RecommendedApp + '</div>' +
        '<div class="file-cell-date">' + file.ModifiedFormatted + '</div>' +
        '<div class="file-cell-actions">' +
          '<button class="btn-file-open">Abrir</button>' +
        '</div>';

      // Clique duplo: abre no app recomendado
      row.addEventListener('dblclick', function () {
        abrirArquivo(file.Path, file.RecommendedAppId);
      });

      // Botão abrir direto
      row.querySelector('.btn-file-open').addEventListener('click', function (e) {
        e.stopPropagation();
        abrirArquivo(file.Path, file.RecommendedAppId);
      });

      // Clique com botão direito: Menu de Contexto
      row.addEventListener('contextmenu', function (e) {
        e.preventDefault();
        abrirMenuContexto(e.clientX, e.clientY, file);
      });

      filesContainer.appendChild(row);
    });
  }

  function abrirArquivo(path, appId) {
    rpc('files.open', { path: path, appId: appId || '' }).catch(function (err) {
      alert('Não foi possível abrir o arquivo: ' + err.message);
    });
  }

  // --- Menu de Contexto Customizado ---
  function setupContextMenu() {
    document.getElementById('ctx-open-noty').addEventListener('click', function () {
      if (state.selectedFile) abrirArquivo(state.selectedFile.Path, 'noty');
      fecharMenuContexto();
    });

    document.getElementById('ctx-open-artry').addEventListener('click', function () {
      if (state.selectedFile) abrirArquivo(state.selectedFile.Path, 'artry');
      fecharMenuContexto();
    });

    document.getElementById('ctx-open-volury').addEventListener('click', function () {
      if (state.selectedFile) abrirArquivo(state.selectedFile.Path, 'volury');
      fecharMenuContexto();
    });

    document.getElementById('ctx-open-default').addEventListener('click', function () {
      if (state.selectedFile) abrirArquivo(state.selectedFile.Path, '');
      fecharMenuContexto();
    });

    document.getElementById('ctx-show-explorer').addEventListener('click', function () {
      if (state.selectedFile) rpc('files.showInExplorer', { path: state.selectedFile.Path });
      fecharMenuContexto();
    });

    document.getElementById('ctx-remove-recent').addEventListener('click', function () {
      if (state.selectedFile) {
        rpc('files.remove', { path: state.selectedFile.Path }).then(function (res) {
          if (res && res.files) state.files = res.files;
          renderFiles();
        });
      }
      fecharMenuContexto();
    });
  }

  function abrirMenuContexto(x, y, file) {
    state.selectedFile = file;
    fileContextMenu.style.left = Math.min(x, window.innerWidth - 240) + 'px';
    fileContextMenu.style.top = Math.min(y, window.innerHeight - 200) + 'px';
    fileContextMenu.classList.add('active');
  }

  function fecharMenuContexto() {
    fileContextMenu.classList.remove('active');
  }

  // --- Ajustes / Configurações ---
  function setupSettingsEvents() {
    document.getElementById('btn-change-suite-dir').addEventListener('click', function () {
      rpc('suite.chooseFolder').then(function (res) {
        if (res && res.path) {
          state.suitePath = res.path;
          suitePathDisplay.textContent = res.path;
          loadApps();
        }
      }).catch(console.error);
    });

    document.getElementById('btn-open-suite-dir').addEventListener('click', function () {
      rpc('suite.openFolder').catch(console.error);
    });
  }

  // Inicia aplicação
  init();
})();
