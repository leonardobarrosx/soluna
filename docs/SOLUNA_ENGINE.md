# Engine e jogo de exemplo

A engine continua dividida em `Soluna.Shared` (formatos, protocolo e regras), `Soluna.Server` (simulação autoritativa e persistência) e `Soluna.Client` (MonoGame, renderização e editores). `games/soluna` contém o jogo de exemplo. Contas, materiais, hábitos e sorte são alterados pelo servidor; o cliente solicita ações.

## Conteúdo por jogo

| Arquivo | Responsabilidade |
| --- | --- |
| `data/adventure.json` | Atividades, recursos, receitas, dungeon e comportamento de renascimento |
| `data/evolution.json` | Combinações de hábitos, formas, cores e bônus |
| `data/items.json` | Equipamentos, ataque, defesa, preço e sorte |
| `data/npcs.json` | Criaturas, comportamento, combate, aparência e chefes |
| `data/maps/*.json` | Mapas, colisões, portais, NPCs, pontos de interação e objetos visuais |
| `assets/characters/*` | Sprites e conjuntos de aparência |
| `assets/props/*` | Objetos de cenário com ordenação de profundidade |
| `assets/props/catalog.json` | Recortes e dimensões das peças de arquitetura, vegetação e mobiliário do exemplo |

Tudo isso é editado dentro do jogo. F2 tem as abas Itens, NPCs, Aventura (materiais, ofícios, suprimentos, efeitos, receitas, loja, objetivos, memórias, regras gerais e textos) e Evolução (formas e os hábitos que as moldam). F1 coloca tiles, atributos, NPCs, peças de cenário com a colisão da base e os pontos de interação (santuário, mercador, bancada, quadro, ofícios, masmorra). O servidor valida cada gravação, escreve os arquivos abaixo e envia a mudança a todos. O conteúdo do exemplo pode servir de modelo para outro jogo:

```bash
dotnet run --project src/Soluna.Server -- --new-game meu_mundo --from soluna
dotnet run --project src/Soluna.Server -- --game meu_mundo
dotnet run --project src/Soluna.Client -- --game meu_mundo
```

`SOLUNA_ROOT` permite manter mundos e contas fora do checkout. `SOLUNA_GAME` seleciona o jogo padrão. Cada tarefa de nuvem já é isolada; use o checkout existente, sem criar worktrees, salvo pedido explícito.

## Vidas e evolução

`Journey` contém a vida atual: hábitos, forma, origem, ouro, materiais e provisões. `Legacy` pertence à conta e guarda memórias, totais e a afinidade natural com a sorte. A morte no exemplo Soluna recria `Journey`, reinicia nível, experiência, aparência, equipamentos e inventário, mantendo `Legacy`. Memórias dão uma pequena resistência inicial e influenciam a sorte.

Uma regra de evolução possui:

- `requires`: mínimos de ações efetivamente registradas pelo servidor;
- `weights`: importância relativa de cada hábito na pontuação normalizada;
- `kind`: `calling` ou `bloodline`;
- `tint`, `accent`, `silhouette`: apresentação visual;
- `attack`, `defense`, `health`, `mana`, `skillCost`, `skillCooldownMs`: efeitos mecânicos;
- `trace`: texto sutil enviado quando a forma muda.

A regra elegível com maior pontuação pode substituir a atual. A margem de 20% evita oscilações a cada ação. Pesca e luta juntas podem produzir um caminho diferente de cada atividade isolada. Nenhuma forma bloqueia profissões, equipamentos ou o pulso de essência. Golpes no vazio e pulsos sem alvo não treinam combate.

O exemplo inclui seis especializações e três origens. O jogador descobre seus sinais durante a vida; os limiares ficam nos dados de autoria, fora da interface do jogo.

## Ofícios e economia

Há cinco atividades: pesca, coleta de plantas, mineração, corte de madeira e extração de essência. Pontos de atividade são `Landmarks` no mapa; o servidor verifica distância e intervalo entre tentativas. Receitas exigem bancada e todos os materiais, debitados uma única vez. Equipamentos fabricados aparecem no inventário e podem ser equipados com I.

O mercador vende provisões e uma lâmina inicial. A venda de excedentes conserva dez unidades de cada material para fabricação. Relicários e criaturas concedem recursos e ouro. Provisões possuem limite de 99, materiais de 999.

