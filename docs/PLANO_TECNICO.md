# Plano de Desenvolvimento Técnico

Próximas grandes mecânicas do Soluna: segurança de rede, expedições privadas, party, IA de bando,
companheiros e paper doll evoluído. Cada fase diz o que muda em `Soluna.Shared`, `Soluna.Server` e
`Soluna.Client`, nos JSON do jogo e no tráfego UDP, e termina com critérios de aceite testáveis no
projeto `tests/Soluna.Tests`.

Duas regras valem para todas as fases:

- **Autoria pela interface.** Toda regra nova de jogo (tamanho de party, preço de mercenário, raio de
  aggro, tempo de vida de instância) fica nos dados do jogo e ganha campo no editor F1 ou F2. Código
  só guarda mecânica. Limites de segurança são do servidor, não do jogo, e ficam em `data/server.json`.
- **Servidor autoritativo.** O cliente pede; o servidor decide. Nada de anti-cheat no cliente.

---

## Ponto de partida (o que já existe)

| Área | Hoje | Onde |
| --- | --- | --- |
| Transporte | LiteNetLib 2.1.4, `ReliableOrdered` em tudo, chave de conexão fixa `soluna-dev`. Login manda usuário e senha **em texto claro**; o servidor guarda PBKDF2. | `Constants.cs`, `Net/Connection.cs`, `GameServer.HandleLogin` |
| Movimento | Orçamento de passos (token bucket): ganha tempo real, gasta `0,85 × WalkTimeMs` por passo, teto de 2 passos. Passo rápido demais, fora de sincronia ou bloqueado gera `PlayerPosition` de correção. | `GameServer.HandleMove` |
| Limites de ação | Ataque com intervalo `PlayerAttackMs`, ações de aventura a cada 400 ms, pulso por `SkillCooldownMs`, ofícios com recarga por atividade. Sem contagem de abusos nem expulsão. | `GameServer.Combat/Adventure` |
| Pacotes inválidos | `OnReceive` captura exceção e só registra no log. `GetBlob` já confere o tamanho declarado. | `GameServer.OnReceive`, `Packets.cs` |
| Masmorra | Uma semente global (`MapStore._expeditionSeed`). Andares com id `FirstMapId + profundidade - 1`, gravados em `data/maps/private`. **Todos os jogadores dividem a mesma expedição.** | `MapStore.Expedition`, `DungeonGenerator` |
| Estado de mapa | `MapState` por id de mapa, com a lista de `NpcInstance`. `TickWorld` percorre só mapas com jogadores. | `GameServer.Combat.cs` |
| IA | Alvo único (`Target`), perseguição gulosa por `Toward()` (sem pathfinding), comportamentos Amigável, Passivo e Agressivo. | `GameServer.Think` |
| Visual | Dois renderizadores: `CharacterSprites` (camadas por item com z e cor, legado chibi) e `EvolvingSprites` (silhuetas das formas, **sem camadas de equipamento**). | `Client/Graphics` |
| Equipamento | 9 slots (`EquipSlot`), sem escudo. Só jogadores têm equipamento. | `Items.cs` |

---

## Fase 0: fundação do protocolo (pré-requisito de tudo)

Tamanho: P. Sem isso, cada fase seguinte quebra clientes antigos sem aviso.

1. **Versão do protocolo.** `Constants.ProtocolVersion` (int). O cliente manda a versão no primeiro
   pacote; o servidor recusa versões diferentes com mensagem clara ("atualize o cliente"). Sobe a cada
   fase que mexer em pacote.
2. **Porta de entrada única no servidor.** Um `PacketGate` em `Server/Net/PacketGate.cs` que, antes do
   `switch` de `OnReceive`:
   - confere o tipo contra uma tabela `PacketType → (tamanho mínimo, tamanho máximo, estágio permitido)`;
   - recusa pacote com bytes sobrando depois da leitura (`reader.AvailableBytes > 0`);
   - conta pontos de suspeita por sessão em vez de só registrar no log (ver Fase 1.2).
3. **Configuração do servidor.** `data/server.json` (fora do repositório quando tiver segredo):
   porta, limites de taxa, limites de instância, caminho da chave de identidade.

Aceite: um cliente com versão diferente é recusado com mensagem; um pacote de tipo desconhecido ou
com bytes extras soma suspeita e não chega aos handlers.

---

