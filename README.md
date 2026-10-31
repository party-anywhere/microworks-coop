# MicroWorks Co-op Local

Uma frase curta que vende o jogo.

## Jogar

Precisa do **MicroWorks** (a sua cópia: Steam, Epic ou de onde for). O app acha sozinho ou deixa você escolher a pasta.

1. Instale o [Party Anywhere](https://github.com/joaocaioo/party-anywhere/releases/latest) (ele avisa quando há versão nova).
2. Na Biblioteca, em **Adicionar jogo → Por link**, cole o link deste repositório.
3. Conecte os controles ou escaneie o QR com o celular e aperte **Jogar**.

Com um agente de IA (Claude e outros), é só pedir *"instale este mod no Party Anywhere"* com o link deste repositório: o `AGENTS.md` diz a ele o que fazer (conferir o app, instalar se faltar e levar o mod para a sua Biblioteca).

## Como este mod funciona

Mod co-op local para **MicroWorks**. Quem instala precisa ter o jogo na Steam; o Hub acha a pasta sozinho, copia os arquivos do mod com backup e abre o jogo.

- O mod é uma DLL carregada pelo jogo (por um proxy como `version.dll`/`dinput8.dll` ou um ASI loader). Ajuste `install.files` no `party-anywhere.json` para o nome que o jogo carrega.
- Os jogadores chegam pelo `sdk/party_anywhere.h`: chame `pa_poll` no mesmo ponto em que o jogo lê os controles e use `pa_get_pad` para cada vaga.
- Em `requires.baseGame.acceptedSha256`, coloque o hash do `MicroWorks.exe` da versão que o mod suporta.
- Só jogos sem anti-cheat, e nada de distribuir arquivos do jogo original.

## Desenvolver

- `pa validate` confere o manifesto e a capa.
- Crie a tag `vX.Y.Z` (igual ao `version` do `party-anywhere.json`) para publicar uma versão.
