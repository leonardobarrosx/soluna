# Arte de Soluna

O conteúdo visual novo foi produzido para Soluna com a ferramenta de imagens da OpenAI e com os geradores de fallback do projeto. Os assets permanecem incluídos no repositório; não exigem geração ou download externo para jogar.

## Conteúdo em uso

| Arquivo | Conteúdo |
| --- | --- |
| `atlases/terrain.png` | 16 superfícies: grama e vegetação, terra, pedras da vila, água e alvenaria das ruínas. |
| `atlases/objects.png` | 16 objetos: iluminação, relicário, santuário, plantas, quadro, minério, portal, escada, toco, cristais, bancada e detalhes de cenário. |
| `atlases/creatures.png` | Espírito, criatura de raízes, fera de cinzas e Guardião do Eclipse, com poses direcionais. |
| `atlases/villagers.png` | Mercador de cinzas e guardião do santuário, com aparências distintas. |
| `atlases/blank.png` | Personagem inicial neutro, sem seleção de raça, classe, cabelo ou equipamento visual. |
| `atlases/forms.png` | Bases de capuz, túnica, lâmina e armadura para as formas emergentes. |
| `props/cottage.png`, `props/pine.png`, `props/ruin.png` | Casa, árvore e entrada das ruínas, ordenadas por profundidade no mundo. |
| `interface/title.png` | Arte de abertura. |

Essas imagens são originais geradas a partir de descrições e revisões para o projeto. As referências de atmosfera não são fontes de recursos extraídos de Moonlighter, Crystalshire ou RPG Maker.

## Importação e animação

Os PNGs das páginas são mantidos como foram gerados. Arquivos `*.png.layout.json` indicam os recortes de cada pose e seu destino no atlas nativo. O cliente faz a importação em memória, com amostragem por vizinho mais próximo, sem alterar os arquivos de origem.

Os tiles continuam em **32 × 32**. Personagens usam folhas de três colunas e quatro linhas: passo, repouso, passo; direções sul, oeste, leste e norte. O personagem neutro e os NPCs comuns usam quadros de 32 pixels; o chefe usa 48. Os recortes alinham os pés e evitam que uma pose invada a seguinte. Poses da página que apontavam para a direção errada foram excluídas do mapeamento; alguns ciclos laterais dos NPCs reutilizam uma pose válida.

`characters/echoes/forms.json` associa silhuetas às folhas. `EvolvingSprites.cs` aplica as cores das regras do jogo e as marcas de origem às bases, preservando o início neutro. As bordas entre grama, caminhos, pedras e água são calculadas pelo cliente a partir dos vizinhos e da configuração `Terrain` do atlas; as colisões continuam definidas no mapa.

## Ferramentas de autoria

- `tools/configure_soluna_art.py`: lê dimensões e transparência das páginas e escreve os metadados de recorte revisados. Não altera pixels dos PNGs. As coordenadas precisam ser revisadas se uma página for substituída.
- `tools/build_soluna_art.py`: recria apenas o atlas e as folhas de fallback antigos; os metadados continuam fornecendo a arte ativa.
- `tools/build_soluna_world.py --maps-only`: recria os dois mapas públicos do exemplo com os detalhes de cenário. Sobrescreve esses mapas; não execute sobre alterações de autoria que queira preservar.

Para conferir todos os quadros pela mesma renderização usada no jogo, sem servidor:

```bash
dotnet run --project src/Soluna.Client -- --art-preview
```

Use Esc para sair. `--screenshot <arquivo.png>` salva a prévia e encerra o cliente. Na nuvem sem monitor, execute com Xvfb conforme o README.

O código dos geradores segue a licença MIT do repositório. Os arquivos e créditos anteriores LPC e BoundWorlds/chibi permanecem nas respectivas pastas e conservam suas próprias licenças.
