//! Uma janela do Noty = um arquivo (como sempre foi). O editor é um GtkSourceView: aguenta arquivos
//! grandes, tem desfazer, busca e o corretor (libspelling) de verdade.

use std::cell::RefCell;
use std::path::{Path, PathBuf};
use std::rc::Rc;

use adw::prelude::*;
use gtk::{gdk, gio, glib};
use sourceview5::prelude::*;

use crate::config::{self, Settings};
use crate::{hyp, previa, tema};

const IDIOMAS: [(&str, &str, &str); 4] =
    [("pt-BR", "pt_BR", "Português (Brasil)"), ("en-US", "en_US", "English (US)"), ("es", "es", "Español"), ("fr", "fr", "Français")];

thread_local! {
    static SETTINGS: RefCell<Settings> = RefCell::new(Settings::ler());
    /// quem segura cada janela viva (as closures guardam só referências fracas)
    static NOTYS: RefCell<Vec<Rc<Noty>>> = const { RefCell::new(Vec::new()) };
}

pub fn com_settings<R>(f: impl FnOnce(&mut Settings) -> R) -> R {
    SETTINGS.with(|s| f(&mut s.borrow_mut()))
}

#[derive(Default)]
struct Estado {
    caminho: Option<PathBuf>,
    nome: String,
    sujo: bool,
    /// o que esta janela sabe que está no disco (abriu ou salvou): o próprio salvar não recarrega
    no_disco: Option<String>,
    monitor: Option<gio::FileMonitor>,
    recarga: Option<glib::SourceId>,
    perguntando: bool,
    pode_fechar: bool,
    contagem: Option<glib::SourceId>,
    carregando: bool,
}

pub struct Noty {
    pub janela: adw::ApplicationWindow,
    buf: sourceview5::Buffer,
    view: sourceview5::View,
    rolagem: gtk::ScrolledWindow,
    pilha: gtk::Stack,
    previa: gtk::TextView,
    titulo: gtk::Label,
    status: gtk::Label,
    menu_alt: gtk::Revealer,
    barra_menu: gtk::PopoverMenuBar,
    busca: gtk::Revealer,
    busca_campo: gtk::SearchEntry,
    busca_cont: gtk::Label,
    busca_ctx: sourceview5::SearchContext,
    corretor: libspelling::Checker,
    adaptador: libspelling::TextBufferAdapter,
    estado: RefCell<Estado>,
}

pub fn todas() -> Vec<Rc<Noty>> {
    NOTYS.with(|n| n.borrow().clone())
}

impl Noty {
    pub fn nova(app: &adw::Application) -> Rc<Noty> {
        let t = config::Tamanho::ler();
        let janela = adw::ApplicationWindow::builder()
            .application(app)
            .title("Noty")
            .default_width(t.w)
            .default_height(t.h)
            .width_request(420)
            .height_request(320)
            .build();
        janela.add_css_class("noty");
        if t.maximizada {
            janela.maximize();
        }

        // ---- o editor
        let buf = sourceview5::Buffer::new(None::<&gtk::TextTagTable>);
        buf.set_highlight_matching_brackets(false);
        let view = sourceview5::View::with_buffer(&buf);
        view.add_css_class("editor");
        view.set_wrap_mode(gtk::WrapMode::WordChar);
        view.set_top_margin(48);
        view.set_bottom_margin(32);
        view.set_pixels_above_lines(3);
        view.set_pixels_below_lines(5);
        view.set_pixels_inside_wrap(9);
        view.set_tab_width(4);
        view.set_smart_home_end(sourceview5::SmartHomeEndType::Before);
        let rolagem = gtk::ScrolledWindow::builder().child(&view).vexpand(true).hexpand(true).build();

        // ---- a pré-visualização (Ctrl+M)
        let previa = gtk::TextView::builder()
            .editable(false)
            .cursor_visible(false)
            .wrap_mode(gtk::WrapMode::WordChar)
            .top_margin(40)
            .bottom_margin(40)
            .pixels_inside_wrap(6)
            .build();
        previa.add_css_class("previa");
        let rolagem_previa = gtk::ScrolledWindow::builder().child(&previa).vexpand(true).build();
        let pilha = gtk::Stack::builder().transition_type(gtk::StackTransitionType::Crossfade).transition_duration(160).build();
        pilha.add_named(&rolagem, Some("editor"));
        pilha.add_named(&rolagem_previa, Some("previa"));

        // ---- busca (Ctrl+F): flutua no canto de cima
        let busca_campo = gtk::SearchEntry::builder().placeholder_text("Localizar…").width_chars(24).build();
        let busca_cont = gtk::Label::new(None);
        busca_cont.add_css_class("cont");
        let ant = gtk::Button::from_icon_name("go-up-symbolic");
        ant.set_tooltip_text(Some("Anterior (Shift+Enter)"));
        ant.add_css_class("flat");
        let prox = gtk::Button::from_icon_name("go-down-symbolic");
        prox.set_tooltip_text(Some("Próximo (Enter)"));
        prox.add_css_class("flat");
        let fechar_busca = gtk::Button::from_icon_name("window-close-symbolic");
        fechar_busca.add_css_class("flat");
        let caixa_busca = gtk::Box::new(gtk::Orientation::Horizontal, 6);
        caixa_busca.add_css_class("busca");
        for w in [busca_campo.upcast_ref::<gtk::Widget>(), busca_cont.upcast_ref(), ant.upcast_ref(), prox.upcast_ref(), fechar_busca.upcast_ref()] {
            caixa_busca.append(w);
        }
        let busca = gtk::Revealer::builder()
            .child(&caixa_busca)
            .transition_type(gtk::RevealerTransitionType::Crossfade)
            .halign(gtk::Align::End)
            .valign(gtk::Align::Start)
            .build();
        let busca_cfg = sourceview5::SearchSettings::new();
        busca_cfg.set_wrap_around(true);
        busca_cfg.set_case_sensitive(false);
        let busca_ctx = sourceview5::SearchContext::new(&buf, Some(&busca_cfg));
        busca_ctx.set_highlight(true);

        let sobreposto = gtk::Overlay::builder().child(&pilha).build();
        sobreposto.add_overlay(&busca);

        // ---- status: palavras · caracteres · linhas
        let status = gtk::Label::new(Some("0 Palavras · 0 Caracteres · 1 Linha"));
        status.add_css_class("status");

        // ---- barra de menu: escondida, o Alt mostra
        let barra_menu = gtk::PopoverMenuBar::from_model(None::<&gio::MenuModel>);
        barra_menu.add_css_class("menu-alt");
        let menu_alt = gtk::Revealer::builder().child(&barra_menu).transition_type(gtk::RevealerTransitionType::SlideDown).build();

        let titulo = gtk::Label::new(Some("sem título.hyp — Noty"));
        titulo.add_css_class("title");
        let cabeca = adw::HeaderBar::builder().title_widget(&titulo).build();
        cabeca.add_css_class("flat");

        let caixa = gtk::Box::new(gtk::Orientation::Vertical, 0);
        caixa.append(&menu_alt);
        caixa.append(&sobreposto);
        caixa.append(&status);
        let moldura = adw::ToolbarView::builder().content(&caixa).build();
        moldura.add_top_bar(&cabeca);
        moldura.add_css_class("vidro");
        janela.set_content(Some(&moldura));

        // ---- corretor
        let corretor = libspelling::Checker::default();
        let adaptador = libspelling::TextBufferAdapter::new(&buf, &corretor);

        let noty = Rc::new(Noty {
            janela,
            buf,
            view,
            rolagem,
            pilha,
            previa,
            titulo,
            status,
            menu_alt,
            barra_menu,
            busca,
            busca_campo,
            busca_cont,
            busca_ctx,
            corretor,
            adaptador,
            estado: RefCell::new(Estado { nome: "sem título.hyp".into(), ..Default::default() }),
        });
        NOTYS.with(|n| n.borrow_mut().push(noty.clone()));
        noty.janela.connect_destroy(|j| {
            NOTYS.with(|n| n.borrow_mut().retain(|x| &x.janela != j));
        });

        noty.ligar(&ant, &prox, &fechar_busca);
        noty.criar_acoes();
        noty.aplicar_corretor();
        noty.aplicar_quebra();
        noty.atualizar_menu();
        tema::aplicar_esquema(&noty.buf);
        noty.atualizar_titulo();
        noty
    }

