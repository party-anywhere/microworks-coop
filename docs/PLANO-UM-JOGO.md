# Um MicroWorks só, todo mundo dentro dele

Decisão de 31/10/2026: o co-op local passa a rodar **num jogo só** (como o SACoop no GTA), com os jogadores
extras criados dentro da partida e a tela dividida na mesma janela. Uma cópia do jogo por jogador funcionou,
mas custa ~5 GB por pessoa (no PC de referência o teto foi 4; ver PLANO-TECNICO.md). Num jogo só a memória é
a de um jogo, e cada jogador a mais custa só o desenho da visão dele.

## Como o jogo funciona por dentro (o que muda e o que não muda)

- **Rede (Mirror):** a cópia que hospeda é servidor e cliente ao mesmo tempo. Cada jogador é um
  `PlayerController` na partida, dono de uma conexão. O jogo de quem hospeda já tem todos os personagens; o
  que separa "o meu" dos outros é `isLocalPlayer` (`ClientScene.localPlayer`) e `GlobalData.localPlayer`.
- **Jogador local:** no `Start`, se o personagem não é o local, o jogo **apaga a câmera, o controle de câmera
  e o HUD dele**. O movimento é calculado por quem controla o personagem e sincronizado (SmoothSync).
- **Comandos:** o servidor só aceita comando de quem é dono do personagem.

## A solução

1. **Um jogador extra = uma conexão de mentira no servidor** (`LocalSeatConnection`): o servidor cria o
   personagem pelo caminho normal (`AddPlayerForConnection`), ele entra no placar, nos microgames e na troca de
   fases como qualquer jogador. A conexão engole as mensagens de rede (o personagem já vive neste jogo).
2. **Troca de contexto:** antes de cada código de um personagem extra rodar (Start, Update, LateUpdate,
   FixedUpdate, OnEnable, de cada componente do personagem e do "pawn" que ele controla), o mod faz dele o
   jogador local (`ClientScene.localPlayer`, `GlobalData.localPlayer`, autoridade, SteamID de cópia) e
   aponta a entrada para o controle da vaga dele; depois devolve. Assim a câmera, o HUD, o movimento e as
   ações dele funcionam com o código do próprio jogo.
3. **Comandos e mensagens dirigidas:** o comando de um extra chega ao servidor pela conexão de quem hospeda; o
   mod diz ao servidor que é do dono certo. Mensagens para um jogador extra (`TargetRpc`) vão para dentro do
   próprio jogo.
4. **Tela dividida:** cada câmera de jogador desenha na parte dela da janela (`Camera.rect`, a grade do Hub);
   o HUD de cada um vai para a câmera dele; só o som de uma câmera.
5. **Entrada:** o controle de cada vaga (Hub) por jogador; o mouse e o teclado do PC só para menus de quem
   hospeda.
6. **Celular:** quem joga pela tela do celular sai da divisão; a câmera dele desenha numa imagem pequena que
   vai para o celular (`pa_submit_view`).

## Etapas (cada uma testada no jogo antes da próxima)

| Etapa | Pronto quando | Situação |
|---|---|---|
| 1. Segundo personagem | P2 nasce na partida, aparece no placar e anda/pula/mira com o controle dele. | Pronta |
| 2. Tela dividida | Duas câmeras lado a lado na mesma janela, cada uma seguindo o seu personagem. | Pronta: cada um vê o corpo dos outros |
| 3. HUD e ações | Mira, HUD, usar, atirar, empurrar, emotes de cada um. | HUD pronto (rodada, título, placar, "PASSOU/FALHOU" e posição de cada um). Armas, bola, sapo e kart com autoridade do extra (SeatAuthority) e desafios de responder com painel por jogador (SeatPads): falta testar no jogo |
| 4. Partida inteira | Troca de fase, microgames, entrar e sair no meio, quem cai e volta. | Troca de fase, 25 rodadas, fase de chefe, entrar no meio e cair/voltar: ok. Sair de vez (remover no Hub): falta testar |
| 5. Até 10 | Grade automática, desempenho medido, qualidade de vídeo ajustada. | Pronta (números abaixo) |
| 6. Celular | A visão de quem está no celular vai para o celular e sai da TV. | Pronta, inclusive para quem hospeda |

