//! O tema ao vivo: o vidro do sistema (vidro.rs) + o CSS livre do usuário (theme-gtk.css), e o
//! esquema de cores do editor. Trocou o tema do Arch Aero (claro/escuro, opacidade) ou salvou o
//! theme-gtk.css: todas as janelas mudam na hora.

use std::cell::RefCell;
use std::path::PathBuf;

use gtk::prelude::*;
use gtk::{gdk, gio, glib};
use sourceview5::prelude::*;

use crate::{config, janela, vidro};

const EXEMPLO: &str = r#"/* O tema livre do Noty: CSS do GTK, aplicado por cima do vidro do sistema.
 * Salve este arquivo e o Noty muda na hora. Apague tudo pra voltar ao padrão.
 *
 * Exemplos (tire os comentários):
 *
 * window.noty .vidro { background-color: rgba(12, 18, 15, 0.94); }
 * window.noty textview.editor { font-family: "JetBrains Mono"; font-size: 16px; }
 * window.noty textview.previa { font-family: "Georgia", serif; font-size: 17px; }
 * window.noty .status { opacity: 0.6; }
 */
"#;

thread_local! {
    static CORES: RefCell<vidro::Cores> = RefCell::new(vidro::ler());
    static PROVEDORES: RefCell<Option<(gtk::CssProvider, gtk::CssProvider)>> = const { RefCell::new(None) };
    static MONITORES: RefCell<Vec<gio::FileMonitor>> = const { RefCell::new(Vec::new()) };
    static ESPERA: RefCell<Option<glib::SourceId>> = const { RefCell::new(None) };
}

pub fn cores() -> vidro::Cores {
    CORES.with(|c| c.borrow().clone())
}

/// O theme-gtk.css do usuário (criado com os exemplos na primeira vez).
pub fn arquivo_do_usuario() -> PathBuf {
    let p = config::pasta().join("theme-gtk.css");
    if !p.exists() {
        let _ = std::fs::create_dir_all(config::pasta());
        let _ = std::fs::write(&p, EXEMPLO);
    }
    p
}

fn pasta_estilos() -> PathBuf {
    std::env::var_os("XDG_CACHE_HOME")
        .filter(|v| !v.is_empty())
        .map(PathBuf::from)
        .unwrap_or_else(|| config::home().join(".cache"))
        .join("arch-quilly/noty/estilos")
}

fn escrever_esquema(c: &vidro::Cores) {
    let dir = pasta_estilos();
    let _ = std::fs::create_dir_all(&dir);
    let _ = std::fs::write(dir.join("noty.xml"), c.esquema());
    let m = sourceview5::StyleSchemeManager::default();
    let d = dir.to_string_lossy().to_string();
    if !m.search_path().iter().any(|p| p.as_str() == d) {
        m.append_search_path(&d);
    }
    m.force_rescan();
}

pub fn aplicar_esquema(buf: &sourceview5::Buffer) {
    buf.set_style_scheme(sourceview5::StyleSchemeManager::default().scheme("noty").as_ref());
}

/// Liga o tema no display e passa a vigiar o hypr-tema e o theme-gtk.css.
pub fn instalar() {
    let Some(display) = gdk::Display::default() else { return };
    let base = gtk::CssProvider::new();
    let usuario = gtk::CssProvider::new();
    usuario.connect_parsing_error(|_, secao, erro| eprintln!("noty: theme-gtk.css {}: {erro}", secao.to_str()));
    gtk::style_context_add_provider_for_display(&display, &base, gtk::STYLE_PROVIDER_PRIORITY_APPLICATION);
    gtk::style_context_add_provider_for_display(&display, &usuario, gtk::STYLE_PROVIDER_PRIORITY_APPLICATION + 1);
    PROVEDORES.with(|p| *p.borrow_mut() = Some((base, usuario)));
    recarregar();

    let mut monitores = Vec::new();
    let [tema, _] = vidro::arquivos_do_tema();
    let alvos = [(tema.parent().map(|p| p.to_path_buf()), vec!["tema", "cores"]), (Some(config::pasta()), vec!["theme-gtk.css"])];
    for (dir, nomes) in alvos {
        let Some(dir) = dir.filter(|d| d.is_dir()) else { continue };
        let Ok(m) = gio::File::for_path(&dir).monitor_directory(gio::FileMonitorFlags::WATCH_MOVES, gio::Cancellable::NONE) else { continue };
        m.connect_changed(move |_, f, outro, _| {
            let nome = |x: Option<&gio::File>| x.and_then(|x| x.basename()).map(|b| b.to_string_lossy().to_string());
            let toca = [nome(Some(f)), nome(outro)].into_iter().flatten().any(|n| nomes.contains(&n.as_str()));
            if toca {
                agendar();
            }
        });
        monitores.push(m);
    }
    MONITORES.with(|m| *m.borrow_mut() = monitores);
}

fn agendar() {
    ESPERA.with(|e| {
        if let Some(id) = e.borrow_mut().take() {
            id.remove();
        }
        let id = glib::timeout_add_local_once(std::time::Duration::from_millis(150), || {
            ESPERA.with(|e| *e.borrow_mut() = None);
            recarregar();
        });
        *e.borrow_mut() = Some(id);
    });
}

fn recarregar() {
    let c = vidro::ler();
    adw::StyleManager::default().set_color_scheme(if c.escuro { adw::ColorScheme::ForceDark } else { adw::ColorScheme::ForceLight });
    PROVEDORES.with(|p| {
        if let Some((base, usuario)) = p.borrow().as_ref() {
            base.load_from_string(&c.css());
            let livre = std::fs::read_to_string(config::pasta().join("theme-gtk.css")).unwrap_or_default();
            usuario.load_from_string(&livre);
        }
    });
    escrever_esquema(&c);
    CORES.with(|x| *x.borrow_mut() = c);
    for n in janela::todas() {
        n.tema_mudou();
    }
}