## Fase 1: segurança e rede de baixa latência (prioridade máxima)

### 1.1 Canal cifrado sobre UDP

**Objetivo:** login e gameplay cifrados e autenticados, sem custo perceptível a 20 Hz.

**Primitivas (todas nativas do .NET 10, sem pacote extra):**

| Função | Escolha | Por quê |
| --- | --- | --- |
| Troca de chaves | `ECDiffieHellman` (curva P-256), chaves efêmeras por conexão | Sigilo futuro: vazar a chave do servidor depois não revela sessões antigas |
| Identidade do servidor | `ECDsa` P-256, chave de longo prazo do servidor | Impede homem no meio; DH sem identidade não protege nada |
| Derivação | `HKDF` SHA-256 | Gera chaves separadas para cada sentido |
| Cifra | `AesGcm` (padrão, acelerado por hardware); `ChaCha20Poly1305` quando `AesGcm.IsSupported` for falso | AEAD: cifra e autentica; pacote alterado é descartado |

Não usar cifra "modificada" nem ofuscação caseira: ofuscação não é segurança.

**Handshake (2 pacotes, antes do `Login`):**

```
Cliente                                         Servidor
  | ClientHello { versão, efêmera_C, nonce_C } ---> |
  |                                               |  efêmera_S, nonce_S
  | <--- ServerHello { efêmera_S, nonce_S,         |
  |        assinatura_ID(transcrição), cifra }     |
  | confere assinatura com a chave pública fixada  |
  | segredo = ECDH(efêmera_C, efêmera_S)           |
  | chaves = HKDF(segredo, sal = nonce_C||nonce_S, "soluna/1")
  |        -> chave_C→S, chave_S→C, prefixos de nonce
  | Login { usuário, senha } (já cifrado) -------> |
```

- **Chave pública do servidor:** gerada no primeiro start em `data/private/server-identity.key`
  (nunca distribuída). A parte pública vai para `games/<jogo>/server.pub` e chega ao cliente junto com
  o jogo. Para servidores de terceiros, o cliente fixa a chave no primeiro contato
  (`cache/<jogo>/known_servers.json`) e alerta se ela mudar.

**Formato do pacote cifrado:**

```
[1 byte: flag 0x01][8 bytes: contador][N bytes: cifrado(tipo + conteúdo)][16 bytes: tag]
nonce (12 bytes) = prefixo da direção (4 bytes) || contador (8 bytes)
```

- O tipo do pacote vai **dentro** do texto cifrado; quem observa não sabe se é movimento ou chat.
- **Anti-replay:** contador estritamente crescente por direção. Hoje tudo é `ReliableOrdered`, então
  basta exigir `contador > último`. Se algum dia movimento for para canal não confiável, trocar por
  janela deslizante de 64 bits.
- **Renovação de chave** a cada 2³² pacotes ou 1 hora (`Rekey` com novo par efêmero).
- **Falha de autenticação** soma suspeita alta; três falhas derrubam a conexão.

**Onde implementar:**

- `Shared/Net/SecureChannel.cs`: estado da sessão (chaves, contadores), `Seal(ReadOnlySpan<byte>)` e
  `Open(...)` sem alocação por pacote (buffers de `ArrayPool`).
- `Client/Net/Connection.cs`: `Send` sela, o evento de recebimento abre antes de ler o tipo.
- `Server/GameServer.cs`: `Session.Channel`; `OnReceive` abre antes do `PacketGate`.
  `Broadcast` monta o texto claro **uma vez** e sela por destinatário (cada um tem sua chave).
- Alternativa avaliada: `PacketLayerBase` do LiteNetLib (cifra o datagrama inteiro, inclusive acks).
  Fica para depois: o handshake dentro da camada é mais complexo e os cabeçalhos do LiteNetLib não
  carregam dado sensível.

**Custo a 20 Hz:** AES-GCM cifra na casa de GB/s; o acréscimo é de 25 bytes por pacote. Com 100
jogadores recebendo 20 atualizações de ~80 bytes por segundo, são ~160 KB/s cifrados: irrelevante para
a CPU. Medir com `Stopwatch` no tick (ver Observabilidade).

**Login:**

- A senha passa a trafegar cifrada; o servidor continua com PBKDF2.
- Limite de tentativas: 5 por minuto por IP e por conta, com espera crescente; registrar em
  `data/private/security.log`.
