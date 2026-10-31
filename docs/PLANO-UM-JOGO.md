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

| Etapa | Pronto quando |
|---|---|
| 1. Segundo personagem | P2 nasce na partida, aparece no placar e anda/pula/mira com o controle dele. |
| 2. Tela dividida | Duas câmeras lado a lado na mesma janela, cada uma seguindo o seu personagem. |
| 3. HUD e ações | Mira, HUD, usar, atirar, empurrar, emotes de cada um. |
| 4. Partida inteira | Troca de fase, microgames, entrar e sair no meio, quem cai e volta. |
| 5. Até 10 | Grade automática, desempenho medido, qualidade de vídeo ajustada. |
| 6. Celular | A visão de quem está no celular vai para o celular e sai da TV. |

## Riscos

- Código do jogo que olha "o jogador local" fora do código dos personagens (HUD geral, alguns microgames):
  vale só para quem hospeda. Cada caso achado vira um ajuste.
- Microgames com câmera própria (sapo, kart): precisam da troca de contexto também no "pawn".
- Desempenho: cada câmera é a cena desenhada de novo; no vídeo integrado, a qualidade das câmeras extras cai.
