//! Onde o Noty guarda as coisas: ~/.local/share/arch-quilly/noty (o mesmo lugar e o mesmo formato
//! do Noty em Python, então a central Quilly continua lendo os recentes em `ui.recent`).
//!   settings.json  {spellLang, ui: {recent, lastFile, wrap, spellEnabled}}
//!   window.json    {W, H, Maximized}
//!   theme-gtk.css  o tema livre (CSS do GTK), aplicado por cima do vidro do sistema

use std::path::{Path, PathBuf};

use serde::{Deserialize, Serialize};

pub fn pasta() -> PathBuf {
    let base = std::env::var_os("XDG_DATA_HOME")
        .filter(|v| !v.is_empty())
        .map(PathBuf::from)
        .unwrap_or_else(|| home().join(".local/share"));
    base.join("arch-quilly/noty")
}

pub fn home() -> PathBuf {
    std::env::var_os("HOME").map(PathBuf::from).unwrap_or_else(|| PathBuf::from("/"))
}

#[derive(Clone, Debug, Default, Serialize, Deserialize)]
pub struct Ui {
    #[serde(default)]
    pub recent: Vec<String>,
    #[serde(default, rename = "lastFile")]
    pub last_file: Option<String>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub wrap: Option<bool>,
    #[serde(default, rename = "spellEnabled", skip_serializing_if = "Option::is_none")]
    pub spell_enabled: Option<bool>,
    /// o que o Noty antigo guardava e o novo não usa: preservado como veio
    #[serde(flatten)]
    pub resto: serde_json::Map<String, serde_json::Value>,
}

#[derive(Clone, Debug, Serialize, Deserialize)]
pub struct Settings {
    #[serde(default = "pt_br", rename = "spellLang")]
    pub spell_lang: String,
    #[serde(default)]
    pub ui: Ui,
    #[serde(flatten)]
    pub resto: serde_json::Map<String, serde_json::Value>,
}

fn pt_br() -> String {
    "pt-BR".into()
}

impl Default for Settings {
    fn default() -> Self {
        Self { spell_lang: pt_br(), ui: Ui::default(), resto: Default::default() }
    }
}

impl Settings {
    pub fn ler() -> Self {
        ler_json("settings.json").unwrap_or_default()
    }
    pub fn gravar(&self) {
        gravar_json("settings.json", self);
    }
    pub fn quebra(&self) -> bool {
        self.ui.wrap != Some(false)
    }
    pub fn corretor(&self) -> bool {
        self.ui.spell_enabled != Some(false)
    }
    /// O arquivo vai pro topo dos recentes (no máximo 5) e vira o "último aberto".
    pub fn lembrar(&mut self, caminho: &Path) {
        let c = caminho.to_string_lossy().to_string();
        self.ui.recent.retain(|p| p != &c);
        self.ui.recent.insert(0, c.clone());
        self.ui.recent.truncate(5);
        self.ui.last_file = Some(c);
        self.gravar();
    }
    pub fn esquecer(&mut self, caminho: &str) {
        self.ui.recent.retain(|p| p != caminho);
        if self.ui.last_file.as_deref() == Some(caminho) {
            self.ui.last_file = None;
        }
        self.gravar();
    }
}

#[derive(Clone, Copy, Debug, Default, Serialize, Deserialize)]
pub struct Tamanho {
    #[serde(rename = "W")]
    pub w: i32,
    #[serde(rename = "H")]
    pub h: i32,
    #[serde(rename = "Maximized", default)]
    pub maximizada: bool,
}

impl Tamanho {
    pub fn ler() -> Self {
        let t: Tamanho = ler_json("window.json").unwrap_or_default();
        if t.w > 300 && t.h > 200 { t } else { Tamanho { w: 980, h: 700, maximizada: t.maximizada } }
    }
    pub fn gravar(&self) {
        gravar_json("window.json", self);
    }
}

fn ler_json<T: for<'a> Deserialize<'a>>(nome: &str) -> Option<T> {
    let texto = std::fs::read_to_string(pasta().join(nome)).ok()?;
    serde_json::from_str(&texto).ok()
}

fn gravar_json<T: Serialize>(nome: &str, valor: &T) {
    let dir = pasta();
    let _ = std::fs::create_dir_all(&dir);
    let destino = dir.join(nome);
    let tmp = dir.join(format!("{nome}.tmp"));
    if let Ok(texto) = serde_json::to_string_pretty(valor)
        && std::fs::write(&tmp, texto).is_ok()
    {
        let _ = std::fs::rename(&tmp, &destino);
    }
}

/// Os tipos que o Noty abre.
pub const EXTENSOES: [&str; 7] = ["md", "markdown", "txt", "text", "hyp", "html", "htm"];
pub const LIMITE: u64 = 20 * 1024 * 1024;

pub fn abre(caminho: &Path) -> bool {
    match caminho.extension().and_then(|e| e.to_str()) {
        None => true,
        Some(e) => EXTENSOES.contains(&e.to_ascii_lowercase().as_str()),
    }
}

/// Lê um arquivo de texto do Noty (até 20 MB; UTF-8 com ou sem BOM; o que não for UTF-8 vira �).
pub fn ler_texto(caminho: &Path) -> Result<String, String> {
    if !abre(caminho) {
        return Err("o Noty abre .hyp, .md, .txt e .html".into());
    }
    let meta = std::fs::metadata(caminho).map_err(|e| e.to_string())?;
    if meta.len() > LIMITE {
        return Err("arquivo grande demais (mais de 20 MB)".into());
    }
    let bytes = std::fs::read(caminho).map_err(|e| e.to_string())?;
    let bytes = bytes.strip_prefix(b"\xEF\xBB\xBF").unwrap_or(&bytes);
    Ok(String::from_utf8_lossy(bytes).into_owned())
}

/// Grava sem perder o arquivo antigo se algo der errado no meio (temporário + rename).
pub fn gravar_texto(caminho: &Path, texto: &str) -> Result<(), String> {
    let nome = caminho.file_name().map(|n| n.to_string_lossy().to_string()).unwrap_or_default();
    let tmp = caminho.with_file_name(format!(".{nome}.noty-tmp"));
    std::fs::write(&tmp, texto).map_err(|e| e.to_string())?;
    std::fs::rename(&tmp, caminho).map_err(|e| {
        let _ = std::fs::remove_file(&tmp);
        e.to_string()
    })
}

/// O pt_BR vem embutido no pacote (não existe no repositório oficial do Arch): liga ele no
/// corretor (enchant/hunspell) copiando pra ~/.config/enchant/hunspell, se ainda não estiver lá.
pub fn garantir_dicionarios() {
    let destino = std::env::var_os("XDG_CONFIG_HOME")
        .filter(|v| !v.is_empty())
        .map(PathBuf::from)
        .unwrap_or_else(|| home().join(".config"))
        .join("enchant/hunspell");
    for origem in ["/usr/share/arch-quilly/noty/hunspell"] {
        let o = Path::new(origem);
        if !o.is_dir() {
            continue;
        }
        let _ = std::fs::create_dir_all(&destino);
        for nome in ["pt_BR.aff", "pt_BR.dic"] {
            let d = destino.join(nome);
            if !d.exists() {
                let _ = std::fs::copy(o.join(nome), d);
            }
        }
        break;
    }
}