- A chave de conexão do LiteNetLib (`soluna-dev`) vira configuração, mas não é segurança: só filtra
  tráfego aleatório.

Aceite (testes):
- um byte alterado no pacote faz ele ser descartado e somar suspeita;
- um pacote reenviado (replay) é descartado;
- um servidor com identidade diferente da fixada é recusado pelo cliente;
- o tick continua abaixo de 50 ms com 50 sondas conectadas.

### 1.2 Anti-cheat autoritativo

**Speed hack.** O orçamento de passos já impede andar mais rápido de forma sustentada. Falta reagir:

- `Server/Security/Suspicion.cs` com pontuação por sessão que decai com o tempo. Eventos:
  passo rápido demais (+1), passo fora de sincronia (+1), pacote inválido (+3), falha de AEAD (+10),
  estouro de taxa (+2).
- Teto configurável: acima dele, expulsa e registra; repetição na mesma conta gera alerta para o admin
  (`/suspeitos` no chat de admin).
- Janela de conferência: além do token bucket, contar passos aceitos em 5 s e comparar com
  `5000 / WalkTimeMs × 1,1`. Pega cliente que acumula orçamento parado e depois "dispara".
- Usar sempre o relógio do servidor (`_clock`). Nunca aceitar carimbo de tempo do cliente.
- Interpolação é só visual no cliente; a posição válida é a do servidor (correção com `PlayerPosition`
  já existe).

**Taxa por tipo de ação.** `Server/Security/RateLimiter.cs`: token bucket por
`(sessão, categoria)`, com parâmetros em `data/server.json`:

| Categoria | Taxa sugerida | Hoje |
| --- | --- | --- |
| Movimento | já coberto pelo orçamento de passos | ok |
| Ataque e pulso | 1 por intervalo da arma ou da habilidade | ok, sem punição |
| Interagir e coletar | 3 por segundo, rajada de 3 | 400 ms fixo |
| Chat | 5 por 5 s | sem limite |
| Pedidos de mapa e de arquivo | 20 por segundo, rajada de 64 (a sincronização de arquivos pede muitos) | sem limite |
| Salvar conteúdo (admin) | 1 por segundo | sem limite |
| Pacotes no total | 120 por segundo | sem limite |

Estourar a taxa descarta a ação e soma suspeita; não responde com erro (não ajuda quem testa limites).

**Packet spoofing.** O LiteNetLib já amarra cada peer a endereço e id de conexão, e com AEAD um pacote
forjado não passa da autenticação. Complementos:

- todo id que o cliente manda (alvo, item, companheiro, NPC) é conferido contra o que pertence à sessão
  e ao mapa dela;
- enums fora do intervalo são descartados (vários handlers já fazem isso);
- nenhum valor numérico vindo do cliente define resultado: dano, ouro e drop são sempre do servidor.

Aceite: uma sonda andando a 2× é corrigida em todo passo e expulsa ao passar do teto; uma sonda que
manda 100 ataques por segundo tem só a taxa válida processada; um ataque a um id de NPC de outro mapa
é ignorado.

---

## Fase 2: sistema de party (pré-requisito das Fases 3 e 5)

Tamanho: M.

- **Shared:** `Party.cs` com `PartyInfo { Id, LiderId, Membros (jogadores e companheiros), Regra de saque }`.
- **Server:** `GameServer.Party.cs`; `_parties` em memória (party não sobrevive a reinício, de
  propósito). Convite com validade de 60 s; o líder sai e a liderança passa para o membro mais antigo.
- **Pacotes:**
  - Cliente → servidor: `PartyInvite { nome }`, `PartyRespond { aceita }`, `PartyLeave`, `PartyKick { id }`;
  - Servidor → cliente: `PartyInvited { de }`, `PartyState { estado completo }` (mandado a cada mudança,
    `ReliableOrdered`), `PartyVitals { id, hp, maxHp }` (a cada 500 ms, só para membros).
- **Experiência compartilhada:** ao derrotar um NPC, membros no mesmo mapa (e mesma instância) dentro
  do raio dividem a experiência: `cada = exp × (1 + bônus × (n - 1)) / n`.
- **adventure.json:**

  ```json
  "party": { "maxSize": 4, "shareRadius": 12, "bonusPerMember": 0.1, "companionsShareExp": true }
  ```