    // ================================================================= sinais
    fn ligar(self: &Rc<Self>, ant: &gtk::Button, prox: &gtk::Button, fechar_busca: &gtk::Button) {
        let fraco = Rc::downgrade(self);

        // texto mudou: sujo + contador (com uma folga, pra não contar a cada tecla num livro inteiro)
        self.buf.connect_changed(glib::clone!(#[strong] fraco, move |_| {
            let Some(n) = fraco.upgrade() else { return };
            if !n.estado.borrow().carregando {
                n.marcar_sujo(true);
            }
            n.agendar_contagem();
        }));

        // coluna central de ~68 caracteres, com a barra de rolagem no canto da janela
        let h = self.rolagem.hadjustment();
        h.connect_page_size_notify(glib::clone!(#[strong] fraco, move |_| {
            if let Some(n) = fraco.upgrade() {
                n.centralizar();
            }
        }));
        self.view.connect_realize(glib::clone!(#[strong] fraco, move |_| {
            if let Some(n) = fraco.upgrade() {
                n.centralizar();
            }
        }));
        self.previa.connect_realize(glib::clone!(#[strong] fraco, move |_| {
            if let Some(n) = fraco.upgrade() {
                n.centralizar();
            }
        }));

        // busca
        self.busca_campo.connect_search_changed(glib::clone!(#[strong] fraco, move |e| {
            if let Some(n) = fraco.upgrade() {
                let t = e.text();
                n.busca_ctx.settings().set_search_text(if t.is_empty() { None } else { Some(t.as_str()) });
            }
        }));
        self.busca_ctx.connect_occurrences_count_notify(glib::clone!(#[strong] fraco, move |c| {
            if let Some(n) = fraco.upgrade() {
                let k = c.occurrences_count();
                n.busca_cont.set_text(&if k > 0 { format!("{k}×") } else if n.busca_campo.text().is_empty() { String::new() } else { "0×".into() });
            }
        }));
        self.busca_campo.connect_activate(glib::clone!(#[strong] fraco, move |_| {
            if let Some(n) = fraco.upgrade() {
                n.procurar(false);
            }
        }));
        self.busca_campo.connect_stop_search(glib::clone!(#[strong] fraco, move |_| {
            if let Some(n) = fraco.upgrade() {
                n.fechar_busca();
            }
        }));
        let tecla_busca = gtk::EventControllerKey::new();
        tecla_busca.connect_key_pressed(glib::clone!(#[strong] fraco, move |_, k, _, m| {
            let Some(n) = fraco.upgrade() else { return glib::Propagation::Proceed };
            if (k == gdk::Key::Return || k == gdk::Key::KP_Enter) && m.contains(gdk::ModifierType::SHIFT_MASK) {
                n.procurar(true);
                return glib::Propagation::Stop;
            }
            glib::Propagation::Proceed
        }));
        self.busca_campo.add_controller(tecla_busca);
        ant.connect_clicked(glib::clone!(#[strong] fraco, move |_| {
            if let Some(n) = fraco.upgrade() {
                n.procurar(true);
            }
        }));
        prox.connect_clicked(glib::clone!(#[strong] fraco, move |_| {
            if let Some(n) = fraco.upgrade() {
                n.procurar(false);
            }
        }));
        fechar_busca.connect_clicked(glib::clone!(#[strong] fraco, move |_| {
            if let Some(n) = fraco.upgrade() {
                n.fechar_busca();
            }
        }));

        // teclado da janela: Alt sozinho mostra o menu; Esc fecha busca/prévia/menu
        let teclas = gtk::EventControllerKey::new();
        teclas.set_propagation_phase(gtk::PropagationPhase::Capture);
        let alt_sozinho = Rc::new(std::cell::Cell::new(false));
        teclas.connect_key_pressed(glib::clone!(#[strong] fraco, #[strong] alt_sozinho, move |_, k, _, _| {
            let Some(n) = fraco.upgrade() else { return glib::Propagation::Proceed };
            if k == gdk::Key::Alt_L || k == gdk::Key::Alt_R {
                alt_sozinho.set(true);
                return glib::Propagation::Proceed;
            }
            alt_sozinho.set(false);
            if k == gdk::Key::Escape {
                if n.busca.reveals_child() {
                    n.fechar_busca();
                    return glib::Propagation::Stop;
                }
                if n.pilha.visible_child_name().as_deref() == Some("previa") {
                    n.mostrar_editor();
                    return glib::Propagation::Stop;
                }
                if n.menu_alt.reveals_child() {
                    n.menu_alt.set_reveal_child(false);
                    n.view.grab_focus();
                    return glib::Propagation::Stop;
                }
            }
            glib::Propagation::Proceed
        }));
        teclas.connect_key_released(glib::clone!(#[strong] fraco, #[strong] alt_sozinho, move |_, k, _, _| {
            if (k == gdk::Key::Alt_L || k == gdk::Key::Alt_R) && alt_sozinho.get() {
                alt_sozinho.set(false);
                if let Some(n) = fraco.upgrade() {
                    let mostrar = !n.menu_alt.reveals_child();
                    n.menu_alt.set_reveal_child(mostrar);
                    if !mostrar {
                        n.view.grab_focus();
                    }
                }
            }
        }));
        self.janela.add_controller(teclas);

        // botão direito: o corretor precisa saber a palavra sob o clique
        let clique = gtk::GestureClick::builder().button(3).propagation_phase(gtk::PropagationPhase::Capture).build();
        clique.connect_pressed(glib::clone!(#[strong] fraco, move |_, _, x, y| {
            let Some(n) = fraco.upgrade() else { return };
            if !n.buf.has_selection() {
                let (bx, by) = n.view.window_to_buffer_coords(gtk::TextWindowType::Widget, x as i32, y as i32);
                if let Some(it) = n.view.iter_at_location(bx, by) {
                    n.buf.place_cursor(&it);
                }
            }
            n.adaptador.update_corrections();
        }));
        self.view.add_controller(clique);

        // colar imagem (print, imagem copiada de outro app) = anexo no .hyp
        self.view.connect_paste_clipboard(glib::clone!(#[strong] fraco, move |v| {
            let Some(n) = fraco.upgrade() else { return };
            let cb = v.clipboard();
            let formatos = cb.formats();
            if formatos.contains_type(gdk::Texture::static_type()) && !formatos.contain_mime_type("text/plain") {
                v.stop_signal_emission_by_name("paste-clipboard");
                let fraco = Rc::downgrade(&n);
                cb.read_texture_async(gio::Cancellable::NONE, move |r| {
                    if let (Some(n), Ok(Some(tex))) = (fraco.upgrade(), r) {
                        n.anexar_textura(&tex, "imagem");
                    }
                });
            }
        }));

        // links da prévia abrem no navegador
        let clique_previa = gtk::GestureClick::new();
        clique_previa.connect_released(glib::clone!(#[strong] fraco, move |_, _, x, y| {
            let Some(n) = fraco.upgrade() else { return };
            if let Some(url) = previa::link_em(&n.previa, x, y) {
                gtk::UriLauncher::new(&url).launch(Some(&n.janela), gio::Cancellable::NONE, |_| {});
            }
        }));
        self.previa.add_controller(clique_previa);

        // arrastar e soltar: imagens viram anexo; textos abrem
        let alvo = gtk::DropTarget::new(gdk::FileList::static_type(), gdk::DragAction::COPY);
        alvo.connect_enter(glib::clone!(#[strong] fraco, move |_, _, _| {
            if let Some(n) = fraco.upgrade() {
                n.view.add_css_class("arrastando");
            }
            gdk::DragAction::COPY
        }));
        alvo.connect_leave(glib::clone!(#[strong] fraco, move |_| {
            if let Some(n) = fraco.upgrade() {
                n.view.remove_css_class("arrastando");
            }
        }));
        alvo.connect_drop(glib::clone!(#[strong] fraco, move |_, valor, _, _| {
            let Some(n) = fraco.upgrade() else { return false };
            n.view.remove_css_class("arrastando");
            let Ok(lista) = valor.get::<gdk::FileList>() else { return false };
            for f in lista.files() {
                let Some(p) = f.path() else { continue };
                let ext = p.extension().and_then(|e| e.to_str()).unwrap_or("").to_ascii_lowercase();
                if ["png", "jpg", "jpeg", "webp", "gif", "bmp"].contains(&ext.as_str()) {
                    let base = p.file_stem().map(|s| s.to_string_lossy().to_string()).unwrap_or_default();
                    match std::fs::read(&p) {
                        Ok(b) => n.anexar_bytes(&b, &base),
                        Err(e) => n.aviso(&format!("Não foi possível anexar a imagem: {e}")),
                    }
                } else if config::abre(&p) {
                    abrir_em_janela(&n.janela.application().and_downcast::<adw::Application>().expect("app"), &p, Some(&n));
                } else {
                    n.aviso("O Noty abre .hyp, .md, .txt e .html (ou arraste imagens para anexar).");
                }
                break;
            }
            true
        }));
        self.janela.add_controller(alvo);

        // fechar: com alteração, pergunta
        self.janela.connect_close_request(glib::clone!(#[strong] fraco, move |j| {
            let Some(n) = fraco.upgrade() else { return glib::Propagation::Proceed };
            if n.estado.borrow().sujo && !n.estado.borrow().pode_fechar {
                n.confirmar_saida();
                return glib::Propagation::Stop;
            }
            let (w, h) = j.default_size();
            config::Tamanho { w: w.max(420), h: h.max(320), maximizada: j.is_maximized() }.gravar();
            if let Some(m) = n.estado.borrow_mut().monitor.take() {
                m.cancel();
            }
            glib::Propagation::Proceed
        }));
    }

    // ================================================================= ações e menus
    fn criar_acoes(self: &Rc<Self>) {
        let fraco = Rc::downgrade(self);
        let g = gio::SimpleActionGroup::new();
        let simples = |nome: &str, f: fn(&Rc<Noty>)| {
            let a = gio::SimpleAction::new(nome, None);
            let fraco = fraco.clone();
            a.connect_activate(move |_, _| {
                if let Some(n) = fraco.upgrade() {
                    f(&n);
                }
            });
            g.add_action(&a);
        };
        simples("novo", |n| n.novo());
        simples("abrir", |n| n.abrir_dialogo());
        simples("salvar", |n| n.salvar(None));
        simples("salvar-como", |n| n.salvar_como(None));
        simples("sair", |n| n.janela.close());
        simples("desfazer", |n| {
            if n.buf.can_undo() {
                n.buf.undo();
            }
        });
        simples("refazer", |n| {
            if n.buf.can_redo() {
                n.buf.redo();
            }
        });
        simples("localizar", |n| n.abrir_busca());
        simples("tudo", |n| {
            let (a, b) = n.buf.bounds();
            n.buf.select_range(&a, &b);
            n.view.grab_focus();
        });
        simples("previa", |n| {
            if n.pilha.visible_child_name().as_deref() == Some("previa") {
                n.mostrar_editor();
            } else {
                n.mostrar_previa();
            }
        });
        simples("negrito", |n| n.envolver("**"));
        simples("italico", |n| n.envolver("*"));
        simples("codigo", |n| n.envolver("`"));
        simples("pesquisar", |n| {
            if let Some((a, b)) = n.buf.selection_bounds() {
                let t = n.buf.text(&a, &b, false).trim().to_string();
                if !t.is_empty() {
                    let q: String = glib::Uri::escape_string(&t, None, false).into();
                    gtk::UriLauncher::new(&format!("https://www.google.com/search?q={q}")).launch(Some(&n.janela), gio::Cancellable::NONE, |_| {});
                }
            }
        });
        simples("tema", |n| {
            let p = tema::arquivo_do_usuario();
            abrir_em_janela(&n.janela.application().and_downcast::<adw::Application>().expect("app"), &p, None);
        });
        simples("perfil", |n| {
            let _ = std::fs::create_dir_all(config::pasta());
            gtk::FileLauncher::new(Some(&gio::File::for_path(config::pasta()))).launch(Some(&n.janela), gio::Cancellable::NONE, |_| {});
        });
        simples("sobre", |n| {
            let d = adw::AboutDialog::builder()
                .application_name("Noty")
                .application_icon("com.archquilly.Noty")
                .version(env!("CARGO_PKG_VERSION"))
                .comments("Editor de texto leve, com vidro: .hyp, Markdown, texto e HTML.\n\nAlt — menu · Botão direito — corretor e formatação\nCtrl+M — pré-visualizar · Ctrl+F — localizar · Ctrl+T — tema")
                .website("https://github.com/Sapyst/quilly")
                .license_type(gtk::License::MitX11)
                .developer_name("Arch Quilly")
                .build();
            d.present(Some(&n.janela));
        });

        let quebra = gio::SimpleAction::new_stateful("quebra", None, &com_settings(|s| s.quebra()).to_variant());
        quebra.connect_activate(move |a, _| {
            let v = !a.state().and_then(|s| s.get::<bool>()).unwrap_or(true);
            a.set_state(&v.to_variant());
            com_settings(|s| {
                s.ui.wrap = Some(v);
                s.gravar();
            });
            for n in todas() {
                n.aplicar_quebra();
            }
        });
        g.add_action(&quebra);
        let corretor = gio::SimpleAction::new_stateful("corretor", None, &com_settings(|s| s.corretor()).to_variant());
        corretor.connect_activate(move |a, _| {
            let v = !a.state().and_then(|s| s.get::<bool>()).unwrap_or(true);
            a.set_state(&v.to_variant());
            com_settings(|s| {
                s.ui.spell_enabled = Some(v);
                s.gravar();
            });
            for n in todas() {
                n.aplicar_corretor();
            }
        });
        g.add_action(&corretor);
        let idioma = gio::SimpleAction::new_stateful("idioma", Some(glib::VariantTy::STRING), &com_settings(|s| s.spell_lang.clone()).to_variant());
        idioma.connect_activate(move |a, p| {
            let Some(l) = p.and_then(|v| v.get::<String>()) else { return };
            a.set_state(&l.to_variant());
            com_settings(|s| {
                s.spell_lang = l.clone();
                s.gravar();
            });
            for n in todas() {
                n.aplicar_corretor();
            }
        });
        g.add_action(&idioma);
        let recente = gio::SimpleAction::new("recente", Some(glib::VariantTy::STRING));
        recente.connect_activate(glib::clone!(#[strong] fraco, move |_, p| {
            let (Some(n), Some(c)) = (fraco.upgrade(), p.and_then(|v| v.get::<String>())) else { return };
            abrir_em_janela(&n.janela.application().and_downcast::<adw::Application>().expect("app"), Path::new(&c), Some(&n));
        }));
        g.add_action(&recente);
        self.janela.insert_action_group("noty", Some(&g));

        // o corretor: sugestões e "adicionar ao dicionário" no menu do botão direito
        self.view.insert_action_group("spelling", Some(&self.adaptador));
        let ctx = gio::Menu::new();
        ctx.append_section(None, &self.adaptador.menu_model());
        let fmt = gio::Menu::new();
        fmt.append(Some("Negrito"), Some("noty.negrito"));
        fmt.append(Some("Itálico"), Some("noty.italico"));
        fmt.append(Some("Código"), Some("noty.codigo"));
        ctx.append_section(None, &fmt);
        let web = gio::Menu::new();
        web.append(Some("Pesquisar na web"), Some("noty.pesquisar"));
        ctx.append_section(None, &web);
        let arq = gio::Menu::new();
        arq.append(Some("Abrir…"), Some("noty.abrir"));
        arq.append(Some("Salvar"), Some("noty.salvar"));
        arq.append(Some("Salvar como…"), Some("noty.salvar-como"));
        ctx.append_section(None, &arq);
        self.view.set_extra_menu(Some(&ctx));
    }

    /// O menu do Alt (refeito quando os recentes mudam).
    pub fn atualizar_menu(&self) {
        let m = gio::Menu::new();
        let arquivo = gio::Menu::new();
        let s1 = gio::Menu::new();
        s1.append(Some("Novo"), Some("noty.novo"));
        s1.append(Some("Abrir…"), Some("noty.abrir"));
        s1.append(Some("Salvar"), Some("noty.salvar"));
        s1.append(Some("Salvar como…"), Some("noty.salvar-como"));
        arquivo.append_section(None, &s1);
        let recentes = gio::Menu::new();
        for p in com_settings(|s| s.ui.recent.clone()) {
            let nome = Path::new(&p).file_name().map(|n| n.to_string_lossy().to_string()).unwrap_or_else(|| p.clone());
            let item = gio::MenuItem::new(Some(&nome), None);
            item.set_action_and_target_value(Some("noty.recente"), Some(&p.to_variant()));
            recentes.append_item(&item);
        }
        if recentes.n_items() > 0 {
            arquivo.append_section(None, &recentes);
        }
        let s3 = gio::Menu::new();
        s3.append(Some("Sair"), Some("noty.sair"));
        arquivo.append_section(None, &s3);
        m.append_submenu(Some("Arquivo"), &arquivo);

        let editar = gio::Menu::new();
        let e1 = gio::Menu::new();
        e1.append(Some("Desfazer"), Some("noty.desfazer"));
        e1.append(Some("Refazer"), Some("noty.refazer"));
        editar.append_section(None, &e1);
        let e2 = gio::Menu::new();
        e2.append(Some("Localizar…"), Some("noty.localizar"));
        e2.append(Some("Selecionar tudo"), Some("noty.tudo"));
        editar.append_section(None, &e2);
        m.append_submenu(Some("Editar"), &editar);

        let exibir = gio::Menu::new();
        exibir.append(Some("Pré-visualizar Markdown"), Some("noty.previa"));
        exibir.append(Some("Quebra de linha"), Some("noty.quebra"));
        m.append_submenu(Some("Exibir"), &exibir);

        let orto = gio::Menu::new();
        let o1 = gio::Menu::new();
        o1.append(Some("Corretor ligado"), Some("noty.corretor"));
        orto.append_section(None, &o1);
        let o2 = gio::Menu::new();
        for (codigo, _, nome) in IDIOMAS {
            let item = gio::MenuItem::new(Some(nome), None);
            item.set_action_and_target_value(Some("noty.idioma"), Some(&codigo.to_variant()));
            o2.append_item(&item);
        }
        orto.append_section(None, &o2);
        m.append_submenu(Some("Ortografia"), &orto);

        let tema = gio::Menu::new();
        tema.append(Some("Editar tema (CSS livre)…"), Some("noty.tema"));
        tema.append(Some("Abrir pasta do perfil"), Some("noty.perfil"));
        m.append_submenu(Some("Tema"), &tema);

        let ajuda = gio::Menu::new();
        ajuda.append(Some("Sobre o Noty"), Some("noty.sobre"));
        m.append_submenu(Some("Ajuda"), &ajuda);
        self.barra_menu.set_menu_model(Some(&m));
    }

    // ================================================================= arquivo
    pub fn intocada(&self) -> bool {
        let e = self.estado.borrow();
        e.caminho.is_none() && !e.sujo && self.buf.char_count() == 0
    }

    pub fn caminho(&self) -> Option<PathBuf> {
        self.estado.borrow().caminho.clone()
    }

    pub fn carregar(self: &Rc<Self>, caminho: &Path) -> Result<(), String> {
        let texto = config::ler_texto(caminho)?;
        let caminho = std::fs::canonicalize(caminho).unwrap_or_else(|_| caminho.to_path_buf());
        {
            let mut e = self.estado.borrow_mut();
            e.caminho = Some(caminho.clone());
            e.nome = caminho.file_name().map(|n| n.to_string_lossy().to_string()).unwrap_or_default();
            e.carregando = true;
        }
        self.buf.begin_irreversible_action();
        self.buf.set_text(&texto);
        self.buf.end_irreversible_action();
        self.buf.place_cursor(&self.buf.start_iter());
        self.estado.borrow_mut().carregando = false;
        self.marcar_sujo(false);
        self.aplicar_linguagem();
        self.vigiar(&caminho, texto);
        self.contar();
        self.mostrar_editor();
        com_settings(|s| s.lembrar(&caminho));
        for n in todas() {
            n.atualizar_menu();
        }
        let v = self.view.clone();
        glib::idle_add_local_once(move || {
            v.scroll_to_iter(&mut v.buffer().start_iter(), 0.0, false, 0.0, 0.0);
        });
        Ok(())
    }

    fn texto(&self) -> String {
        let (a, b) = self.buf.bounds();
        self.buf.text(&a, &b, true).to_string()
    }

    fn novo(self: &Rc<Self>) {
        let app = self.janela.application().and_downcast::<adw::Application>().expect("app");
        if self.intocada() {
            return;
        }
        let n = Noty::nova(&app);
        n.janela.present();
    }

    fn abrir_dialogo(self: &Rc<Self>) {
        let d = gtk::FileDialog::builder().title("Abrir").modal(true).build();
        d.set_filters(Some(&filtros(None)));
        if let Some(p) = self.caminho().and_then(|p| p.parent().map(Path::to_path_buf)) {
            d.set_initial_folder(Some(&gio::File::for_path(p)));
        }
        let fraco = Rc::downgrade(self);
        d.open(Some(&self.janela), gio::Cancellable::NONE, move |r| {
            if let (Some(n), Ok(f)) = (fraco.upgrade(), r)
                && let Some(p) = f.path()
            {
                let app = n.janela.application().and_downcast::<adw::Application>().expect("app");
                abrir_em_janela(&app, &p, Some(&n));
            }
        });
    }

    /// Salva; `depois` roda se salvou (pro "Salvar e sair").
    fn salvar(self: &Rc<Self>, depois: Option<Box<dyn FnOnce()>>) {
        let Some(c) = self.caminho() else {
            self.salvar_como(depois);
            return;
        };
        let nome = self.estado.borrow().nome.clone();
        let saida = hyp::para_gravar(&self.texto(), &nome);
        match config::gravar_texto(&c, &saida) {
            Ok(()) => {
                self.estado.borrow_mut().no_disco = Some(saida);
                self.marcar_sujo(false);
                if let Some(f) = depois {
                    f();
                }
            }
            Err(e) => self.aviso(&format!("Não salvou: {e}")),
        }
    }

    fn salvar_como(self: &Rc<Self>, depois: Option<Box<dyn FnOnce()>>) {
        let nome = self.estado.borrow().nome.clone();
        let ext = Path::new(&nome).extension().and_then(|e| e.to_str()).map(|e| e.to_ascii_lowercase());
        let d = gtk::FileDialog::builder().title("Salvar como").modal(true).initial_name(nome.as_str()).build();
        d.set_filters(Some(&filtros(ext.as_deref())));
        if let Some(p) = self.caminho().and_then(|p| p.parent().map(Path::to_path_buf)) {
            d.set_initial_folder(Some(&gio::File::for_path(p)));
        }
        let fraco = Rc::downgrade(self);
        d.save(Some(&self.janela), gio::Cancellable::NONE, move |r| {
            let (Some(n), Ok(f)) = (fraco.upgrade(), r) else { return };
            let Some(mut p) = f.path() else { return };
            if p.extension().is_none() {
                p.set_extension("hyp");
            }
            {
                let mut e = n.estado.borrow_mut();
                e.caminho = Some(p.clone());
                e.nome = p.file_name().map(|x| x.to_string_lossy().to_string()).unwrap_or_default();
            }
            n.aplicar_linguagem();
            n.salvar(depois);
            if !n.estado.borrow().sujo {
                let texto = n.estado.borrow().no_disco.clone().unwrap_or_default();
                n.vigiar(&p, texto);
                com_settings(|s| s.lembrar(&p));
                for x in todas() {
                    x.atualizar_menu();
                }
            }
            n.atualizar_titulo();
        });
    }

    fn confirmar_saida(self: &Rc<Self>) {
        let d = adw::AlertDialog::new(Some("Tem alteração que não foi salva."), None);
        d.add_responses(&[("ficar", "Ficar"), ("sair", "Sair sem salvar"), ("salvar", "Salvar e sair")]);
        d.set_response_appearance("sair", adw::ResponseAppearance::Destructive);
        d.set_response_appearance("salvar", adw::ResponseAppearance::Suggested);
        d.set_default_response(Some("salvar"));
        d.set_close_response("ficar");
        let fraco = Rc::downgrade(self);
        d.choose(Some(&self.janela), gio::Cancellable::NONE, move |r| {
            let Some(n) = fraco.upgrade() else { return };
            match r.as_str() {
                "sair" => {
                    n.estado.borrow_mut().pode_fechar = true;
                    n.janela.close();
                }
                "salvar" => {
                    let j = n.janela.clone();
                    let f2 = Rc::downgrade(&n);
                    n.salvar(Some(Box::new(move || {
                        if let Some(n) = f2.upgrade() {
                            n.estado.borrow_mut().pode_fechar = true;
                        }
                        j.close();
                    })));
                }
                _ => {}
            }
        });
    }

    // ---- outro programa mudou o arquivo: sem alteração aqui, atualiza; com alteração, pergunta
    fn vigiar(self: &Rc<Self>, caminho: &Path, texto: String) {
        let mut e = self.estado.borrow_mut();
        e.no_disco = Some(texto);
        if let Some(m) = e.monitor.take() {
            m.cancel();
        }
        let Ok(m) = gio::File::for_path(caminho).monitor_file(gio::FileMonitorFlags::WATCH_MOVES, gio::Cancellable::NONE) else { return };
        let fraco = Rc::downgrade(self);
        m.connect_changed(move |_, _, _, ev| {
            use gio::FileMonitorEvent as E;
            if !matches!(ev, E::ChangesDoneHint | E::Changed | E::Created | E::Renamed | E::MovedIn) {
                return;
            }
            let Some(n) = fraco.upgrade() else { return };
            if let Some(id) = n.estado.borrow_mut().recarga.take() {
                id.remove();
            }
            let f2 = Rc::downgrade(&n);
            let id = glib::timeout_add_local_once(std::time::Duration::from_millis(200), move || {
                if let Some(n) = f2.upgrade() {
                    n.estado.borrow_mut().recarga = None;
                    n.recarregar();
                }
            });
            n.estado.borrow_mut().recarga = Some(id);
        });
        e.monitor = Some(m);
    }

    fn recarregar(self: &Rc<Self>) {
        let Some(c) = self.caminho() else { return };
        let Ok(texto) = config::ler_texto(&c) else { return };
        if self.estado.borrow().no_disco.as_deref() == Some(texto.as_str()) || texto == self.texto() {
            self.estado.borrow_mut().no_disco = Some(texto);
            return;
        }
        self.estado.borrow_mut().no_disco = Some(texto.clone());
        if self.estado.borrow().sujo {
            if self.estado.borrow().perguntando {
                return;
            }
            self.estado.borrow_mut().perguntando = true;
            let d = adw::AlertDialog::new(Some("O arquivo foi mudado por outro programa."), None);
            d.add_responses(&[("manter", "Manter o meu"), ("recarregar", "Recarregar")]);
            d.set_default_response(Some("manter"));
            d.set_close_response("manter");
            let fraco = Rc::downgrade(self);
            d.choose(Some(&self.janela), gio::Cancellable::NONE, move |r| {
                let Some(n) = fraco.upgrade() else { return };
                n.estado.borrow_mut().perguntando = false;
                if r == "recarregar"
                    && let Some(c) = n.caminho()
                    && let Ok(t) = config::ler_texto(&c)
                {
                    n.aplicar_do_disco(&t);
                }
            });
            return;
        }
        self.aplicar_do_disco(&texto);
    }

    fn aplicar_do_disco(&self, texto: &str) {
        let adj = self.rolagem.vadjustment();
        let no_fim = adj.value() + adj.page_size() >= adj.upper() - 4.0;
        let topo = adj.value();
        let pos = self.buf.cursor_position();
        self.estado.borrow_mut().carregando = true;
        self.buf.begin_irreversible_action();
        self.buf.set_text(texto);
        self.buf.end_irreversible_action();
        self.estado.borrow_mut().carregando = false;
        self.buf.place_cursor(&self.buf.iter_at_offset(pos.min(self.buf.char_count())));
        self.marcar_sujo(false);
        self.contar();
        let v = self.view.clone();
        glib::idle_add_local_once(move || {
            let a = v.vadjustment().expect("rolagem");
            a.set_value(if no_fim { a.upper() } else { topo });
        });
    }

    // ================================================================= anexos
    fn anexar_textura(self: &Rc<Self>, tex: &gdk::Texture, base: &str) {
        let png = tex.save_to_png_bytes();
        self.anexar_bytes(&png, base);
    }

    fn anexar_bytes(self: &Rc<Self>, bytes: &[u8], base: &str) {
        let (mime, ext, b64) = match hyp::preparar_imagem(bytes) {
            Ok(x) => x,
            Err(e) => {
                self.aviso(&format!("Não foi possível anexar a imagem: {e}"));
                return;
            }
        };
        let id = hyp::novo_id(&self.texto(), base, ext);
        let base = if base.is_empty() { "imagem" } else { base };
        self.buf.begin_user_action();
        self.buf.delete_selection(true, true);
        self.buf.insert_at_cursor(&format!("![{base}](anexo:{id})"));
        let mut fim = self.buf.end_iter();
        let texto = self.texto();
        let resto = texto.trim_end();
        let corte = self.buf.iter_at_offset(resto.chars().count() as i32);
        let mut ini = corte;
        self.buf.delete(&mut ini, &mut fim);
        let mut fim = self.buf.end_iter();
        let cab = if texto.contains(hyp::CABECALHO) { String::from("\n") } else { format!("\n\n{}\n", hyp::CABECALHO) };
        self.buf.insert(&mut fim, &format!("{cab}{}", hyp::bloco(&id, mime, &b64)));
        self.buf.end_user_action();
    }

    // ================================================================= edição
    fn envolver(&self, marca: &str) {
        if self.eh_texto_puro() {
            return;
        }
        let Some((mut a, mut b)) = self.buf.selection_bounds() else { return };
        let trecho = self.buf.text(&a, &b, true).to_string();
        let ini = a.offset();
        self.buf.begin_user_action();
        self.buf.delete(&mut a, &mut b);
        let novo = format!("{marca}{trecho}{marca}");
        self.buf.insert(&mut a, &novo);
        self.buf.end_user_action();
        let s = self.buf.iter_at_offset(ini);
        let e = self.buf.iter_at_offset(ini + novo.chars().count() as i32);
        self.buf.select_range(&s, &e);
    }

    fn eh_texto_puro(&self) -> bool {
        self.estado.borrow().nome.to_lowercase().ends_with(".txt")
    }

    // ================================================================= aparência
    fn aplicar_linguagem(&self) {
        let nome = self.estado.borrow().nome.to_lowercase();
        let lm = sourceview5::LanguageManager::default();
        let lang = if nome.ends_with(".txt") {
            None
        } else if nome.ends_with(".css") {
            lm.language("css")
        } else if nome.ends_with(".html") || nome.ends_with(".htm") {
            lm.language("html")
        } else {
            lm.language("markdown")
        };
        self.buf.set_language(lang.as_ref());
        self.atualizar_titulo();
    }

    fn aplicar_quebra(&self) {
        let q = com_settings(|s| s.quebra());
        self.view.set_wrap_mode(if q { gtk::WrapMode::WordChar } else { gtk::WrapMode::None });
        self.centralizar();
    }

    fn aplicar_corretor(&self) {
        let (ligado, lang) = com_settings(|s| (s.corretor(), s.spell_lang.clone()));
        let codigo = IDIOMAS.iter().find(|(c, _, _)| *c == lang).map_or("pt_BR", |(_, d, _)| d);
        self.corretor.set_language(codigo);
        self.adaptador.set_enabled(ligado);
    }

    /// A coluna de texto: ~68 caracteres no meio da janela (com a quebra ligada).
    fn centralizar(&self) {
        let largura = self.rolagem.width();
        if largura <= 0 {
            return;
        }
        let ctx = self.view.pango_context();
        let metricas = ctx.metrics(None, None);
        let cw = (metricas.approximate_char_width() as f64 / gtk::pango::SCALE as f64).max(6.0);
        let coluna = (cw * 68.0) as i32;
        let margem = if com_settings(|s| s.quebra()) { ((largura - coluna) / 2).max(24) } else { 24 };
        self.view.set_left_margin(margem);
        self.view.set_right_margin(margem);
        let mp = ((self.rolagem.width() - (cw * 76.0) as i32) / 2).max(28);
        previa::margens(&self.previa, mp);
    }

    fn marcar_sujo(&self, v: bool) {
        if self.estado.borrow().sujo == v {
            return;
        }
        self.estado.borrow_mut().sujo = v;
        self.atualizar_titulo();
    }

    fn atualizar_titulo(&self) {
        let e = self.estado.borrow();
        let t = format!("{}{} — Noty", if e.sujo { "• " } else { "" }, e.nome);
        self.titulo.set_text(&t);
        self.janela.set_title(Some(&t));
    }

    fn agendar_contagem(self: &Rc<Self>) {
        if let Some(id) = self.estado.borrow_mut().contagem.take() {
            id.remove();
        }
        let fraco = Rc::downgrade(self);
        let id = glib::timeout_add_local_once(std::time::Duration::from_millis(180), move || {
            if let Some(n) = fraco.upgrade() {
                n.estado.borrow_mut().contagem = None;
                n.contar();
            }
        });
        self.estado.borrow_mut().contagem = Some(id);
    }

    /// A seção de anexos (base64) fica miúda e apagada no editor: está lá, mas não polui o texto.
    fn apagar_anexos(&self, texto: &str) {
        let tabela = self.buf.tag_table();
        let tag = tabela.lookup("anexos").unwrap_or_else(|| {
            let t = gtk::TextTag::builder().name("anexos").scale(0.62).foreground_rgba(&gdk::RGBA::new(0.55, 0.55, 0.55, 0.45)).build();
            tabela.add(&t);
            t
        });
        tag.set_priority(tabela.size() - 1);
        let (a, b) = self.buf.bounds();
        self.buf.remove_tag(&tag, &a, &b);
        if let Some(i) = texto.find(hyp::CABECALHO) {
            let ini = self.buf.iter_at_offset(texto[..i].chars().count() as i32);
            self.buf.apply_tag(&tag, &ini, &b);
        }
    }

    fn contar(&self) {
        let texto = self.texto();
        self.apagar_anexos(&texto);
        let (p, c, l) = hyp::contar(&texto);
        let s = |n: usize, um: &str, varios: &str| format!("{n} {}", if n == 1 { um } else { varios });
        self.status.set_text(&format!(
            "{} · {} · {}",
            s(p, "Palavra", "Palavras"),
            s(c, "Caractere", "Caracteres"),
            s(l, "Linha", "Linhas")
        ));
    }

    fn mostrar_previa(&self) {
        previa::mostrar(&self.previa, &self.texto(), tema::cores().verde());
        self.pilha.set_visible_child_name("previa");
        self.centralizar();
    }

    fn mostrar_editor(&self) {
        self.pilha.set_visible_child_name("editor");
        self.view.grab_focus();
    }

    // ================================================================= busca
    fn abrir_busca(&self) {
        self.mostrar_editor();
        if let Some((a, b)) = self.buf.selection_bounds() {
            let t = self.buf.text(&a, &b, false);
            if t.len() < 80 && !t.contains('\n') {
                self.busca_campo.set_text(&t);
            }
        }
        self.busca.set_reveal_child(true);
        self.busca_campo.grab_focus();
        self.busca_campo.select_region(0, -1);
    }

    fn fechar_busca(&self) {
        self.busca.set_reveal_child(false);
        self.busca_ctx.settings().set_search_text(None);
        self.view.grab_focus();
    }

    fn procurar(&self, para_tras: bool) {
        let (ini, fim) = self.buf.selection_bounds().unwrap_or_else(|| {
            let c = self.buf.iter_at_offset(self.buf.cursor_position());
            (c, c)
        });
        let achou = if para_tras { self.busca_ctx.backward(&ini) } else { self.busca_ctx.forward(&fim) };
        if let Some((a, b, _)) = achou {
            self.buf.select_range(&a, &b);
            self.view.scroll_to_iter(&mut a.clone(), 0.2, true, 0.0, 0.5);
        }
    }

    pub fn aviso(&self, msg: &str) {
        let d = adw::AlertDialog::new(None, Some(msg));
        d.add_response("ok", "OK");
        d.present(Some(&self.janela));
    }

    /// O tema do sistema (ou o theme-gtk.css) mudou.
    pub fn tema_mudou(&self) {
        tema::aplicar_esquema(&self.buf);
        if self.pilha.visible_child_name().as_deref() == Some("previa") {
            previa::mostrar(&self.previa, &self.texto(), tema::cores().verde());
        }
    }

    pub fn foco(&self) {
        self.janela.present();
        self.view.grab_focus();
    }
}

