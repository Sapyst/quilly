//! O documento Hyper (.hyp): Markdown com as imagens embutidas no fim do próprio arquivo.
//!
//!   ![legenda](anexo:foto-1.jpg)            ← no texto
//!   <!-- ===== ANEXOS HYP ===== -->         ← separador, uma vez
//!   ```hyp-anexo foto-1.jpg image/jpeg      ← cada anexo: id, tipo
//!   …base64 em linhas de 76…
//!   ```
//!
//! O mesmo formato do Noty antigo, do Artry e do Volury: os arquivos continuam abrindo em todos.

use std::collections::HashMap;
use std::sync::LazyLock;

use base64::Engine;
use regex::Regex;

pub const CABECALHO: &str = "<!-- ===== ANEXOS HYP ===== -->";

static BLOCO: LazyLock<Regex> = LazyLock::new(|| {
    Regex::new(r"(?is)```hyp-anexo\s+([\w.\-]+)\s+([\w.\-/+]+)\s*\n(.*?)```").expect("regex fixa")
});
static BLOCO_SOLTO: LazyLock<Regex> = LazyLock::new(|| Regex::new(r"(?is)```hyp-anexo.*?```").expect("regex fixa"));
static SEPARADOR: LazyLock<Regex> =
    LazyLock::new(|| Regex::new(r"(?i)<!--\s*=====\s*ANEXOS HYP\s*=====\s*-->").expect("regex fixa"));
static IMG_ANEXO: LazyLock<Regex> = LazyLock::new(|| Regex::new(r"!\[([^\]]*)\]\(anexo:[^)\s]+\)").expect("regex fixa"));
static PALAVRA: LazyLock<Regex> =
    LazyLock::new(|| Regex::new(r"[\p{L}\p{N}]+(?:['’-][\p{L}\p{N}]+)*").expect("regex fixa"));

/// Os anexos: id (minúsculo) → (tipo, base64 sem quebras).
pub fn anexos(texto: &str) -> HashMap<String, (String, String)> {
    BLOCO
        .captures_iter(texto)
        .map(|c| (c[1].to_lowercase(), (c[2].to_string(), c[3].split_whitespace().collect::<String>())))
        .collect()
}

/// O texto sem os blocos de anexo (o que conta palavras e o que a prévia mostra).
pub fn sem_anexos(texto: &str) -> String {
    SEPARADOR.replace_all(&BLOCO_SOLTO.replace_all(texto, ""), "").into_owned()
}

/// (palavras, caracteres, linhas), sem contar os anexos — como a barra de status do Noty antigo.
pub fn contar(texto: &str) -> (usize, usize, usize) {
    let limpo = sem_anexos(texto);
    // uma imagem anexada conta só a legenda, não o "anexo:foto-1.jpg"
    let palavras = PALAVRA.find_iter(&IMG_ANEXO.replace_all(&limpo, "$1")).count();
    let chars = limpo.chars().count();
    let linhas = if limpo.is_empty() { 1 } else { limpo.split('\n').count() };
    (palavras, chars, linhas)
}

/// Salvar como .txt: sem anexos, e cada imagem vira só a legenda entre colchetes.
pub fn para_txt(texto: &str) -> String {
    let t = sem_anexos(texto);
    let t = IMG_ANEXO.replace_all(&t, "[$1]");
    format!("{}\n", t.trim_end())
}

/// Troca `anexo:id` pelos dados da imagem (data:…;base64,…), pra HTML e prévia.
pub fn com_dados(texto: &str) -> String {
    let mapa = anexos(texto);
    let limpo = sem_anexos(texto);
    static URL: LazyLock<Regex> = LazyLock::new(|| Regex::new(r"\]\(anexo:([^)\s]+)\)").expect("regex fixa"));
    URL.replace_all(&limpo, |c: &regex::Captures| match mapa.get(&c[1].to_lowercase()) {
        Some((mime, b64)) => format!("](data:{mime};base64,{b64})"),
        None => c[0].to_string(),
    })
    .into_owned()
}

fn escapar(s: &str) -> String {
    s.replace('&', "&amp;").replace('<', "&lt;").replace('>', "&gt;").replace('"', "&quot;")
}

