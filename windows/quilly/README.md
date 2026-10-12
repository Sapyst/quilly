# Quilly Suite

A central desktop (Home) da suíte de escrita e produtividade criativa Quilly. Gerencia, instala silenciosamente em background, atualiza e inicia os aplicativos da suíte (**Noty** e **Artry**), além de integrar todos os seus documentos criados e recentes na aba **Files**.

---

## 1. Identidade Visual e Design

- **Logo e Marca:** Ícone squircle coral/terracota com a letra **Q** maiúscula caligráfica da fonte **Gabriola**.
- **Tipografia:** A marca no cabeçalho utiliza a fonte **Gabriola** na inicial e no texto com estilização refinada.
- **Tema Dark Pro:** Superfícies escuras modernas (`#14171A`, `#191C21`, `#20242B`), sem propagandas nem poluição visual, inspirado na elegância de ateliês de software profissional.

---

## 2. Estrutura de Diretórios

```
D:\DATA\Trabalhos\Projeto\Apps\Quilly\
├── Quilly.exe                 # Aplicativo Home Desktop da Suíte
├── Quilly-Installer.exe       # Instalador standalone completo da Quilly Suite
├── icon.png / icon.ico        # Ícone oficial Quilly (letra Q coral Gabriola)
├── build.ps1                  # Script de recompilação da Home e do Instalador
├── Suite\                     # Pasta padrão de instalação dos aplicativos da suíte
│   ├── Noty\Noty.exe
│   └── Artry\Artry.exe
├── source_apps\               # Repositório onde ficam os instaladores/executáveis fonte
│   ├── Noty\Noty.exe
│   └── Artry\Artry.exe
├── profile\                   # Histórico de arquivos e configurações do usuário
└── src\                       # Código-fonte (C# .NET 10 + WinForms + WebView2)
    ├── Quilly.csproj
    ├── Program.cs
    ├── MainForm.cs
    ├── AppManager.cs
    ├── FileManager.cs
    ├── installer\             # Projeto do Instalador Quilly
    └── ui\                    # Interface web (HTML5, CSS3, JavaScript)
```

---

## 3. Como Funciona a Instalação e Atualização dos Apps

### 3.1 Instalação Silenciosa
Ao abrir o **Quilly Suite**:
- O app verifica os pacotes em `source_apps\`.
- Se um aplicativo ainda não foi instalado, o botão exibe **"Instalar"**.
- Ao clicar em "Instalar", o aplicativo é instalado em segundo plano silenciosamente direto na pasta `Suite\<NomeDoApp>`, sem abrir janelas extras — o progresso é exibido no próprio card do aplicativo.

### 3.2 Detector Inteligente de Atualizações
- Quando você recompilar ou colar uma versão mais recente do `Noty.exe` ou `Artry.exe` dentro de `source_apps\`, o Quilly compara automaticamente a data de modificação e o tamanho dos arquivos.
- O card do app altera automaticamente o status para **"Atualização"** e o botão vira **"Atualizar"**.
- Clicar em "Atualizar" substitui os binários da pasta `Suite\` de forma limpa, **sem duplicar** nada!

---

## 4. Aba de Arquivos Recentes (Files)

- Mostra todos os documentos `.hyp`, `.artry`, `.md`, `.txt` e `.docx` criados ou abertos recentemente na suíte.
- Integrado automaticamente com o histórico do Noty e Artry.
- **Clique Duplo:** Abre o arquivo no aplicativo recomendado.
- **Clique com Botão Direito:** Menu de contexto com opções:
  - *Abrir no Noty*
  - *Abrir no Artry*
  - *Abrir com Padrão do Windows*
  - *Mostrar no Explorador de Arquivos*
  - *Remover da lista de recentes*

---

## 5. Compilação

Para recompilar a Home e o Instalador:

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

Requer o SDK do .NET 10.
