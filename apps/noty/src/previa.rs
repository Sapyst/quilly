//! Ctrl+M: a pré-visualização do Markdown num TextView só de leitura, com as imagens do .hyp.
//! Nada de navegador embutido: o pulldown-cmark lê o Markdown e cada pedaço vira uma etiqueta
//! (título, negrito, citação, código, link…) ou uma imagem no meio do texto.

use gtk::prelude::*;
use gtk::{gdk, glib, pango};
use pulldown_cmark::{Event, HeadingLevel, Options, Parser, Tag, TagEnd};

use crate::hyp;

fn etiquetas(buf: &gtk::TextBuffer, verde: &str) {
    let tabela = buf.tag_table();
    if tabela.lookup("negrito").is_some() {
        return;
    }
    let add = |t: gtk::TextTag| {
        tabela.add(&t);
    };
    for (n, escala) in [("h1", 1.9), ("h2", 1.55), ("h3", 1.3), ("h4", 1.15), ("h5", 1.05), ("h6", 1.0)] {
        add(gtk::TextTag::builder().name(n).weight(700).scale(escala).pixels_above_lines(14).pixels_below_lines(6).build());
    }
    add(gtk::TextTag::builder().name("negrito").weight(700).build());
    add(gtk::TextTag::builder().name("italico").style(pango::Style::Italic).build());
    add(gtk::TextTag::builder().name("riscado").strikethrough(true).build());
    add(gtk::TextTag::builder().name("codigo").family("Cascadia Mono, JetBrains Mono, monospace").scale(0.92).build());
    add(gtk::TextTag::builder()
        .name("bloco-codigo")
        .family("Cascadia Mono, JetBrains Mono, monospace")
        .scale(0.92)
        .left_margin(28)
        .paragraph_background_rgba(&gdk::RGBA::new(0.0, 0.0, 0.0, 0.22))
        .build());
    add(gtk::TextTag::builder()
        .name("citacao")
        .left_margin(30)
        .pixels_above_lines(6)
        .pixels_below_lines(10)
        .style(pango::Style::Italic)
        .foreground(verde)
        .build());
    add(gtk::TextTag::builder().name("link").foreground(verde).underline(pango::Underline::Single).build());
    add(gtk::TextTag::builder().name("item").left_margin(26).build());
    add(gtk::TextTag::builder().name("paragrafo").pixels_below_lines(10).build());
    add(gtk::TextTag::builder().name("respiro").scale(0.45).build());
}