fn filtros(primeiro: Option<&str>) -> gio::ListStore {
    let store = gio::ListStore::new::<gtk::FileFilter>();
    let mut lista = vec![
        ("hyp", "Documento Hyper", vec!["*.hyp"]),
        ("todos-textos", "Textos (.hyp .md .txt .html)", vec!["*.hyp", "*.md", "*.markdown", "*.txt", "*.html", "*.htm"]),
        ("md", "Markdown", vec!["*.md", "*.markdown"]),
        ("html", "Página HTML", vec!["*.html", "*.htm"]),
        ("txt", "Texto", vec!["*.txt"]),
    ];
    if let Some(e) = primeiro
        && let Some(i) = lista.iter().position(|(x, _, _)| *x == e)
    {
        let f = lista.remove(i);
        lista.insert(0, f);
    }
    for (_, nome, padroes) in lista {
        let f = gtk::FileFilter::new();
        f.set_name(Some(nome));
        for p in padroes {
            f.add_pattern(p);
        }
        store.append(&f);
    }
    let todos = gtk::FileFilter::new();
    todos.set_name(Some("Todos"));
    todos.add_pattern("*");
    store.append(&todos);
    store
}

/// Abre `caminho`: na janela que já tem ele; senão na `aqui` se ela estiver intocada; senão numa nova.
pub fn abrir_em_janela(app: &adw::Application, caminho: &Path, aqui: Option<&Rc<Noty>>) {
    let alvo = std::fs::canonicalize(caminho).unwrap_or_else(|_| caminho.to_path_buf());
    if let Some(n) = todas().into_iter().find(|n| n.caminho().as_deref() == Some(alvo.as_path())) {
        n.foco();
        return;
    }
    let n = match aqui {
        Some(n) if n.intocada() => n.clone(),
        _ => Noty::nova(app),
    };
    match n.carregar(caminho) {
        Ok(()) => n.foco(),
        Err(e) => {
            n.janela.present();
            n.aviso(&format!("Não abriu: {e}"));
            com_settings(|s| s.esquecer(&caminho.to_string_lossy()));
            for x in todas() {
                x.atualizar_menu();
            }
        }
    }
}
