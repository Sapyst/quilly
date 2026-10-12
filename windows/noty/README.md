# Noty

Editor de texto simples, com estética de vidro (acrílico do Windows 11).
Só faz `.hyp`, `.md` e `.txt` — e é isso mesmo.

## Usar

- **Escrever**: abre e escreve. O contador (palavras · caracteres · linhas) fica embaixo.
- **Alt**: mostra/esconde a barra de menu (Arquivo, Editar, Exibir, Ortografia, Tema, Ajuda).
- **Botão direito**: menu de contexto. Muda conforme a seleção:
  - *sem seleção* → Abrir / Salvar / Salvar como;
  - *com seleção* → Negrito / Itálico / Código (se for .md/.hyp), Pesquisar na web,
    e as sugestões de ortografia do corretor.
- **Arrastar** um `.hyp`/`.md`/`.txt` pra janela abre o conteúdo.
- Abrir um arquivo por "Abrir com" do Explorer também funciona (`Noty.exe caminho`).

## Atalhos

| Tecla | Ação |
| --- | --- |
| Ctrl+N / Ctrl+O | Novo / Abrir |
| Ctrl+S / Ctrl+Shift+S | Salvar / Salvar como |
| Ctrl+F | Localizar |
| Ctrl+M | Pré-visualizar Markdown |
| Ctrl+B / Ctrl+I | Negrito / Itálico (.md/.hyp, com seleção) |
| Ctrl+T | Editar tema |
| Esc | Fecha painel/menu/busca |
| F12 | DevTools |

## Ortografia

Usa o corretor do Chromium (WebView2). O dicionário do idioma escolhido no menu
**Ortografia** é baixado automaticamente (padrão Google) na primeira vez com
internet. Trocar de idioma pede reiniciar o app. "Adicionar ao dicionário" do
menu de contexto guarda a palavra no perfil.

## Tema custom

Menu **Tema → Editar tema** (Ctrl+T): o CSS é livre, aplica ao vivo enquanto
digita e salva em `profile\theme.css` (Ctrl+S dentro do painel). "Restaurar
padrão" volta o tema original. O arquivo também pode ser editado por fora.

Os diálogos do app (sair sem salvar, avisos, confirmações) são do próprio
editor — vidro, e re-estilizáveis no tema pelos seletores `.dlg-fundo`,
`.dlg`, `.dlg-msg`, `.dlg-btn` e `.dlg-btn.primario`.

A barra de rolagem fica no canto direito da janela e segue o tema: cores em
`--rolagem` e `--rolagem-hover`, forma pelos seletores `::-webkit-scrollbar-*`.

## Formato .hyp

O Noty é a implementação de referência do padrão `.hyp` (nota em texto UTF-8
com Markdown, compartilhado entre os apps do Projeto). O spec vive em
`D:\DATA\Trabalhos\Projeto\Padroes\HYP.md` — inclui o checklist pra outros
apps adotarem o formato e o registro do Windows (ProgID neutro `Hyp.Document`).

## Compilar

```powershell
.\build.ps1
```

Precisa do SDK .NET 10. Sai um `Noty.exe` autocontido na raiz do app.
O perfil (configurações, tema, janela, dicionário) fica em `profile\`.

- `src\` — código (host C# WinForms + WebView2; UI em `src\ui\editor.html`)
- `icon.png` — ícone 256px sem fundo (fonte do `src\icon.ico`)
