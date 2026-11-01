# MicroWorks Co-op Local

Até 10 pessoas no mesmo PC e no mesmo MicroWorks, cada uma na sua parte da tela ou no celular.

## Jogar

Precisa do **MicroWorks** (a sua cópia: Steam, Epic ou de onde for). O app acha sozinho ou deixa você escolher a pasta.

1. Instale o [Party Anywhere](https://github.com/joaocaioo/party-anywhere/releases/latest) (ele avisa quando há versão nova).
2. Na Biblioteca, em **Adicionar jogo → Por link**, cole o link deste repositório.
3. Conecte os controles ou escaneie o QR com o celular e aperte **Jogar**.

Com um agente de IA (Claude e outros), é só pedir *"instale este mod no Party Anywhere"* com o link deste repositório: o `AGENTS.md` diz a ele o que fazer (conferir o app, instalar se faltar e levar o mod para a sua Biblioteca).

## Como este mod funciona

- **Um jogo só.** Quem hospeda cria a partida e cada pessoa do lobby do Party Anywhere entra nela como um
  jogador de verdade: aparece no placar, joga os microgames e troca de fase como num jogo online.
- **Tela dividida automática, sem faixa preta:** os jogadores se dividem por igual entre as linhas e cada linha
  ocupa a largura toda (3 = 2+1, 5 = 3+2, 7 = 3+2+2, 10 = 4+3+3). Cada parte tem o HUD do seu jogador
  (rodada, placar, posição, "PASSOU/FALHOU"), e a tela se reorganiza quando alguém entra, sai ou passa a
  jogar pelo celular.
- **Celular:** com "Tela do jogo no celular" ligada, a parte daquela pessoa vai para o celular dela e sai da TV.
- **Quem é quem:** no placar, cada jogador do PC aparece com a cor da vaga e "P1".."P10".
- **Vídeo:** com mais de um jogador na tela, o mod usa o preset mais leve do jogo só durante a partida; as suas
  configurações não mudam.
- **Sala:** sem o Party Pass do MicroWorks, a partida é uma sala pública (regra do jogo). O mod não contorna
  isso.
- Precisa da sua cópia do MicroWorks na Steam. O mod usa o BepInEx (incluído) e não distribui arquivos do jogo.

## Desenvolver

- Precisa do MicroWorks instalado (o build usa as DLLs dele, que não vão para o repositório) e do .NET SDK.
- `powershell -File scripts\dev-install.ps1` compila e copia o mod para o jogo.
- `powershell -File scriptselease.ps1` compila, confere o manifesto e gera o pacote em `release\`; com
  `-Publicar`, cria o release `vX.Y.Z` (igual ao `version` do `party-anywhere.json`) no GitHub. O build não roda
  no GitHub Actions porque precisa do jogo.