/// Salvar como .html: uma página autocontida (imagens dentro), clara ou escura conforme o sistema.
pub fn para_html(texto: &str, titulo: &str) -> String {
    let md = com_dados(texto);
    let mut corpo = String::new();
    let opcoes = pulldown_cmark::Options::ENABLE_TABLES | pulldown_cmark::Options::ENABLE_STRIKETHROUGH;
    pulldown_cmark::html::push_html(&mut corpo, pulldown_cmark::Parser::new_ext(&md, opcoes));
    let titulo = if titulo.is_empty() { "Documento Hyper" } else { titulo };
    format!(
        r#"<!DOCTYPE html>
<html lang="pt-BR">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1.0">
<title>{t}</title>
<style>
  :root {{ color-scheme: dark light; }}
  body {{ max-width: 760px; margin: 40px auto; padding: 0 24px;
    font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Helvetica, Arial, sans-serif;
    font-size: 16px; line-height: 1.75; color: #e0e6ed; background: #0f1412; }}
  @media (prefers-color-scheme: light) {{
    body {{ color: #1e252b; background: #fdfdfd; }}
    code, pre {{ background: rgba(0,0,0,0.05) !important; }}
    blockquote {{ border-color: #2e7d32 !important; color: #555 !important; }}
  }}
  h1, h2, h3, h4 {{ margin: 1.6em 0 0.6em; line-height: 1.3; font-weight: 700; }}
  p, ul, ol, blockquote {{ margin-bottom: 1.2em; }}
  ul, ol {{ padding-left: 28px; }}
  li {{ margin-bottom: 0.3em; }}
  img {{ max-width: 100%; height: auto; border-radius: 9px; box-shadow: 0 6px 24px rgba(0,0,0,0.25); margin: 16px 0; display: block; }}
  code {{ font-family: "Cascadia Mono", Consolas, monospace; font-size: 0.9em; background: rgba(255,255,255,0.08); padding: 3px 6px; border-radius: 5px; }}
  pre {{ background: rgba(0,0,0,0.35); padding: 16px; border-radius: 8px; overflow-x: auto; margin: 16px 0; }}
  pre code {{ background: transparent; padding: 0; }}
  blockquote {{ border-left: 4px solid #8fd6a8; padding: 8px 18px; margin: 18px 0; color: #9bb0a4; background: rgba(143,214,168,0.05); border-radius: 0 8px 8px 0; }}
  a {{ color: #8fd6a8; text-decoration: none; font-weight: 500; }} a:hover {{ text-decoration: underline; }}
  hr {{ border: none; border-top: 1px solid rgba(255,255,255,0.12); margin: 36px 0; }}
</style>
</head>
<body>
{corpo}
</body>
</html>
"#,
        t = escapar(titulo)
    )
}

/// O texto pronto pra gravar, conforme a extensão escolhida.
pub fn para_gravar(texto: &str, nome: &str) -> String {
    let n = nome.to_lowercase();
    let pagina = texto.trim_start().get(..15).is_some_and(|c| c.to_ascii_lowercase().starts_with("<!doctype html") || c.to_ascii_lowercase().starts_with("<html"));
    if (n.ends_with(".html") || n.ends_with(".htm")) && !pagina {
        let titulo = nome.rsplit_once('.').map_or(nome, |(a, _)| a);
        para_html(texto, titulo)
    } else if n.ends_with(".txt") {
        para_txt(texto)
    } else {
        texto.to_string()
    }
}

/// Um id que ainda não existe no texto: `foto-1.jpg`, `foto-2.jpg`…
pub fn novo_id(texto: &str, base: &str, ext: &str) -> String {
    let mut base: String = base.chars().filter(|c| c.is_ascii_alphanumeric() || *c == '_' || *c == '-').collect();
    base.make_ascii_lowercase();
    if base.is_empty() {
        base = "imagem".into();
    }
    let baixo = texto.to_lowercase();
    (1..).map(|n| format!("{base}-{n}{ext}")).find(|id| !baixo.contains(id)).unwrap_or_else(|| format!("{base}{ext}"))
}

/// O bloco de um anexo, com o base64 em linhas de 76.
pub fn bloco(id: &str, mime: &str, b64: &str) -> String {
    let linhas: Vec<&str> = b64.as_bytes().chunks(76).filter_map(|c| std::str::from_utf8(c).ok()).collect();
    format!("```hyp-anexo {id} {mime}\n{}\n```\n", linhas.join("\n"))
}

/// Imagem pra anexar: no máximo 1920 px no lado maior; JPEG (q 85), ou PNG se tiver transparência.
pub fn preparar_imagem(bytes: &[u8]) -> Result<(&'static str, &'static str, String), String> {
    let img = image::load_from_memory(bytes).map_err(|e| format!("imagem ilegível: {e}"))?;
    let img = if img.width() > 1920 || img.height() > 1920 { img.resize(1920, 1920, image::imageops::FilterType::Lanczos3) } else { img };
    let mut saida = std::io::Cursor::new(Vec::new());
    let (mime, ext) = if img.color().has_alpha() {
        img.write_to(&mut saida, image::ImageFormat::Png).map_err(|e| e.to_string())?;
        ("image/png", ".png")
    } else {
        let rgb = img.to_rgb8();
        image::codecs::jpeg::JpegEncoder::new_with_quality(&mut saida, 85).encode_image(&rgb).map_err(|e| e.to_string())?;
        ("image/jpeg", ".jpg")
    };
    Ok((mime, ext, base64::engine::general_purpose::STANDARD.encode(saida.into_inner())))
}

/// Os bytes de um anexo (pra mostrar na prévia).
pub fn bytes_do_anexo(b64: &str) -> Option<Vec<u8>> {
    base64::engine::general_purpose::STANDARD.decode(b64).ok()
}

#[cfg(test)]
mod testes {
    use super::*;

    const DOC: &str = "Olá, mundo do Noty.\n\n![gato](anexo:gato-1.png)\n\n<!-- ===== ANEXOS HYP ===== -->\n```hyp-anexo gato-1.png image/png\naGVs\nbG8=\n```\n";

    #[test]
    fn anexos_e_contagem() {
        let a = anexos(DOC);
        assert_eq!(a["gato-1.png"], ("image/png".to_string(), "aGVsbG8=".to_string()));
        let (p, _, _) = contar(DOC);
        assert_eq!(p, 5, "Olá mundo do Noty + a legenda gato");
        assert!(!sem_anexos(DOC).contains("hyp-anexo"));
    }

    #[test]
    fn txt_e_html() {
        assert_eq!(para_txt(DOC), "Olá, mundo do Noty.\n\n[gato]\n");
        let h = para_html(DOC, "t");
        assert!(h.contains("data:image/png;base64,aGVsbG8="));
        assert!(h.contains("<p>Olá, mundo do Noty.</p>"));
    }

    #[test]
    fn ids_novos() {
        assert_eq!(novo_id(DOC, "Gato!", ".png"), "gato-2.png");
        assert_eq!(novo_id("", "", ".jpg"), "imagem-1.jpg");
    }
}
