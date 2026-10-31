# MicroWorks Co-op Local: plano técnico

Objetivo: até 10 pessoas no mesmo PC jogando MicroWorks **exatamente como no multiplayer online**, mas com
todo mundo no mesmo PC: tela dividida na TV e, para quem joga pelo celular, a própria visão no celular (e
fora da TV), como pede o padrão do Party Anywhere.

**Regra do projeto: nenhuma jogabilidade a menos.** O multiplayer já é nativo; o mod não reescreve o jogo,
só troca "cada um no seu PC" por "todos neste PC".

## O que o jogo é por dentro (MicroWorks, Steam 1233410)

| | |
|---|---|
| Motor | Unity com Mono (não IL2CPP) e HDRP. Código em `MicroWorks_Data/Managed/Assembly-CSharp.dll`. |
| Rede | Mirror antigo (com `ClientScene`), transporte próprio `AgiriTransport` sobre Steam P2P (endereço = SteamID). O `TelepathyTransport` (TCP) também vem no jogo. |
| Jogadores | Até 16 por partida. `PlayerController` com câmera própria (`PlayerCamera`, terceira pessoa). |
| Entrada | Luminosity InputManager (`InputManager.GetAxis/GetButton/GetButtonDown/IsInputHeld`), ações `Horizontal`, `Vertical`, `Mouse X`, `Mouse Y`, `Jump`, `Crouch`, `Sprint`, `Fire`, `AltFire`, `Use`, `Emote`, `Mouse ScrollWheel`. Controle Xbox via XInputDotNet. |
| Anti-cheat | Nenhum: a Steam não lista o MicroWorks com Valve Anti-Cheat. Sala "VAC" só faz o servidor conferir o ticket Steam de quem entra e recusar contas com banimento VAC (`AgiriAuth`). |
| Salas e DLC | Sem o **Party Pass** (DLC 2355460), o jogo só cria sala pública, 16 vagas, "VAC", sem senha e sem mutadores; o servidor da Agiriko recusa o resto (`/api/lobby/register` → 402). O mod respeita: sem DLC, sala pública (gente da internet pode entrar junto); com DLC, "Criar partida" já abre privada e sem VAC. O mod nunca contorna essa cobrança. |
| Tutorial e conduta | Conta que nunca jogou vai para o tutorial e vê o aviso de conduta: a cópia 1 mostra os dois, como o jogo; as cópias extras pulam (sem gravar). |

## Caminho escolhido: uma cópia do jogo por jogador, todas neste PC

Cada jogador roda o seu próprio MicroWorks, e as cópias se conectam entre si pela rede local do próprio PC
(`127.0.0.1`), do mesmo jeito que se conectam pela internet. Como cada cópia é um cliente de verdade, **tudo
do jogo funciona igual**: microgames, HUD, placar, modos, cosméticos, câmeras especiais.

Descartado: **vários personagens dentro de uma cópia só.** Gasta menos, mas o jogo supõe um jogador local
em 99 arquivos (HUD, microgames, câmeras): cada um viraria um patch, e alguma jogabilidade se perderia.

O preço do caminho escolhido é desempenho: cada jogador é um jogo HDRP inteiro rodando. O mod deixa cada
cópia leve (resolução da própria janela, qualidade baixa nas cópias extras, só a cópia 1 com áudio) e o
limite real de cada PC é medido (fase 4).

### Peças

1. **Carregar o mod:** BepInEx 5 (Mono x64) + Harmony. O pacote do mod traz o BepInEx (LGPL) e a DLL do
   mod; a Biblioteca copia para a pasta do jogo e remover devolve tudo como estava.
2. **SDK em C#** (`PartyAnywhere.cs`): lê a memória `Local\PartyAnywhere.v1` por P/Invoke em `kernel32`
   (o mesmo layout `pa_shm` do `party_anywhere.h`), sem DLL nativa.
   - A cópia 1 (a que hospeda) **conecta** ao Hub (`pa_connect`, dona do `game_pid`).
   - As outras **só leem** a vaga delas (o seqlock aceita vários leitores) e escrevem a própria tela
     (`Local\PartyAnywhere.v1.view.<vaga>` é uma memória por vaga). Avisos e vibração das outras cópias
     passam pela cópia 1.
3. **Abrir as cópias:** a Biblioteca abre a cópia 1. O mod nela abre uma cópia por jogador do lobby
   (`MicroWorks.exe -pa-seat N`), e cada cópia sabe a vaga dela pela linha de comando. Jogador entrou no
   meio: abre mais uma; saiu: fecha a dele.
4. **Rede local** (`src/Net`): a cópia 1 hospeda pela rotina normal do jogo e passa a aceitar, além da Steam,
   TCP em `127.0.0.1` (`MultiplexTransport` com o `TelepathyTransport` do jogo, escutando só em loopback: nada
   de rede nem aviso do firewall). Ela cria um mutex `Local\MicroWorksCoop.HostReady.<porta>`; as cópias
   veem o mutex, trocam o transporte e entram sozinhas.
   - **Identidade:** placar e reconexão do jogo são por SteamID. Cada cópia extra se apresenta com o SteamID
     real e outro campo "instância" (P2 → 0x11...), que não existe como conta; o nome mostrado é o da vaga.
   - **Autenticação:** cópia local (conexão do Telepathy) com SteamID de cópia entra direto, como o próprio
     host; SteamID de cópia vindo da internet é recusado; quem vem pela internet passa por tudo do jogo.