- **Editor:** F2 → Aventura → Geral ganha o bloco "Party".
- **Client:** painel de party (tecla P) e quadros com vida dos membros no HUD; chat de party (`/p`).

Aceite: três sondas formam party, a experiência de um abate se divide pela fórmula e quem está fora do
raio não recebe.

---

## Fase 3: instâncias de masmorra (expedições privadas)

Tamanho: G. Depende da Fase 2.

### Arquitetura

- `Server/Instances/InstanceManager.cs`, dono de todas as instâncias:

  ```
  Instance {
    Id, PartyId (ou jogador sozinho), Seed,
    Andares: Dictionary<profundidade, MapData>,
    Estados: Dictionary<idDoMapa, MapState>,
    CriadaEm, ÚltimaOcupaçãoEm, Ocupantes
  }
  ```

- **Ids de mapa em tempo de execução:** `InstanceBase + instance.Id × 1000 + profundidade`
  (`InstanceBase = 100_000_000`). Assim `Broadcast`, `PlayersOn`, `TickWorld` e o chat continuam
  funcionando por id de mapa, sem mudança.
- `MapStore` passa a ter mapas **transitórios**: `RegisterTransient(MapData)` e `Forget(id)`;
  transitório nunca é gravado em disco. Hoje cada andar vai para `data/maps/private`: isso deixa de
  acontecer.
- `MapData.Transient = true` faz o cliente **não** guardar o mapa no cache em disco (hoje o cache é
  por id e revisão, e ids de instância se repetiriam entre sessões).
- A semente vem de `RandomNumberGenerator.GetInt32` quando o líder entra no andar 1, e substitui a
  semente global de `MapStore`.

### Ciclo de vida

```
Criada --(alguém entra)--> Ativa --(último sai ou desloga)--> Ociosa
   ^                         ^                                 |
   |                         +--(membro volta antes do prazo)--+
   |                                                           |
   +-------------------- Descartada <----(prazo de ociosidade)--+
```

- **Entrar:** o líder usa o ponto de expedição; a instância é criada para a party (ou para o jogador
  sozinho). Membros que usam o mesmo ponto entram na mesma instância.
- **Deslogar dentro dela:** o personagem é salvo **na entrada da masmorra**, nunca num id transitório.
  Ao voltar antes do prazo de ociosidade, se a instância ainda existir, volta para dentro dela.
- **Descartar:** remove estados, NPCs e mapas transitórios e solta todas as referências. Em .NET não
  se chama o coletor de lixo à mão: basta não sobrar referência (timers, listas de jogadores,
  dicionários). O teste confirma com `WeakReference` + `GC.Collect()` só no ambiente de teste.
- **Limites** (`data/server.json`): instâncias simultâneas por servidor e criações por conta por minuto.
- **Recompensas:** a chave do baú já inclui a semente (`seed:mapa:x:y`), então cada instância tem
  seus próprios baús.

### adventure.json

```json
"dungeon": { ..., "instancing": "party", "idleSeconds": 120 }
```

`"instancing": "shared"` mantém o comportamento atual para jogos que quiserem masmorra coletiva.
Os dois campos vão para F2 → Aventura → Geral → Masmorra.

Aceite:
- duas parties entram ao mesmo tempo e veem layouts e inimigos diferentes;
- quem sai e volta em menos de 120 s retorna à mesma instância;
- depois do prazo, a contagem de instâncias volta a zero e o `WeakReference` do estado é coletado;
- nenhum arquivo é criado em `data/maps/private`.

---

## Fase 4: IA avançada

Tamanho: M.

### Pathfinding

- `Shared/Pathfinding.cs`: A* em grade de 4 direções, heurística Manhattan, teto de nós expandidos
  (512), usando `map.IsWalkable` e um callback de ocupação.
- Caminho guardado por NPC; recalculado quando o alvo se move mais de 2 tiles ou o próximo passo está
  bloqueado.
- **Orçamento por tick:** no máximo N buscas por tick (ex.: 30); quem não couber reaproveita o caminho
  antigo ou usa `Toward()` como hoje. Isso mantém o tick estável em 20 Hz com muitos NPCs.
- Estruturas reaproveitadas entre buscas (`PriorityQueue<,>`, arrays por mapa) para não alocar no tick.

### Aggro de bando

- **NpcDef ganha:** `Group` (ex.: "goblins"), `AssistRadius` (tiles) e `Leash` (distância máxima do
  ponto de nascimento antes de desistir e voltar curado).
