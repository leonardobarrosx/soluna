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

Os editores F1 e F2 existentes continuam disponíveis para mapas, itens e NPCs. As novas receitas, combinações de hábitos e pontos de atividade são configurados em JSON nesta versão. O conteúdo do exemplo pode servir de modelo para outro jogo:

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

## Arte

O jogo usa páginas de arte em `assets/atlases`, importadas por arquivos `*.png.layout.json`. Cada layout define `Columns`, `Rows`, `CellWidth`, `CellHeight` e `Patches`. Um patch referencia uma página por `File` (relativo a `assets/`), sua grade por `Columns`/`Rows` e os índices `Source`/`Target`. `Rects` permite especificar recortes `[x, y, largura, altura]` revisados; `Fit` e `Inset` preservam proporções e alinham os pés dentro dos quadros. A imagem PNG que acompanha o layout serve de fallback.

A importação ocorre em memória e entrega aos renderizadores e editores as dimensões nativas: tiles de 32 pixels e folhas direcionais de três colunas por quatro linhas. O chefe do exemplo usa quadros de 48 pixels. O campo opcional `Terrain`, com `GrassTile`, `Grass`, `BlendInto` e `Water`, habilita as transições de terreno na camada de chão. Novas páginas devem ficar em `assets/atlases` para participar da invalidação dos caches ao atualizar assets.

`characters/echoes/forms.json` associa cada silhueta à sua folha de animação. O compositor aplica as cores e marcas recebidas do servidor, usando os desenhos em código como fallback quando não há uma folha de autoria.

`tools/configure_soluna_art.py` escreve os metadados da arte atual, sem alterar os PNGs. `tools/build_soluna_art.py` reproduz apenas os sprites de fallback com Pillow. `tools/build_soluna_world.py` recria o conteúdo do exemplo; `--maps-only` restringe a recriação aos dois mapas públicos. Ambos os modos sobrescrevem os respectivos arquivos de autoria. Veja [Arte de Soluna](../games/soluna/assets/SOLUNA_ART.md) para o inventário e os créditos.

O cliente aceita `--art-preview` para conferir tiles, objetos, formas e todas as direções dos NPCs sem conectar ao servidor. A opção pode ser combinada com `--screenshot`. A paleta do editor F1 reorganiza as colunas do atlas para permitir selecionar também os tiles que excedem a largura do painel.

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

Esta é uma base jogável de desenvolvimento. A validação cobre multiplayer local e os sistemas acima. Operação de MMO público, criptografia do protocolo de autenticação, testes de carga, moderação, instâncias por grupo, áudio e um editor visual específico para as novas regras continuam sendo trabalho de produto. O protocolo LiteNetLib atual transmite login sem criptografia e é destinado ao desenvolvimento em rede confiável. Cliente e servidor precisam usar a mesma versão do protocolo.
