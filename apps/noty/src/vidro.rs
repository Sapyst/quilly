//! O vidro do Arch Aero: a cor do tema atual (claro/escuro) e a opacidade dos outros apps, lidas do
//! hypr-tema (~/.cache/hypr-tema/tema e cores). Sem o hypr-tema (outro desktop), o escuro a 85%.
//!
//! A janela inteira é UMA camada desse vidro (a barra de título e o texto em cima), e o Hyprland
//! desfoca o que está atrás. O verde do cursor e da seleção é a cara do Noty.

use std::path::PathBuf;

#[derive(Clone, Debug, PartialEq)]
pub struct Cores {
    pub escuro: bool,
    pub fundo: (u8, u8, u8),
    pub alfa: f32,
}

fn estado() -> PathBuf {
    std::env::var_os("XDG_CACHE_HOME")
        .filter(|v| !v.is_empty())
        .map(PathBuf::from)
        .unwrap_or_else(|| crate::config::home().join(".cache"))
        .join("hypr-tema")
}

pub fn arquivos_do_tema() -> [PathBuf; 2] {
    let e = estado();
    [e.join("tema"), e.join("cores")]
}

fn hex(s: &str) -> Option<(u8, u8, u8)> {
    let s = s.trim().trim_start_matches('#');
    if s.len() != 6 {
        return None;
    }
    let b = |i: usize| s.get(i..i + 2).and_then(|h| u8::from_str_radix(h, 16).ok());
    Some((b(0)?, b(2)?, b(4)?))
}

pub fn ler() -> Cores {
    let e = estado();
    let modo = std::fs::read_to_string(e.join("tema")).unwrap_or_default();
    let escuro = modo.trim() != "claro";
    let mut clara = (246, 246, 246);
    let mut escura = (30, 30, 30);
    let mut alfa = 0.85_f32;
    if let Ok(c) = std::fs::read_to_string(e.join("cores")) {
        // "COR_SINC COR_CLARA COR_ESCURA COR_ALFA COR_DESTAQUE BAR_ALPHA"
        let p: Vec<&str> = c.split_whitespace().collect();
        if let Some(v) = p.get(1).and_then(|s| hex(s)) {
            clara = v;
        }
        if let Some(v) = p.get(2).and_then(|s| hex(s)) {
            escura = v;
        }
        if let Some(v) = p.get(3).and_then(|s| s.parse::<f32>().ok()) {
            alfa = v;
        }
    }
    // texto de editor precisa de chão firme: o vidro do Noty é um pouco mais denso que o das barras
    let alfa = (alfa + 0.08).clamp(0.82, 0.97);
    Cores { escuro, fundo: if escuro { escura } else { clara }, alfa }
}