- **Tabela de ameaça** no lugar do alvo único: `Dictionary<idDaUnidade, int>` somando dano recebido e
  cura em aliados; o alvo é a maior ameaça. É o que permite party e companheiros funcionarem (alguém
  segura a atenção).
- **Propagação:** quando um NPC ganha ameaça (viu um alvo ou levou dano), avisa os NPCs do mesmo
  `Group` dentro do `AssistRadius` que estejam sem alvo. A propagação é em largura, com conjunto de
  visitados e profundidade máxima configurável, para não virar avalanche no mapa inteiro.
- **Editor:** F2 → NPCs ganha "Grupo", "Raio de ajuda" e "Distância máxima do ninho".

Aceite: atacar um goblin puxa os goblins do grupo dentro do raio e não os de outro grupo; um NPC puxado
além do leash volta ao ninho com vida cheia; o tick fica abaixo de 50 ms com 200 NPCs perseguindo.

---

## Fase 5: companheiros de party (mercenários)

Tamanho: G. Depende das Fases 2 e 4.

- **Conceito:** o companheiro é uma unidade do servidor que pertence a um jogador e ocupa uma vaga na
  party. Ele nasce de um `NpcDef` marcado como contratável.
- **NpcDef ganha:** `Hireable`, `HirePrice` e `MaxLevel`.
- **Contratação:** novo tipo de ponto `Recruiter` (Recrutador), que entra no modo Pontos do F1, ou o
  próprio NPC contratável, ao interagir com E.
- **Estado no servidor:**

  ```
  CompanionInstance {
    Id, DonoId, Def, Nível, Exp, Hp,
    Equipment, Inventário,
    Postura: Seguir | Ajudar | Passivo
  }
  ```

  Persistido em `CharacterSave.Companions`, então o companheiro volta quando o dono loga.
- **Seguir o líder:** o servidor guarda uma trilha com as últimas posições do dono; o companheiro anda
  para o tile que o dono ocupava K passos atrás, o que dá o efeito de fila. Se a distância passar de um
  limite, usa A* (Fase 4). Se o dono trocar de mapa ou passar muito do limite, o companheiro é
  transferido junto.
- **Combate:** ataca o alvo atual do dono (o servidor guarda o último alvo atacado por cada jogador) ou
  quem estiver atacando o dono, com o ritmo de ataque do `NpcDef`. Entra na tabela de ameaça dos
  inimigos como qualquer unidade.
- **Experiência:** conta como membro da party quando `companionsShareExp` estiver ligado.
- **Derrota:** fica fora de combate e volta ao dono depois de um tempo configurável.
- **Rede:** reaproveita `NpcSpawned`, `NpcMoved` e `NpcRemoved` com um campo novo `OwnerId` (0 para NPC
  comum), mais `UnitKind.Companion`. O cliente desenha o companheiro como NPC, com o nome do dono abaixo.
- **adventure.json:**

  ```json
  "companions": { "maxPerPlayer": 2, "followDistance": 2, "teleportDistance": 12, "downSeconds": 30 }
  ```

Aceite: contratar consome o ouro definido no editor; o companheiro segue em fila, troca de mapa com o
dono, ataca o alvo do dono, recebe sua parte da experiência e reaparece após relogar.

---

## Fase 6: paper doll evoluído e inventário de companheiros

Tamanho: G. Depende da Fase 5.

### Renderização em camadas unificada

- **Um compositor só:** `Client/Graphics/LayeredSheetComposer.cs` recebe uma folha base (corpo ou forma
  evoluída) e uma lista ordenada de camadas, cada uma com tinta opcional, e produz uma `Texture2D`
  3×4. Substitui a parte de composição de `CharacterSprites` e passa a valer também para
  `EvolvingSprites`.
- **Cache:** a chave é o hash da lista de camadas; descarte por LRU com o `Trim()` que já existe,
  sempre fora do `Draw`.
- **Contrato de arte**, documentado em `SOLUNA_ART.md`: toda folha de base e de item usa a mesma grade
  3×4 e o mesmo ponto de apoio dos pés. Sem isso, o item não encaixa em silhuetas diferentes.

### Itens

