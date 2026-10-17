//! Noty (Arch Quilly): editor de texto e Markdown leve, com vidro. GTK4 + GtkSourceView + libspelling.
//!
//!   noty                 reabre o último arquivo (ou uma folha em branco)
//!   noty a.hyp b.md      cada arquivo na sua janela

mod config;
mod hyp;
mod janela;
mod previa;
mod tema;
mod vidro;

use std::path::PathBuf;

use adw::prelude::*;
use gtk::{gio, glib};

const APP_ID: &str = "com.archquilly.Noty";

fn atalhos(app: &adw::Application) {
    for (acao, teclas) in [
        ("noty.novo", &["<Ctrl>n"][..]),
        ("noty.abrir", &["<Ctrl>o"]),
        ("noty.salvar", &["<Ctrl>s"]),
        ("noty.salvar-como", &["<Ctrl><Shift>s"]),
        ("noty.sair", &["<Ctrl>w"]),
        ("noty.localizar", &["<Ctrl>f"]),
        ("noty.previa", &["<Ctrl>m"]),
        ("noty.tema", &["<Ctrl>t"]),
        ("noty.negrito", &["<Ctrl>b"]),
        ("noty.italico", &["<Ctrl>i"]),
    ] {
        app.set_accels_for_action(acao, teclas);
    }
}

fn main() -> glib::ExitCode {
    let app = adw::Application::builder().application_id(APP_ID).flags(gio::ApplicationFlags::HANDLES_OPEN).build();
    app.connect_startup(|app| {
        sourceview5::init();
        gtk::Window::set_default_icon_name(APP_ID);
        config::garantir_dicionarios();
        tema::instalar();
        atalhos(app);
    });
    app.connect_activate(|app| {
        if let Some(n) = janela::todas().into_iter().next() {
            n.foco();
            return;
        }
        let ultimo = janela::com_settings(|s| s.ui.last_file.clone()).map(PathBuf::from).filter(|p| p.is_file());
        match ultimo {
            Some(p) => janela::abrir_em_janela(app, &p, None),
            None => janela::Noty::nova(app).foco(),
        }
    });
    app.connect_open(|app, arquivos, _| {
        for f in arquivos {
            if let Some(p) = f.path() {
                let vazia = janela::todas().into_iter().find(|n| n.intocada());
                janela::abrir_em_janela(app, &p, vazia.as_ref());
            }
        }
    });
    app.run()
}