impl Cores {
    fn rgba(&self, a: f32) -> String {
        let (r, g, b) = self.fundo;
        format!("rgba({r}, {g}, {b}, {a:.2})")
    }
    fn texto(&self, a: f32) -> String {
        if self.escuro { format!("rgba(240, 240, 240, {a:.2})") } else { format!("rgba(30, 30, 30, {a:.2})") }
    }
    pub fn verde(&self) -> &'static str {
        if self.escuro { "#8fd6a8" } else { "#2e8a54" }
    }
    fn borda(&self) -> &'static str {
        if self.escuro { "rgba(255, 255, 255, 0.08)" } else { "rgba(0, 0, 0, 0.09)" }
    }
    fn realce(&self) -> &'static str {
        if self.escuro { "rgba(130, 220, 165, 0.16)" } else { "rgba(46, 138, 84, 0.14)" }
    }
    fn selecao(&self) -> &'static str {
        if self.escuro { "rgba(126, 214, 158, 0.30)" } else { "rgba(46, 138, 84, 0.22)" }
    }

    /// O CSS do GTK: janela transparente, a moldura inteira com o vidro, o editor por cima.
    pub fn css(&self) -> String {
        let fundo = self.rgba(self.alfa);
        let painel = self.rgba(0.97);
        let texto = self.texto(0.96);
        let suave = self.texto(0.50);
        format!(
            r#"
window.noty, window.noty.background {{ background: transparent; color: {texto}; --accent-bg-color: {verde}; --accent-color: {verde}; --accent-fg-color: {sobre_verde}; }}
window.noty .vidro {{ background-color: {fundo}; }}
window.noty headerbar {{ background: transparent; box-shadow: none; color: {texto}; min-height: 34px; }}
window.noty headerbar .title {{ font-weight: 500; opacity: 0.85; }}
window.noty textview, window.noty textview text {{ background: transparent; color: {texto}; }}
window.noty textview.editor {{ font-family: "Cascadia Mono", "Adwaita Mono", "JetBrains Mono", "DejaVu Sans Mono", monospace; font-size: 15px; caret-color: {verde}; }}
window.noty textview.previa {{ font-family: "Inter", "Adwaita Sans", sans-serif; font-size: 15.5px; }}
window.noty textview text selection {{ background-color: {selecao}; }}
window.noty scrollbar slider {{ background-color: {rolagem}; min-width: 6px; }}
window.noty scrollbar slider:hover {{ background-color: {verde}; }}
window.noty .status {{ color: {suave}; font-size: 12px; padding: 7px 12px; border-top: 1px solid {borda}; }}
window.noty .busca {{ background: {painel}; border: 1px solid {borda}; border-radius: 10px; padding: 6px 8px; margin: 10px 14px; box-shadow: 0 8px 26px rgba(0,0,0,.35); }}
window.noty .busca .cont {{ color: {suave}; font-size: 12px; min-width: 34px; }}
window.noty .menu-alt {{ background: {painel}; border-bottom: 1px solid {borda}; }}
window.noty .menu-alt > item:hover, window.noty .menu-alt > item:selected {{ background: {realce}; border-radius: 6px; }}
window.noty popover > contents, window.noty popover.menu > contents {{ background: {painel}; color: {texto}; border: 1px solid {borda}; }}
window.noty popover modelbutton:hover {{ background: {realce}; }}
window.noty .arrastando {{ box-shadow: inset 0 0 0 2px {verde}; }}
"#,
            verde = self.verde(),
            selecao = self.selecao(),
            realce = self.realce(),
            borda = self.borda(),
            rolagem = self.texto(0.18),
            sobre_verde = if self.escuro { "#0d1f14" } else { "#ffffff" },
        )
    }

    /// O esquema de cores do GtkSourceView: sem fundo (o vidro aparece), Markdown discreto.
    pub fn esquema(&self) -> String {
        let texto = if self.escuro { "#f0f0f0" } else { "#1e1e1e" };
        let suave = if self.escuro { "#9a9a9a" } else { "#6a6a6a" };
        let codigo = if self.escuro { "#cfe9d9" } else { "#24563a" };
        format!(
            r#"<?xml version="1.0" encoding="UTF-8"?>
<style-scheme id="noty" name="Noty" version="1.0">
  <style name="text" foreground="{texto}"/>
  <style name="selection" background="{sel}"/>
  <style name="selection-unfocused" background="{sel}"/>
  <style name="cursor" foreground="{verde}"/>
  <style name="current-line" background="{linha}"/>
  <style name="search-match" background="{busca}"/>
  <style name="bracket-match" bold="true"/>
  <style name="def:heading" bold="true" foreground="{texto}"/>
  <style name="def:emphasis" italic="true"/>
  <style name="def:strong-emphasis" bold="true"/>
  <style name="def:preformatted-section" foreground="{codigo}"/>
  <style name="def:inline-code" foreground="{codigo}"/>
  <style name="def:link-text" foreground="{verde}" underline="single"/>
  <style name="def:link-destination" foreground="{suave}"/>
  <style name="def:list-marker" foreground="{verde}" bold="true"/>
  <style name="def:comment" foreground="{suave}" italic="true"/>
  <style name="def:shebang" foreground="{suave}"/>
  <style name="markdown:header" bold="true" scale="1.15"/>
  <style name="markdown:horizontal-rule" foreground="{suave}"/>
  <style name="markdown:blockquote-marker" foreground="{verde}"/>
  <style name="markdown:url" foreground="{suave}"/>
</style-scheme>
"#,
            verde = self.verde(),
            sel = if self.escuro { "#3f5c49" } else { "#bcdcc8" },
            linha = if self.escuro { "#ffffff08" } else { "#00000006" },
            busca = if self.escuro { "#5b7a3a" } else { "#e7d97a" },
        )
    }
}