A sorte soma afinidade natural da conta, ciclo dos espíritos, ambiente, memórias, itens, bênçãos, maldições e consumíveis. O total fica entre -30 e +40. Ela influencia quantidades obtidas e recompensas raras. Bênçãos, chá e maldições expiram por tempo; o sal de cinzas remove a maldição.

## Ruínas procedurais

`DungeonGenerator` cria salas conectadas por corredores, encontros, relicários, saída e descida. Semente e profundidade determinam o layout. Não existe um andar final definido no conteúdo; IDs de mapas e os limites numéricos do processo são os limites técnicos. Vida, ataque, defesa e experiência das criaturas crescem com a profundidade. A cada terceiro andar, o exemplo coloca um Guardião do Eclipse.

As criaturas de um andar não reaparecem durante a expedição. A descida abre depois que os inimigos daquele andar são derrotados. Uma expedição nova muda a semente quando não há jogadores nas ruínas. A semente e os mapas são persistidos; criaturas reaparecem ao reiniciar o processo do servidor. Jogadores simultâneos compartilham a expedição ativa: não há instâncias privadas por grupo nesta versão.

Cada andar usa uma grade de 60 × 48 tiles. `DungeonSettings` define `floorTile`, `floorVariants`, `wallTile`, `wallFaceTile` e os marcadores `exitTile`, `descentTile`, `chestTile`, `extractionTile`. Índices pertencem ao tileset configurado; -1 omite paredes ou marcadores opcionais. Nenhuma variação de piso ou marcador é escolhida pelo código do jogo-modelo.

`roomStyles` contém até 16 estilos, com `name`, `minDepth`, `floorTile`, `borderTile` e até 16 `decorations`. Cada decoração inclui um `prop` no formato `MapProp`, uma âncora (`NorthWest`, `NorthEast`, `SouthWest`, `SouthEast`, `Center`) e `offsetX`/`offsetY` em tiles. O tamanho da peça determina seu alinhamento ao canto; coordenadas X/Y do próprio prop são substituídas durante a geração. Pisos -1 conservam o piso base. Os estilos elegíveis são sorteados com um fluxo aleatório separado: trocar decoração não desloca corredores nem encontros.

Peças que não cabem na sala, encobrem pontos importantes ou sobrepõem outro móvel são omitidas. Uma colisão só é aceita se todo o chão restante continuar conectado ao nascimento. Tapetes podem passar sob atores, mas seus marcadores de interação continuam visíveis. Estilos, pisos e peças do catálogo podem ser editados em F2 → Aventura → Ruínas. Recortes e dimensões próprios também podem ser definidos no JSON. Alterações afetam andares ainda não gerados; os já persistidos mantêm sua composição até uma nova expedição.

O exemplo tem claustro, cripta, arquivo e uma sala de raízes disponível a partir do terceiro andar. Esses nomes, pisos e peças vivem em `games/soluna/data/adventure.json`.

## Interiores e portas

Os mapas 3–6 são estalagem, ferraria, botica e moinho. Usam os mesmos formatos e regras dos exteriores: `Warps` nas soleiras fazem a transferência no servidor, e a chegada fica em um tile livre, fora de outro gatilho. As lojas compartilham as ofertas do jogo; bancadas e altar usam os sistemas existentes. Camas, engrenagens e móveis são cenário nesta versão.

`tools/soluna_interiors.py` descreve sua composição, divisões, móveis e colisões. O editor F1 pode alterá-los como qualquer outro mapa. Nenhuma regra da engine depende de um desses IDs para reconhecer um interior.

## Arte

O jogo usa páginas de arte em `assets/atlases`, importadas por arquivos `*.png.layout.json`. Cada layout define `Columns`, `Rows`, `CellWidth`, `CellHeight` e `Patches`. Um patch referencia uma página por `File` (relativo a `assets/`), sua grade por `Columns`/`Rows` e os índices `Source`/`Target`. `Rects` permite especificar recortes `[x, y, largura, altura]` revisados; `Fit` e `Inset` preservam proporções e alinham os pés dentro dos quadros. `Average` reduz ruído dos pisos ao converter a imagem para resolução nativa. A imagem PNG que acompanha o layout serve de fallback; expandir o atlas conserva as células anteriores em suas posições.