5. **Entrada:** em cada cópia, o `InputManager` (Luminosity) responde com o `pa_pad` da vaga dela.
   Teclado, mouse e controles do Windows deixam de mexer nas cópias (o Hub entrega tudo pelo SDK).
   Mira: stick direito vira `Mouse X/Y`.
6. **Tela dividida:** cada cópia é uma janela sem borda posicionada no seu pedaço da TV (1, 2, 2x2, 3x2,
   3x3 + 1...), só com quem **não** está vendo pelo celular.
7. **Tela no celular:** a cópia de quem tem `pa_view_wanted` sai da TV (janela fora da tela, renderizando
   pequena), lê a própria imagem com `AsyncGPUReadback` e entrega com `pa_submit_view`.
8. **Partida de outra pessoa:** quando a cópia 1 entra na sala de alguém pela internet, o mod fica de fora
   (as cópias não entram ali) e o jogo segue normal.

## Fases (cada uma termina com algo testável no jogo)

| Fase | Pronto quando |
|---|---|
| 1. Entrar no jogo ✅ | BepInEx carrega o mod, o mod conecta ao Hub, mostra "MicroWorks Co-op conectado" no celular e loga entradas/saídas de jogadores. Duas cópias do jogo abrem juntas. |
| 2. Entrada pelo SDK ✅ | Uma cópia joga inteira com o celular ou o controle da vaga dela; nos menus, um cursor da cor do jogador (A clica). |
| 3. Duas cópias na mesma partida ✅ | A cópia 2 entra sozinha na partida da cópia 1 (Telepathy), cada uma com a sua vaga. Falta: lado a lado na TV (fase 4). |
| 4. N jogadores | Até 10, abrindo e fechando cópias com o lobby, a divisão da TV automática e o FPS medido. |
| 5. Celular | A visão de cada jogador no celular, e quem está no celular sai da TV. |
| 6. Acabamento | Áudio só da cópia 1, menus pulados nas cópias extras, partida inteira de teste (todos os modos). |
| 7. Publicar | Capa, controle do celular na ficha (`controls`), release com o pacote. |

## Desempenho medido (31/10/2026)

PC de referência: Ryzen 7 5700U com vídeo integrado, 32 GB, tela 1920x1080. Cada cópia no preset VeryLow
(QualidadeNasCopias) e com o mesmo teto de 60 fps; a cópia aberta por último fica na frente.

| Jogadores | RAM das cópias | RAM livre | fps de cada cópia |
|---|---|---|---|
| 1 | 5,1 GB | 51% | 59 (1920x1080) |
| 2 | 10,3 GB | 35% | 36 e 59 (960x1080) |
| 3 | 15,3 GB | 19% | 21, 23 e 52 (960x540) |
| 4 | 20,4 GB | 6% | 21, 24, 24 e 59 (960x540) |

- Cada cópia custa ~5 GB (no vídeo integrado, a "memória de vídeo" sai da RAM). Nesse PC o teto é 4.
- Duas descobertas que mudaram o resultado: o jogo segura janelas sem foco em poucos fps
  (`gfx_iMaxFPSInactive`; as cópias agora têm o mesmo teto) e as configurações "Alto" da pessoa valiam para
  todas as cópias (agora o preset leve, só na sessão).
- Para 5 a 10 jogadores, cópias não cabem num PC comum (10 × ~5 GB). Ver "Caminho para 5 a 10" abaixo.

## Caminho para 5 a 10 jogadores

Uma cópia por jogador é o caminho que não perde nada do jogo, e é o que fica para até 4 (ou o que o PC
aguentar). Acima disso, o caminho A do guia (um jogo, vários jogadores dentro dele) é o único que cabe: a
memória fica a de uma cópia só, e o custo de cada jogador a mais vira só o desenho da visão dele. É engenharia
reversa maior (o jogo supõe um jogador local em 99 arquivos), a fazer por partes e medida da mesma forma.

## Riscos

- **Desempenho:** N jogos HDRP ao mesmo tempo. No PC de referência (Ryzen 7 5700U, vídeo integrado, 32 GB)
  o limite pode ficar em 2 a 4; com placa de vídeo dedicada, mais. Medir na fase 4.
- **Pendências conhecidas:** avatar "?" das cópias no placar; o `GracefullyShutdownServer` do jogo só dá o
  último envio da Steam quando o transporte é o `AgiriTransport` (com o Multiplex, não); impedir que uma cópia
  assuma como host quando a cópia 1 sai (`HostTakeoverHelper`); áudio só da cópia 1.
- **Atualizações do jogo** podem quebrar os patches. A ficha pode fixar versões aceitas (`acceptedSha256`).
- **Conta Steam:** o jogo não tem anti-cheat e o mod não mexe no que os outros jogadores recebem. Ainda
  assim, é um mod: quem joga assume o risco que vale para qualquer mod de jogo com partes online.
