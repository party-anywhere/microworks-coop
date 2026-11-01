---
name: party-anywhere-game
description: Criar, validar e publicar um jogo ou mod co-op local para o Party Anywhere (até 10 jogadores com controles, celulares e teclados), inclusive transformar um jogo instalado no PC em co-op. Use ao começar um jogo co-op, ao ler jogadores/controles, ao preparar a capa ou ao publicar uma versão.
---

# Jogo co-op para o Party Anywhere

O Party Anywhere é uma central de controles para Windows: controles USB e Bluetooth, celulares (QR, sem
app) e teclados viram jogadores P1..P10. O jogo **não escreve código de controle**: lê as vagas pelo SDK.
Um jogo publicado direito entra na Biblioteca de qualquer pessoa pelo link do GitHub, e as versões novas
chegam sozinhas.

## Ferramentas (a CLI `pa`; `--json` para ler o resultado)

O servidor MCP `party-anywhere` (`pa mcp`) é opcional e tem as mesmas ferramentas, para agentes sem terminal.

| Tarefa | CLI | MCP |
|---|---|---|
| O Hub está instalado e atualizado? | `pa hub` | `hub_status` |
| Instalar ou atualizar o Hub | `pa hub install` (peça permissão: abre o instalador) | `install_hub` |
| Jogos instalados que podem virar co-op | `pa games [busca]` | `find_installed_games` |
| Começar um jogo novo | `pa new <pasta> --name "..."` | `create_game` |
| Começar o mod co-op de um jogo do PC | `pa new <pasta> --name "..." --base-game "<nome ou appid>"` | `create_game` com `base_game` |
| Online → co-op local e passar do limite de jogadores | `pa docs coop` | `get_docs` `coop` |
| Ler o contrato do SDK | `pa docs sdk` | `get_docs` `sdk` |
| Ver o formato do manifesto | `pa docs manifest` (e `schema`) | `get_docs` `manifest` |
| Conferir manifesto e capa | `pa validate` | `validate_game` |
| Gerar o pacote do release | `pa pack --from build` | `pack_release` |
| Conferir o jogo publicado (como o Hub lê) | `pa inspect <dono/repo>` | `inspect_published_game` |
| Levar um mod para a Biblioteca (para jogar ou testar) | `pa add <dono/repo ou pasta>` | `add_to_library` |

## O caminho de todo mod: uma cópia do jogo, N jogadores, N visões

Leia `pa docs coop` antes de propor o caminho técnico. Os jogadores extras são jogadores de verdade do jogo,
dentro da mesma cópia; o mod troca o contexto ("quem é o jogador" e "a câmera") e pede ao motor uma passada de
desenho por jogador, cada uma no seu pedaço da tela. **Nunca proponha abrir várias cópias do jogo** (uma por
jogador): custa N vezes a memória, o processador e o som, e o Party Anywhere não dá suporte. Jogo com
multiplayer online: **não reescreva o multiplayer, reaproveite** as entidades e regras que o jogo usa para
vários jogadores, na mesma cópia. Para passar do limite de jogadores, ache todos os lugares do limite antes de
mudar um.

## Padrões de todo mod (seção 3 do `pa docs coop`)

Tela dividida sem cantos vazios (os jogadores divididos por igual entre as linhas, cada linha com a largura
toda), HUD por jogador, cor e número da vaga no lugar do nome da conta, `controls` na ficha com o meio livre,
câmera pelo celular com `pa_take_look`, preset de vídeo leve só na memória com mais de um jogador na tela.
Teste com `pa phone-sim` (seção 9) antes de entregar.

## Tela do jogo no celular (o jogador liga)

Todo jogo e mod oferece; vem desligada e o jogador liga no menu do celular. Para cada vaga com
`pa_view_wanted(pa, s)`, não desenhe a parte daquele jogador na TV, desenhe a visão dele no tamanho de
`pa_view_size` (o formato da tela do celular) e entregue com `pa_submit_view` (BGRA; stride negativo para
imagem de baixo para cima). Sem o Hub ou sem celular, nada muda. Exemplos: `sdk/examples/arena_console.c` (C)
e `sdk/csharp/Unity/PhoneViews.cs` (Unity).

## Fluxo

1. `pa hub`. Sem o Hub, ou com versão velha: explique e ofereça `pa hub install` (só com o "sim" da pessoa).
2. Jogo novo: `pa new`. Mod de um jogo instalado: `pa games`, confirme com a pessoa qual é, e `pa new` com
   `--base-game`. Jogo com anti-cheat é recusado: explique e sugira outro.
3. `pa docs sdk` e implemente: `pa_connect`, `pa_poll` uma vez por frame, eventos de vaga, `pa_get_pad`.
   Controle do celular que muda com o jogo: `pa_set_mode` (entrou no carro: modo "Veículo") e
   `pa_set_label` (nome do botão).
   Em mods, chame `pa_poll` no ponto em que o jogo lê os controles e ajuste `install.files` para o nome
   da DLL que o jogo carrega (proxy como `version.dll`/`dinput8.dll` ou ASI loader).
4. Peça a capa de verdade (retrato 3:4, mínimo 600×800, até 2 MB, sem texto pequeno). `pa validate`.
5. Ajuste o passo "Build" de `.github/workflows/release.yml`; suba `version` e crie a tag igual (`v0.1.0`).
6. Peça para a pessoa adicionar o tópico `party-anywhere-game` ao repositório (aparece em "Descobrir").
7. `pa inspect` no repositório: se diz "pronto para instalar", qualquer um cola o link no Hub.

## Regras

- **Controles só pelo SDK** (`sdk/party_anywhere.h`). Nunca leia XInput, DirectInput ou teclado direto:
  o Hub já entrega tudo como controle de Xbox, inclusive celulares e teclados. Em mods, **desligue a leitura
  de controle e teclado do próprio jogo** para os jogadores: o Windows continua entregando o teclado ao
  jogo, e um teclado que é o P3 no Party Anywhere moveria o P1 do jogo.
- **Vaga em RECONNECTING**: pause ou proteja o personagem; o mesmo jogador volta para a mesma vaga.
- **`id` nunca muda** depois de publicado (`com.usuario.jogo`).
- **Nada de anti-cheat, jogos online de terceiros ou arquivos do jogo original** no pacote. O mod leva só
  os arquivos dele; o jogo-base a pessoa já tem.
- Em mods, preencha `requires.baseGame.acceptedSha256` com o hash do executável da versão suportada.
- Textos para o jogador em português do Brasil, curtos e sem jargão.