A importação ocorre em memória e entrega aos renderizadores e editores as dimensões nativas: tiles de 32 pixels e folhas direcionais de três colunas por quatro linhas. O chefe do exemplo usa quadros de 48 pixels. O campo opcional `Terrain`, com `GrassTile`, `Grass`, `BlendInto` e `Water`, habilita as transições de terreno na camada de chão. Novas páginas devem ficar em `assets/atlases` para participar da invalidação dos caches ao atualizar assets.

`Terrain.Blends` permite uma lista de transições entre materiais: `Tile` indica a textura usada na borda, `From` os vizinhos que a produzem, `Into` os tiles que a recebem e `Width` a largura em pixels (1–12). A engine aplica as regras em ordem, mantendo o formato anterior quando a lista está vazia. O exemplo usa bordas de musgo, grama, areia e água rasa. As regras visuais não alteram colisões.

`MapProp.Source` é um recorte opcional `[x, y, largura, altura]` na imagem indicada por `Sprite`. `Width` e `Height` continuam em tiles. `Underfoot` coloca uma peça abaixo dos atores; os demais objetos seguem a ordenação pela base. Mapas antigos sem esses campos continuam usando a imagem inteira e a ordenação anterior. `Attributes` define as colisões separadamente.

O catálogo de cenário reúne 68 peças maiores. `tools/soluna_maps.py` e `tools/soluna_interiors.py` montam seis mapas fixos, revisão 12. F1 permite pintar tiles e colocar peças grandes no modo Cenário, com sua colisão `Solid`. Tapetes e decks com `Underfoot` são desenhados depois do chão e antes das camadas de detalhes, preservando marcadores de interação. O zoom inicial é 1,25; os controles de zoom permanecem disponíveis.

`characters/echoes/forms.json` associa cada silhueta à sua folha de animação. O compositor aplica as cores e marcas recebidas do servidor, usando os desenhos em código como fallback quando não há uma folha de autoria.

`tools/configure_soluna_art.py` escreve os metadados da arte atual, sem alterar os PNGs, e conserva as colisões existentes do catálogo. `tools/build_soluna_art.py` reproduz apenas os sprites de fallback com Pillow. `tools/build_soluna_world.py` sobrescreve somente os seis mapas públicos; aventura, evolução, itens, NPCs e dados dos jogadores são preservados. A opção antiga `--maps-only` produz o mesmo resultado. Veja [Arte de Soluna](../games/soluna/assets/SOLUNA_ART.md) para o inventário e os créditos.

O cliente aceita `--art-preview` para conferir tiles, formas e direções dos NPCs. `--scenery-preview` mostra o catálogo de peças grandes; Tab troca a página, e `--preview-page <n>` seleciona a página inicial (zero em diante). `--map-preview <id>` mostra um mapa local; `--dungeon-preview <profundidade> --seed <semente>` gera uma prévia das ruínas sem persistir o andar. `--overview` enquadra o mapa inteiro. WASD/setas respeitam colisões e Tab alterna o enquadramento. Não há interações, combate nem gravação nesse modo. Todas as prévias aceitam `--game` e `--screenshot`. A paleta F1 reorganiza as colunas do atlas para permitir selecionar todos os tiles.

## Validação

```bash
dotnet build Soluna.slnx
dotnet run --project tests/Soluna.Tests
```

O executor retorna código diferente de zero em caso de falha. Ele cria um mundo temporário, usa contas locais descartáveis, testa 80 layouts e estabelece conexões UDP reais. Cobre evolução, mudança de hábitos, ingredientes, profissões, permissões, sorte, persistência, combate, cooldown, passagem de andar e renascimento, além das referências e limites dos recortes da arte. Não escreve contas no jogo do checkout.

Para validar o cliente sem monitor no Linux, use Xvfb e Mesa:

```bash
LIBGL_ALWAYS_SOFTWARE=1 ALSOFT_DRIVERS=null xvfb-run -a \
  dotnet run --project src/Soluna.Client -- \
  --walk --name Smoke --screenshot /tmp/soluna-smoke.png
```

## Limites desta versão

Esta é uma base jogável de desenvolvimento. A validação cobre multiplayer local e os sistemas acima. Operação de MMO público, criptografia do protocolo de autenticação, testes de carga, moderação, instâncias por grupo, áudio, um editor de arte de personagem e de animações e um lançador no cliente continuam sendo trabalho de produto. O protocolo LiteNetLib atual transmite login sem criptografia e é destinado ao desenvolvimento em rede confiável. Cliente e servidor precisam usar a mesma versão do protocolo.
