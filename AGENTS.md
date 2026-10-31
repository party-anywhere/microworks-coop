# Instruções para agentes de IA

Este repositório é um **mod co-op local para o Party Anywhere** (ou um jogo feito para ele). O Party Anywhere é
um app para Windows que junta controles, celulares e teclados num lobby (até 10 jogadores) e abre o jogo para
todos jogarem juntos no mesmo PC. A ficha do mod é o `party-anywhere.json` na raiz.

Descubra primeiro o que a pessoa quer: **jogar** este mod ou **mexer** nele.

## A pessoa quer JOGAR este mod

O objetivo é terminar com o mod na Biblioteca do Party Anywhere, pronto para apertar Jogar.

1. **Tenha a ferramenta `pa`** (`pa --version`). Se não tiver, baixe o `pa-windows-x64.exe` do release mais
   novo de <https://github.com/joaocaioo/party-anywhere/releases/latest> e rode como `pa`. Toda saída tem
   `--json` quando você precisa ler o resultado. (Com o servidor MCP `party-anywhere` ligado, as ferramentas
   dele fazem o mesmo; os nomes estão entre parênteses.)
2. **Confira o app:** `pa hub` (MCP: `hub_status`). Não instalado ou com versão nova: explique e, **com o
   "sim" da pessoa**, rode `pa hub install` (MCP: `install_hub`). O instalador abre na tela; ela confirma.
3. **Leve o mod para a Biblioteca:** `pa add <link deste repositório>` (MCP: `add_to_library`). O app abre
   com a prévia do mod. Ele procura sozinho o jogo original (Steam, Epic ou a pasta que a pessoa escolher).
   A pessoa confirma em **Instalar o mod**.
4. **Se o app disser que falta o jogo original:** ele mostra os botões (instalar pela Steam ou pela Epic, ver
   na loja, ou "já tenho": escolher o `.exe` ou a pasta). Explique isso à pessoa.

Regras: **nunca** baixe, copie ou procure o jogo original fora da conta da própria pessoa. Este repositório só
tem o mod. Jogos com anti-cheat ficam de fora.

## A pessoa quer MEXER no mod (ou criar o dela a partir deste)

Use a skill `.claude/skills/party-anywhere-game`. A documentação vem na `pa`, na versão instalada:
`pa docs sdk` antes de mexer em controles (o mod lê os jogadores só pelo SDK; `sdk/party_anywhere.h` é o
contrato) e `pa validate` antes de publicar.

**Jogo com multiplayer online?** Leia `pa docs coop` antes de propor o caminho: o mod reaproveita o
multiplayer do jogo (nada de jogabilidade perdida) e, para passar do limite de jogadores, segue o método do guia.

**Seja honesto sobre o tamanho do trabalho.** Ler os controles pelo SDK é a parte pequena. Criar os jogadores
extras e dividir a tela num jogo fechado é engenharia reversa, feita em passos pequenos e testados com a
pessoa (a ordem está no guia). Não prometa "pronto em um pedido".

**Padrão de todo mod: a tela do jogo no celular.** Para cada jogador com `pa_view_wanted` verdadeiro, o mod
não desenha a parte dele na TV e entrega a visão dele com `pa_submit_view` (ver "Tela do jogo no celular" no
SDK). Implemente junto com a tela dividida, não depois.

Publicar é opcional: um release no GitHub (tag igual à `version` da ficha) deixa qualquer pessoa jogar só com
o link.