## Situação em 31/10/2026

Medido no PC de referência (Ryzen 7 5700U, vídeo integrado, 32 GB, janela 1920x1080), com o preset VeryLow
enquanto a tela está dividida (`QualidadeComVariosJogadores`; com um jogador só voltam as configurações da
pessoa):

| Jogadores na TV | Quadros por segundo |
|---|---|
| 4 | 34 |
| 6 | 27 |
| 10 | 19 |

Uma cópia do jogo por jogador parava em 4 (21 a 24 por janela, 6% de memória livre).

O que precisou de ajuste (cada um tem o porquê no código):

- **Teleporte sem fim:** mensagens para o personagem de um extra (ClientRpc, TargetRpc) rodam no contexto
  dele; antes, o jogo achava que o personagem era de outro PC e reenviava o teleporte para sempre.
- **Contexto preso:** o Harmony fecha os patches de um método na mesma ordem em que abriu; entrar no contexto
  de quem já está nele não faz nada, e assim ninguém fica preso como "jogador local".
- **Placar com "?":** o jogo pede à Steam os dados de cada conta antes de mostrar o avatar; um SteamID de
  jogador extra não é conta, então o mod responde na hora.
- **HUD da partida:** uma cópia por jogador extra, só com as telas do HUD (sem pausa, PDA nem a espera pelos
  jogadores, que começaria rodadas de novo), criada assim que a fase carrega.
- **Desconhecidos na sala:** sem o Party Pass a sala é pública (é a regra do jogo; sala privada é o recurso
  pago). O mod não bloqueia ninguém; quem hospeda pode expulsar pelo menu do jogo.

## Paridade com o online (01/11/2026)

Pela internet, cada pessoa tem no jogo dela a autoridade sobre o que é dela e um PDA próprio. Aqui faltavam os dois:

- **Autoridade (SeatAuthority):** a arma na mão (a bola da queimada, o caranguejo, a bandeira), o sapo, o kart e
  os projéteis de um extra pertencem à conexão de mentira dele, e o Mirror só dava autoridade, no jogo de quem
  hospeda, ao que é da conexão de quem hospeda: a arma do P2 não lia o controle dele nem mandava o arremesso.
  Agora o que é de um extra ganha autoridade, o aviso de autoridade (OnStartAuthority) roda no contexto dele, e o
  código desses objetos também (SeatContext acha a vaga pelo dono do objeto na rede).
- **PDA por jogador (SeatPads):** a conta (TypeTheAnswer e o "Ou não?" da rodada livre), digitar a palavra
  (TypeTheWord), digitar a cor (TypeTheColor), trivia (SelectTheAnswer), repetir a sequência (RepeatTheSequence) e
  empilhar (StackIt). Cada jogador do sofá (e quem hospeda, se joga pelo controle) responde num painel na parte
  dele, no direcional e no A/B; a resposta vai pelo mesmo evento do PDA (`MathAnswer`, `Answer`, `TriviaAnswer`,
  `SequenceAnswer`, `Stacked`), no contexto dele: conta para ele no placar. Digitar uma palavra num controle
  levaria o desafio inteiro: são quatro palavras para escolher, a certa e três da lista do jogo.
- **Quem é quem (SeatMarkers):** a etiqueta "P2 · nome" no canto de cada parte e "P3" sobre a cabeça de cada
  jogador do sofá, na cor da vaga, no HUD de quem vê.

Para conferir no jogo, com 2 ou mais jogadores: queimada (P2 pega e arremessa a bola), sapo e kart, cada desafio
de responder (P2 responde certo e ganha os pontos dele; errado, perde), e o placar no fim da partida.

## Riscos

- Código do jogo que olha "o jogador local" fora do código dos personagens e do que eles têm (alguns objetos de
  cena usáveis, projéteis só visuais): vale só para quem hospeda. Cada caso achado vira um ajuste.
- Desempenho: cada câmera é a cena desenhada de novo; no vídeo integrado, com 10 jogadores, ~19 quadros por
  segundo no preset mais leve.
- O que o jogo mostra fora do HUD da partida (avisos, votação, combo) segue um só, na parte de quem hospeda.