- **ItemDef ganha:**
  - `Z` (ordem da camada);
  - `ZByDirection`, opcional: `[baixo, esquerda, direita, cima]`, para a arma passar para trás do corpo
    quando o personagem está de costas;
  - `Hides`, para o capacete esconder o cabelo, por exemplo;
  - `Layers`, opcional: folha por silhueta quando o mesmo item precisar de arte diferente por forma.
- **EquipSlot ganha `OffHand`** (escudo) **no fim** do enum (índice 9), para não quebrar personagens
  salvos; `Equipment.SlotCount` passa a 10 e a leitura completa arrays antigos de 9 posições.
- **Editor:** a aba Itens ganha ordem da camada e "Esconde", com a pré-visualização sobre cada forma.

### Inventário dos companheiros

- **Client:** `InventoryPanel` passa a receber um dono (jogador ou companheiro). Clicar no quadro do
  companheiro no HUD abre o inventário dele.
- **Pacotes:**
  - cliente → servidor: `CompanionEquip { companionId, itemId }` e `CompanionUnequip { companionId, slot }`;
  - servidor → cliente: `CompanionState { companionId, equipamento, atributos }` para o dono, e
    `NpcLook { index, equipamento }` para o mapa.
- **Servidor:** confere se o companheiro é da sessão e se o item está no inventário do dono ou do
  companheiro, move o item, recalcula ataque e defesa (`Formulas` + bônus dos itens), salva e transmite.
- **Atualização imediata:** o cliente recebe o `NpcLook`, recompõe a folha (chave nova no cache) e
  desenha no próximo quadro.

Aceite: equipar uma espada no companheiro muda o ataque mostrado no painel e o sprite dele para todos
no mapa no mesmo segundo; o item sai do inventário do dono; tentar equipar o companheiro de outro
jogador é ignorado.

---

## Transversal

- **Observabilidade:** medir a duração de `TickWorld` com `Stopwatch`; avisar no log quando passar de
  25 ms e expor `/status` para admin com tick médio e máximo, instâncias ativas, sessões e suspeitas.
- **Testes:** cada fase adiciona sondas UDP em `tests/Soluna.Tests` como as atuais (`Probe`).
  Teste de carga simples: 50 sondas andando e atacando por 60 s, com o tick monitorado.
- **Desempenho .NET 10:** nada de LINQ no caminho quente do tick (os `Where/Any` de `Think` e
  `NpcCanStand` viram laços com índice de ocupação por tile); buffers com `ArrayPool` e `Span` na
  cifra; `ServerGarbageCollection` e `ConcurrentGarbageCollection` no `.csproj` do servidor.
- **Versão do protocolo** sobe ao fim de cada fase; README e `docs/SOLUNA_ENGINE.md` atualizados junto.

---

## Ordem e dependências

```
Fase 0 (protocolo)
   └── Fase 1.1 (canal cifrado) ── Fase 1.2 (anti-cheat)
   └── Fase 2 (party)
          ├── Fase 3 (instâncias)
          └── Fase 4 (IA) ── Fase 5 (companheiros) ── Fase 6 (paper doll + inventário)
```

| Fase | Tamanho | Entrega principal |
| --- | --- | --- |
| 0 | P | versão de protocolo, porta de entrada de pacotes, `server.json` |
| 1.1 | M | handshake ECDH com identidade, AES-GCM por pacote, login protegido |
| 1.2 | M | suspeita, limites por categoria, expulsão |
| 2 | M | party, experiência compartilhada |
| 3 | G | expedições privadas por party com ciclo de vida |
| 4 | M | A*, tabela de ameaça, aggro de bando, leash |
| 5 | G | mercenários que seguem, lutam e persistem |
| 6 | G | compositor único de camadas, escudo, inventário de companheiros |

## Riscos

- **Arte do paper doll:** o código fica pronto antes da arte. Sem itens desenhados na grade comum,
  a Fase 6 não aparece na tela. Planejar a arte junto com a Fase 5.
- **Mudança de protocolo:** todo cliente antigo para de conectar a cada fase. A distribuição de arquivos
  já existe, mas o executável do cliente ainda não se atualiza sozinho.
- **Instâncias e saves:** salvar alguém num id transitório deixaria o personagem preso. O teste da
  Fase 3 cobre deslogar dentro da masmorra.
- **Cripto:** usar só as APIs do .NET e não inventar formato além do descrito acima; revisar o handshake
  com testes de adulteração antes de abrir o servidor na internet.
