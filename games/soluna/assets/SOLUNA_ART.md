# Arte de Soluna

O conteúdo visual novo foi produzido para Soluna com a ferramenta de imagens da OpenAI e com os geradores de fallback do projeto. Os assets permanecem incluídos no repositório; não exigem geração ou download externo para jogar.

## Conteúdo em uso

| Arquivo | Conteúdo |
| --- | --- |
| `atlases/terrain.png` | 16 superfícies: grama e vegetação, terra, pedras da vila, água e alvenaria das ruínas. |
| `atlases/objects.png` | 16 objetos: iluminação, relicário, santuário, plantas, quadro, minério, portal, escada, toco, cristais, bancada e detalhes de cenário. |
| `atlases/surfaces.png` | 16 superfícies adicionais: gramas discretas, terra, calçamento, água funda e rasa, lajes, solo cultivado, palha, madeira, folhas, cascalho e areia. |
| `atlases/architecture.png` | Pousada, ferraria, casa do herbalista e moinho, com fachadas próprias. |
| `atlases/nature.png` | 16 peças naturais: carvalho, pinheiro, salgueiro, árvore seca, rochas, minério, cristais, arco, sebe, juncos, samambaias, tronco, cogumelos, ervas, lenha e entulho. |
| `atlases/furnishings.png` | 16 peças de mobiliário: fonte lunar, banca, cais, quadro, cercas, muros, poste, bancos, floreira, poço, estoque, forja, ponte e acampamento. |
| `props/catalog.json` | Nomes, recortes e dimensões das 36 peças maiores das três páginas acima; cais e ponte são desenhados sob os personagens. |
| `atlases/creatures.png` | Espírito, criatura de raízes, fera de cinzas e Guardião do Eclipse, com poses direcionais. |
| `atlases/villagers.png` | Mercador de cinzas e guardião do santuário, com aparências distintas. |
| `atlases/blank.png` | Personagem inicial neutro, sem seleção de raça, classe, cabelo ou equipamento visual. |
| `atlases/forms.png` | Bases de capuz, túnica, lâmina e armadura para as formas emergentes. |
| `props/cottage.png`, `props/pine.png`, `props/ruin.png` | Casa, árvore e entrada das ruínas, ordenadas por profundidade no mundo. |
| `interface/title.png` | Arte de abertura. |

Essas imagens são originais geradas a partir de descrições e revisões para o projeto com base em pixelart original feita em aseprite.

## Importação e animação

Os PNGs das páginas são mantidos como foram gerados. Arquivos `*.png.layout.json` indicam os recortes de cada pose e seu destino no atlas nativo. O cliente faz a importação em memória, sem alterar os arquivos de origem. As poses usam amostragem por vizinho mais próximo; os pisos usam `Average` para reduzir ruído ao converter imagens maiores em tiles de 32 pixels. A renderização final continua em pixels, sem filtragem linear do atlas.

Os tiles continuam em **32 × 32**. Personagens usam folhas de três colunas e quatro linhas: passo, repouso, passo; direções sul, oeste, leste e norte. O personagem neutro e os NPCs comuns usam quadros de 32 pixels; o chefe usa 48. Os recortes alinham os pés e evitam que uma pose invada a seguinte. Poses da página que apontavam para a direção errada foram excluídas do mapeamento; alguns ciclos laterais dos NPCs reutilizam uma pose válida.

`characters/echoes/forms.json` associa silhuetas às folhas. `EvolvingSprites.cs` aplica as cores das regras do jogo e as marcas de origem às bases, preservando o início neutro. As bordas entre grama, musgo, caminhos, pedras, areia e água são calculadas pelo cliente a partir dos vizinhos e das regras `Terrain.Blends` do atlas; as colisões continuam definidas no mapa.

O atlas nativo tem 16 colunas e 11 linhas. Os índices anteriores permanecem: pisos originais em 0–15 e objetos originais em 96–111. Os pisos novos ficam em 128–143, natureza em 144–159 e mobiliário em 160–175. São **32 pisos e 48 objetos pequenos**; as células legadas intermediárias continuam disponíveis para compatibilidade. O catálogo maior reutiliza os mesmos recortes em escala apropriada, acrescentando as quatro construções. Não é preciso duplicar os PNGs para colocar cada objeto no mapa.

Os grandes objetos de `Props` usam `Source: [x, y, largura, altura]` para recortar a página e `Width`/`Height` para ocupar tiles no mundo. `Underfoot: true` desenha cais e ponte abaixo dos personagens. `Solid: [x, y, largura, altura]` diz quais tiles da base bloqueiam a passagem; o modo Cenário do editor de mapas (F1) marca esses tiles como bloqueados ao colocar a peça e os libera ao removê-la. Transparência de um desenho não define colisão. Os prédios novos são fachadas externas, sem interiores acessíveis nesta versão.

## Composição dos mapas

`tools/soluna_maps.py` descreve a revisão 10 dos mapas. A vila tem praça com fonte, mercado junto à oficina, moradias com caminhos de entrada, jardim cercado do herbalista e margem de pesca junto ao moinho. As trilhas do bosque formam um circuito entre clareiras reconhecíveis; árvores agrupadas delimitam os espaços e a alvenaria reaparece perto do pátio das ruínas.

Os objetos são posicionados deliberadamente. Recriar esses mapas produz o mesmo resultado. A geração de andares das ruínas continua procedural; esta composição é dos dois mapas externos. A documentação visual está nas [capturas atuais](../../../docs/screenshots/).

## Ferramentas de autoria

- `tools/configure_soluna_art.py`: lê dimensões e transparência das páginas e escreve os metadados de recorte revisados. Não altera pixels dos PNGs. As coordenadas precisam ser revisadas se uma página for substituída.
- `tools/build_soluna_art.py`: recria apenas o atlas e as folhas de fallback antigos; os metadados continuam fornecendo a arte ativa.
- `tools/build_soluna_world.py --maps-only`: recria os dois mapas públicos do exemplo com os detalhes de cenário. Sobrescreve esses mapas; não execute sobre alterações de autoria que queira preservar.
- `tools/soluna_maps.py`: planos de composição dos mapas e pegadas dos objetos; usa `props/catalog.json`.

O caminho normal de autoria é o editor dentro do jogo: tiles, peças grandes (modo Cenário) e pontos de interação (modo Pontos) no F1; uma peça nova entra arrastando o PNG para a janela e escolhendo "Cenário".

Para conferir todos os quadros pela mesma renderização usada no jogo, sem servidor:

```bash
dotnet run --project src/Soluna.Client -- --art-preview
dotnet run --project src/Soluna.Client -- --scenery-preview
dotnet run --project src/Soluna.Client -- --map-preview 1 --overview
```

Use Esc para sair. A prévia de mapas permite andar com WASD/setas e alternar a visão geral com Tab; não conecta, combate nem salva. `--screenshot <arquivo.png>` salva a prévia e encerra o cliente. Na nuvem sem monitor, execute com Xvfb conforme o README.

O código dos geradores segue a licença MIT do repositório. Os arquivos e créditos anteriores LPC e BoundWorlds/chibi permanecem nas respectivas pastas e conservam suas próprias licenças.