/// Desenha `texto` (Markdown do .hyp) em `view`. Links abrem no navegador com um clique.
pub fn mostrar(view: &gtk::TextView, texto: &str, verde: &str) {
    let buf = view.buffer();
    buf.set_text("");
    etiquetas(&buf, verde);
    let mapa = hyp::anexos(texto);
    let limpo = hyp::sem_anexos(texto);
    let opcoes = Options::ENABLE_TABLES | Options::ENABLE_STRIKETHROUGH;
    let mut pilha: Vec<&'static str> = Vec::new();
    let mut listas: Vec<Option<u64>> = Vec::new();
    let mut link: Option<String> = None;
    let mut na_imagem = false;
    let mut fim = buf.end_iter();

    // começa numa linha nova (se ainda não estiver no começo de uma)
    let quebra = |buf: &gtk::TextBuffer, fim: &mut gtk::TextIter| {
        if fim.offset() > 0 {
            let mut antes = *fim;
            antes.backward_char();
            if antes.char() != '\n' {
                buf.insert(fim, "\n");
            }
        }
    };

    for ev in Parser::new_ext(&limpo, opcoes) {
        match ev {
            Event::Start(Tag::Heading { level, .. }) => {
                quebra(&buf, &mut fim);
                pilha.push(match level {
                    HeadingLevel::H1 => "h1",
                    HeadingLevel::H2 => "h2",
                    HeadingLevel::H3 => "h3",
                    HeadingLevel::H4 => "h4",
                    HeadingLevel::H5 => "h5",
                    HeadingLevel::H6 => "h6",
                });
            }
            Event::End(TagEnd::Heading(_)) => {
                pilha.pop();
                buf.insert(&mut fim, "\n");
            }
            Event::Start(Tag::Paragraph) => {
                if listas.is_empty() {
                    pilha.push("paragrafo");
                }
            }
            Event::End(TagEnd::Paragraph) => {
                if listas.is_empty() {
                    pilha.pop();
                    buf.insert(&mut fim, "\n");
                }
            }
            Event::Start(Tag::Strong) => pilha.push("negrito"),
            Event::Start(Tag::Emphasis) => pilha.push("italico"),
            Event::Start(Tag::Strikethrough) => pilha.push("riscado"),
            Event::End(TagEnd::Strong | TagEnd::Emphasis | TagEnd::Strikethrough) => {
                pilha.pop();
            }
            Event::Start(Tag::BlockQuote(_)) => pilha.push("citacao"),
            Event::End(TagEnd::BlockQuote(_)) => {
                pilha.pop();
            }
            Event::Start(Tag::CodeBlock(_)) => {
                quebra(&buf, &mut fim);
                pilha.push("bloco-codigo");
            }
            Event::End(TagEnd::CodeBlock) => {
                pilha.pop();
                buf.insert(&mut fim, "\n");
            }
            Event::Start(Tag::List(inicio)) => listas.push(inicio),
            Event::End(TagEnd::List(_)) => {
                listas.pop();
                if listas.is_empty() {
                    quebra(&buf, &mut fim);
                    buf.insert_with_tags_by_name(&mut fim, "\n", &["respiro"]);
                }
            }
            Event::Start(Tag::Item) => {
                quebra(&buf, &mut fim);
                let marca = match listas.last_mut() {
                    Some(Some(n)) => {
                        let m = format!("{n}. ");
                        *n += 1;
                        m
                    }
                    _ => "•  ".to_string(),
                };
                let recuo = "    ".repeat(listas.len().saturating_sub(1));
                buf.insert_with_tags_by_name(&mut fim, &format!("{recuo}{marca}"), &["item"]);
                pilha.push("item");
            }
            Event::End(TagEnd::Item) => {
                pilha.pop();
            }
            Event::Start(Tag::Link { dest_url, .. }) => {
                link = Some(dest_url.to_string());
                pilha.push("link");
            }
            Event::End(TagEnd::Link) => {
                pilha.pop();
                link = None;
            }
            Event::Start(Tag::Image { dest_url, .. }) => {
                na_imagem = true;
                let url = dest_url.to_string();
                let bytes = url
                    .strip_prefix("anexo:")
                    .and_then(|id| mapa.get(&id.to_lowercase()))
                    .and_then(|(_, b64)| hyp::bytes_do_anexo(b64));
                quebra(&buf, &mut fim);
                match bytes.and_then(|b| gdk::Texture::from_bytes(&glib::Bytes::from_owned(b)).ok()) {
                    Some(tex) => {
                        let ancora = buf.create_child_anchor(&mut fim);
                        let largura = 640.min(tex.width());
                        let alto = (tex.height() as f64 * largura as f64 / tex.width().max(1) as f64) as i32;
                        let foto = gtk::Picture::for_paintable(&tex);
                        foto.set_size_request(largura, alto);
                        foto.set_can_shrink(true);
                        foto.add_css_class("card");
                        view.add_child_at_anchor(&foto, &ancora);
                        buf.insert(&mut fim, "\n");
                    }
                    None => buf.insert_with_tags_by_name(&mut fim, &format!("[imagem: {url}]"), &["italico"]),
                }
            }
            Event::End(TagEnd::Image) => na_imagem = false,
            Event::Text(_) | Event::Code(_) if !na_imagem => {
                let mut nomes: Vec<&str> = pilha.clone();
                let t = match ev {
                    Event::Code(t) => {
                        nomes.push("codigo");
                        t
                    }
                    Event::Text(t) => t,
                    _ => unreachable!(),
                };
                let ini = fim.offset();
                buf.insert(&mut fim, &t);
                let comeco = buf.iter_at_offset(ini);
                for n in &nomes {
                    buf.apply_tag_by_name(n, &comeco, &fim);
                }
                if let Some(url) = &link {
                    // o endereço mora no nome da etiqueta: "url:" + endereço
                    let nome = format!("url:{url}");
                    let tag = buf.tag_table().lookup(&nome).unwrap_or_else(|| {
                        let t = gtk::TextTag::builder().name(nome.as_str()).build();
                        buf.tag_table().add(&t);
                        t
                    });
                    buf.apply_tag(&tag, &buf.iter_at_offset(ini), &fim);
                }
            }
            Event::SoftBreak => buf.insert(&mut fim, " "),
            Event::HardBreak => buf.insert(&mut fim, "\n"),
            Event::Rule => {
                quebra(&buf, &mut fim);
                buf.insert_with_tags_by_name(&mut fim, "⸻\n", &["citacao"]);
            }
            _ => {}
        }
    }
}

/// A margem de uma etiqueta substitui a da view: as recuadas (citação, lista, código) somam o recuo
/// à margem da coluna central.
pub fn margens(view: &gtk::TextView, base: i32) {
    view.set_left_margin(base);
    view.set_right_margin(base);
    let tabela = view.buffer().tag_table();
    for (nome, recuo) in [("citacao", 30), ("item", 26), ("bloco-codigo", 28)] {
        if let Some(t) = tabela.lookup(nome) {
            t.set_left_margin(base + recuo);
        }
    }
}

/// O link (se houver) sob a posição (x, y) da janela de texto.
pub fn link_em(view: &gtk::TextView, x: f64, y: f64) -> Option<String> {
    let (bx, by) = view.window_to_buffer_coords(gtk::TextWindowType::Widget, x as i32, y as i32);
    let it = view.iter_at_location(bx, by)?;
    it.tags().iter().find_map(|t| t.name().and_then(|n| n.strip_prefix("url:").map(str::to_string)))
}
