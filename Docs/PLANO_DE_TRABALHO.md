# Plano de trabalho — Versão 0.1

[Índice do projeto](../README.md) · [Versão 0.1](VERSAO_0_1.md) · [Roadmap](ROADMAP.md) · [Decisões](DECISOES.md)

**Estado:** proposta do assistente, criada em 4 de novembro de 2026 a pedido do autor ("organize uma forma de iniciarmos esse projeto"). Detalha as etapas do [Roadmap](ROADMAP.md) em missões pequenas. Não altera o escopo confirmado da [Versão 0.1](VERSAO_0_1.md).

## Como trabalhamos

O autor tem experiência em programação, mas este é o primeiro jogo. A divisão de trabalho combinada:

| O assistente faz | O autor faz |
| --- | --- |
| Escreve os scripts C# na pasta do projeto e explica cada um. | Monta as cenas no editor da Unity seguindo um passo a passo. |
| Quebra cada missão em passos pequenos. | Dá Play, testa e relata o que está estranho ou sem graça. |
| Diagnostica erros a partir do Console ou de capturas de tela. | Escolhe e importa assets visuais. |
| Sugere valores, sistemas e soluções. | Toma as decisões de design. |

**Ciclo de cada missão:** objetivo → scripts → passo a passo no editor → teste em Play Mode → ajustes → commit no Git → próxima missão.

**Regra de ouro:** uma missão por vez. Nada de arte final antes de o jogo ser divertido com cubos e cápsulas.

## Missões da Versão 0.1

### Missão 0 — Preparar a base ✅

**Concluída em 4/11/2026.** Repositório Git local criado e enviado para o GitHub (`H-Salsa/ImNotAnAlien`).

**Objetivo:** proteger o projeto e conhecer o editor.

- Instalar o Git e criar o repositório na raiz `C:\WorkSpace\ImNotAnAlien`, com `.gitignore` para Unity (ignora `Library/`, `Temp/`, `Logs/`, `UserSettings/` e os arquivos `.csproj`/`.slnx` gerados).
- Opcional: enviar para um repositório privado no GitHub (cópia de segurança fora do computador).
- Conhecer as janelas principais: Scene, Game, Hierarchy, Inspector, Project e Console.

**Pronto quando:** o primeiro commit existe e o autor sabe selecionar um objeto e mudar valores no Inspector.

### Missão 1 — Andar e olhar ✅

**Concluída em 4/11/2026.** Cursor travado durante o jogo (Esc libera, clique trava). Câmera pode olhar para cima (`Min Pitch = -35`) e se aproxima do personagem ao encostar no chão ou em paredes. Sensibilidade aprovada pelo autor; velocidade de movimento terá ajuste fino depois. Console sem erros.

**Objetivo:** concluir o teste do movimento que já existe ([plano de movimento](superpowers/plans/2026-11-04-player-movement.md), passo 6 pendente).

- Testar WASD + mouse na cena `Prototype`.
- Ajuste proposto: travar e esconder o cursor do mouse durante o jogo (Esc libera).
- Ajustar velocidade, sensibilidade e distância da câmera pelo Inspector até ficar confortável.

**Pronto quando:** andar pela cena é agradável por um minuto inteiro sem incômodo.

### Missão 2 — Bloquear a esquina (blockout) ✅

**Concluída em 5/11/2026.** Base gerada pela ferramenta e salva na cena `Prototype`.

**Objetivo:** montar o espaço da 0.1 com formas simples.

- Ruas e calçadas com planos/cubos; casas e o possível prédio com cubos; árvores do parque com cilindro + esfera.
- Ferramenta de editor `ImNotAnAlien > Gerar Blockout da Esquina` (script `Assets/Scripts/Editor/BlockoutEsquinaGenerator.cs`) monta a base: mapa de 80 × 80 m, cruzamento de duas ruas de 10 m, quatro quarteirões (parque, prédio e dois com casas), postes e limites invisíveis.
- O autor ajusta e decora a base no editor (mover, duplicar, mudar cores).
- Opcional depois: pacote **ProBuilder** (gratuito, da Unity) para formas um pouco melhores que cubos.
- Cores simples por material para diferenciar rua, casa, parque.

**Pronto quando:** é possível reconhecer a esquina, as casas e o parque andando pela cena.

### Missão 3 — Os três alienígenas ✅

**Concluída em 5/11/2026.** Prefab `Assets/Prefabs/Alienigena`, três instâncias (parque, casas, prédio), NavMesh assado em `Esquina_Blockout`. Passeio, susto com "!", fuga e derrota de teste aprovados pelo autor. Ferramenta: `ImNotAnAlien > Criar Alienigenas`.

**Objetivo:** colocar alvos vivos na cena.

- Alienígenas provisórios: cápsulas coloridas com "olhos" (esferas).
- Comportamento simples com NavMesh (pacote já instalado): passear pelo bairro; fugir ao ver o jogador.
- Vida básica: podem ser derrotados.

**Pronto quando:** os três se movem pela esquina e reagem à presença do jogador.

### Missão 4 — Telecinese básica ✅

**Concluída em 5/11/2026; sensação aprovada pelo autor.** Ferramenta `ImNotAnAlien > Preparar Telecinese` adiciona o poder ao jogador e cria caixas, lixeiras e um carro. Controles definidos pelo autor (D09): mira no centro da tela; botão direito — toque levanta ou solta, segurar e soltar arremessa. Massa máxima 1500 kg; objetos pesados seguem a mira mais devagar. Alienígenas levam dano por impacto acima de 6 m/s e, se sobreviverem, levantam e fogem.

**Objetivo:** o coração da 0.1 — levantar e arremessar.

- Mira no centro da tela; segurar o botão para levantar o objeto/alienígena mirado; soltar ou clicar para arremessar na direção da câmera.
- Objetos arremessáveis com física (Rigidbody): caixas, lixeiras, bancos do parque, um "carro" de cubos.
- Alienígenas viram corpos físicos quando levantados e podem ser derrotados ao colidir com força.

**Pronto quando:** é possível levantar e arremessar objetos e alienígenas de forma controlável.

### Missão 5 — Deixar gostoso (game feel) ✅

**Concluída em 5/11/2026; efeitos aprovados pelo autor.** Ferramenta `ImNotAnAlien > Preparar Efeitos` cria o objeto `GameFeel` e adiciona `CameraShake` à câmera. Implementado: estilhaços em cubinhos da cor do objeto, tremida de câmera por trauma, pausa de impacto acima de 18 m/s, brilho no objeto segurado (pulsa ao carregar o arremesso), alienígenas se debatendo no ar e sons provisórios sintetizados por código (impacto, levantar, arremesso, grito alienígena). Os sons devem ser substituídos por sons de verdade antes de um lançamento.

**Objetivo:** fazer o poder parecer poderoso e engraçado.

- Tremida de câmera no impacto, partículas, brilho no objeto levantado, sons provisórios.
- Reações exageradas dos alienígenas (pulo de susto, giro no ar).

**Pronto quando:** arremessar um alienígena num prédio faz o autor rir ou querer repetir.

### Missão 6 — Avaliar a 0.1

**Pendente.** O autor preferiu definir a coleta de DNA e iniciar a 0.2 antes da avaliação formal. Fazer a avaliação junto com a da 0.2.

**Objetivo:** decidir o próximo passo com base no que foi jogado.

- Jogar por 10 minutos, gravar um vídeo curto, mostrar para 2 ou 3 pessoas.
- Registrar o que funcionou e o que não funcionou em [Decisões](DECISOES.md).

**Pronto quando:** existe uma lista do que entra na Versão 0.2.

## Versão 0.2 — Consumir e contar

Escopo definido pelo autor em 5/11/2026 ([D10 e D11](DECISOES.md)).

### Missão 7 — DNA e contador de alienígenas ✅

**Concluída em 5/11/2026; aprovada pelo autor.** Correções após o teste: alienígena segurado não leva dano de esbarrões (só ao ser arremessado/solto ou atingido por outro objeto); objeto segurado flutua sobre a linha da mira (sobe e desce com a câmera, sem afundar no chão) e ignora colisão com o personagem. Ferramenta `ImNotAnAlien > Preparar Versao 0.2`.

- Orbes de DNA ao morrer: salto, atração por proximidade e rede de segurança (`DnaOrb`, `DnaOrbSystem`).
- DNA acumulado do jogador (`DnaInventory`).
- Objetivo do setor: alienígenas consumidos / total, setor limpo com bônus (`SectorTracker`).
- Interface no canto superior direito com os dois contadores e o aviso "SETOR LIMPO!" (`GameHud`). O espaço do minimapa já está previsto (opção `Reserve Minimap Space`).
- Corpo do alienígena fica acinzentado ("drenado") depois de soltar o DNA.

**Pronto quando:** derrotar os três alienígenas, coletar o DNA e ver o setor limpo funciona sem confusão entre os dois números.

### Próximas candidatas (sem ordem aprovada)

- **Devorar:** consumir um alienígena enfraquecido ainda vivo, com animação exagerada, rendendo o dobro de DNA.
- **Minimapa** no canto superior direito.
- Gastar DNA em evoluções (ex.: aumentar o raio de atração dos orbes).

## Versão 0.3 — O ciclo completo

Rumo aprovado pelo autor em 5/11/2026: mais alienígenas, evoluções com DNA e risco. Ajustes do autor: [D12, D13 e D14](DECISOES.md).

### Missão 8 — Policiais, Devorar e vida do jogador ✅

**Concluída em 5/11/2026; testada pelo autor.** Ajustes pedidos no teste: Devorar com alcance de 20 m e sem aviso "chegue mais perto"; telecinese com alcance de 25 m; tiros a 38 m/s em rajada de 2, mira travada durante o aviso (dá para desviar se mover); marcadores dos últimos 3 alienígenas. Ferramenta `ImNotAnAlien > Preparar Versao 0.3`.

- Bairro com 8 civis e 3 policiais (prefab `Alienigena_Policial`, com quepe, distintivo e arma).
- Policiais: se aproximam até a distância de tiro (7 a 13 m), avisam (ponta da arma acende) e atiram projéteis visíveis; objetos no caminho bloqueiam. Tiros também ferem outros alienígenas.
- Dano por impacto proporcional ao peso (D14).
- Devorar com a tecla 1 e barra de vida sobre o alienígena mirado (D12).
- Vida do jogador: barra no canto superior esquerdo, flash vermelho, regeneração após 4 s, derrota e renascimento no início sem perder progresso.

### Missão 9 — Evoluções ✅

**Concluída em 5/11/2026; testada pelo autor.** O autor pretende voltar ao menu para ajustes finos (custos, efeitos, aparência). Ferramenta `ImNotAnAlien > Preparar Evolucoes` ([D16](DECISOES.md)).

| Evolução | Níveis e efeito | Custos (DNA) |
| --- | --- | --- |
| Ímã de DNA | atração 9 / 12 / 16 m | 20 / 40 / 70 |
| Força Telecinética | peso 2.500 / 4.000 / 6.000 kg; arremesso +15 / +30 / +50% | 30 / 60 / 100 |
| Alcance Mental | 30 / 35 m | 25 / 50 |
| Pele Alienígena | vida 130 / 160 / 200 | 30 / 60 / 90 |
| Devorar | desbloqueia (2x DNA) / 3x DNA | 50 / 120 |
| Onda de Impacto (tecla 2) | raio 8 m / 12 m com mais força e dano; recarga 6 s | 80 / 140 |

- Menu com Tab (pausa o jogo); HUD mostra as teclas das habilidades e "EVOLUÇÃO DISPONÍVEL!" quando dá para comprar algo.

### Ideia anotada — Estruturas destrutíveis (A07)

Definida pelo autor em 5/11/2026 ([D15](DECISOES.md)), ainda sem versão: estruturas desmoronam; os pedaços caem por 1–2 s, atravessam o chão e são apagados; fica um pequeno escombro no lugar. Casa pequena cai com alguns arremessos de carro, com uma arma futura ou com a Superforça; caixas e lixeiras não derrubam casas. Sem recompensa (só visual) e sem alienígenas dentro das casas. Exige atualizar o NavMesh quando uma estrutura cai.

### Estado em 5/11/2026 (fim do dia)

Missão 8 testada pelo autor: dano por peso, Devorar (alcance 20 m), policiais com rajada rápida desviável (mira travada no aviso), vida do jogador e marcadores dos últimos 3 alienígenas. Autor adicionou uma casa no lado leste e moveu o `Civil_05`. Lembretes: salvar a cena (Ctrl+S) e refazer o Bake do NavMesh ao mudar o cenário. **Próximo passo: Missão 9.**

**Versão 0.3 concluída em 5/11/2026:** ciclo completo (explorar, enfrentar policiais, derrotar/devorar, coletar DNA, evoluir, limpar o setor).

## Versão 0.4 — Destruição e cara de jogo

O autor escolheu começar pela destruição (caminho mais simples agora) em 5/11/2026; a arte (personagens e cenário estilizados) deve vir logo depois, por ser o que mais pesa para a Steam.

### Missão 10 — Estruturas destrutíveis ✅

**Concluída em 5/11/2026; aprovada pelo autor.** Ajuste após o teste: os blocos só afundam depois de pousar e parar (no máximo 6 s com física); a separação inicial entre blocos sobrepostos foi suavizada para não lançá-los para cima. Ferramenta `ImNotAnAlien > Preparar Destruicao` ([D15](DECISOES.md)).

- Casas dentro de `Esquina_Blockout` (nome começando com "Casa" e com parte "Paredes") ganham `DestructibleStructure`; o prédio fica de fora (Superforça).
- Só objetos de 300 kg ou mais causam dano: o carro (900 kg) derruba uma casa com cerca de 3 arremessos (2 com Força Telecinética). Vida da casa: 500.
- Cada batida: tremor, poeira, lascas e a casa fica mais encardida.
- Ao cair: blocos caem por 1,2–2,2 s, afundam no chão e são apagados; fica um monte de escombros; o NavMesh é atualizado em tempo de jogo.

### Missão 11 — Região 1 "Vilarejo do Bilu" em blockout

**Em andamento.** Ferramenta `ImNotAnAlien > Gerar Regiao 1 (Vilarejo do Bilu)` ([D17, D18](DECISOES.md)). Rodar com a cena Prototype aberta: cria a cena `Regiao1_VilarejoDoBilu` como cópia (a Prototype fica para testes).

- Mapa de 300 × 300 m (x −60..240, z −240..60); a esquina original é o Setor 1.
- Rio raso de 25 m (z −120..−95) com margens em rampa; dentro da água o jogador anda a 50% (`WaterZone`) e tudo que cai faz splash. Pontes em x = 0 e x = 165.
- Setor 1: +8 casas e árvores. Setor 2: 24 casas. Setor 3: parque com lago, 32 árvores, bancos. Setor 4: 4 galpões, 4 píeres e contêineres de 2.200 kg (exigem Força Telecinética).
- Casas novas já são destrutíveis. 9 carros, caixas e lixeiras.
- NavMesh calculado pela ferramenta para a região inteira; 54 alienígenas novos (total 65: 50 civis, 15 policiais).
- Próximo (Missão 12, proposta): contagem por setor, nome do setor ao entrar e recompensa por setor limpo.

### Missão 12 — População dinâmica e cotas por setor ✅

**Concluída em 6/11/2026; aprovada pelo autor ("ficou muito melhor").** Ferramenta `ImNotAnAlien > Preparar Populacao (Missao 12)`, rodada na cena da região ([D19](DECISOES.md)).

- Alienígenas surgem a 40–80 m do jogador, fora da visão (atrás de prédios ou fora da tela), e somem a mais de 110 m quando calmos e fora de vista. Cerca de 18 civis e 2 policiais de patrulha por perto.
- Cota por setor; surgem só em setores que ainda têm cota. Nome do setor aparece ao entrar; HUD mostra região e setor atual.
- Marcadores quando faltam 3 ou menos no setor atual.

### Missão 13 — Nível de procurado ✅

**Concluída em 6/11/2026; aprovada pelo autor.** Ajuste após o teste: a polícia usa o último ponto onde viu o jogador (procura em volta), precisa de linha de visão para atacar, e não surge policial novo enquanto procuram sem ver; com estrelas, policiais novos surgem a 55 m ou mais. Ferramenta `ImNotAnAlien > Preparar Procurado (Missao 13)` ([D19](DECISOES.md)).

- Calor por crime testemunhado (civil a até 25 m ou policial a até 40 m, com linha de visão): matar civil +10, matar policial +15, devorar +15, derrubar casa +20.
- Estrelas com 10 / 40 / 90 / 160 / 250 de calor; policiais extras por perto: +1 / +3 / +5 / +7 / +10. Os que surgem até ★★ contam para a cota.
- Com estrelas, policiais caçam o jogador e atiram mais rápido (até 40% no ★★★★★).
- Despistar: 10 s sem crimes e fora da vista da polícia → calor cai 6/s; estrelas piscam enquanto procuram. Morrer ou limpar a região zera.
- HUD: linha PROCURADO com 5 losangos abaixo do contador; sirene ao subir uma estrela.
- Ideia futura: reforços especiais no ★★★★★.

### Missão 14 — Trânsito ✅

**Concluída em 6/11/2026; aprovada pelo autor.** Ferramenta `ImNotAnAlien > Preparar Transito (Missao 14)`.

- Rede montada a partir das 4 ruas (cruzamentos e pontas automáticos); cerca de 6 carros por perto, surgindo e sumindo fora da vista.
- Conversíveis com motorista alienígena visível; mão direita, curvas nos cruzamentos, retorno no fim da rua; 9 m/s na reta, 4,5 m/s nas curvas.
- Freiam diante de jogador, alienígenas, carros e objetos; buzinam se o jogador ficar na frente; destravam sozinhos após alguns segundos.
- Telecinese ou Onda de Impacto num carro em movimento: vira carro comum e o motorista é ejetado (vira civil e conta para a cota, se houver vaga).
- Carros estacionados foram tirados das faixas.
- Ideia futura: carros de polícia com o nível de procurado.

**Carros explosivos** (pedido do autor em 6/11/2026; ferramenta `ImNotAnAlien > Preparar Carros Explosivos`): carros perdem vida com batidas fortes (vida 60; dano = (velocidade − 12) × 4, sem contar enquanto segurados); danificados soltam fumaça; sem vida pegam fogo e explodem em 2,5 s (ou na hora com outra pancada forte). Explosão de raio 8 m: empurra tudo, fere alienígenas (120) e o jogador (40), danifica casas (300) e outros carros (reação em cadeia), conta como crime e deixa uma carcaça queimada.

### Missão 15 — Arte dos alienígenas e do protagonista ✅

Pedido do autor em 6/11/2026: trocar as cápsulas por personagens estilizados (protagonista com animações, alienígenas civis e policiais).

**Feito em 6/11/2026 (aprovado pelo autor):**
- Protagonista: corpo base Quaternius (CC0) com as animações UAL (`ImNotAnAlien > Montar Protagonista (Missao 15)`, `PlayerAnimator`). Andar/correr, queda, nado, derrota; braço levantado aponta para o objeto da telecinese, tronco gira junto e o braço não atravessa o corpo; tranco no tronco ao levar tiro.
- Blender conectado ao Claude (conector oficial da Blender Lab). Alien gerado por `Art/Blender/alien_builder.py` (arquivo `Art/Blender/Alien_Base.blend`): cabeça em bulbo inclinada para trás e costurada no pescoço, olhos grandes laranja com pálpebra, nariz de fendas, boca, mãos de 3 dedos costuradas nos braços, corpo magro (decisão D20).
- Alien no jogo (`ImNotAnAlien > Montar Aliens 3D (Missao 15)`, `AlienAnimator`, `AlienRagdoll`): anda/corre/mira com pistola; segurado pela telecinese fica de pé, de frente para o jogador, balançando braços e pernas; arremessado ou morto vira boneco de pano com juntas naturais (cotovelo/joelho, cintura, pescoço) e "músculo" que afrouxa na morte. Policiais sempre verde-azulados.

**Feito em 7/11/2026 (aprovado pelo autor):**
- Alien: listras da cabeça num tom mais escuro da pele, 15 cores de pele para civis (verde-azulado só da polícia), versão feminina (`Alien_Feminino.fbx`), tamanho 10% maior e pequena variação entre eles (`AlienAppearance`).
- Roupas sorteadas: civis com camiseta, regata ou manga longa + calça ou bermuda + tênis, cada peça de uma cor; policiais com macacão, colete, cinto, coldre e botas, boné nos níveis baixos e capacete a partir de 3 estrelas.
- Expressões (blend shapes): apavorado com boca aberta gritando (fugindo, segurado, voando, morto) e bravo (policiais). Civil fugindo olha para trás, para o jogador, trocando de ombro suavemente.
- Fuga corrigida: o civil foge na velocidade certa e escolhe o caminho mais livre.
- Motoristas do trânsito com o modelo 3D, sentados no banco da frente, cabeça na bolha e pés nos pedais (`SeatedDriver`).
- Carros flutuantes 3D feitos no Blender por `Art/Blender/car_builder.py` a partir das plantas do autor: carro civil (`Carro_Civil.fbx`, 8 combinações de cores) e viatura (`Viatura.fbx`, ainda sem uso). Bolha de vidro comprida no teto, faróis redondos, 4 portas separadas com dobradiça (prontas para a animação de entrar e sair), interior com bancos, painel e volante (`ImNotAnAlien > Montar Carros 3D (Missao 15)`, `HoverCar`).
- Carro desligado: discos apagam e ele cai. Explosão: carro inteiro queimado, teto afundado e lataria amassada, vidros estourados em cacos, discos, retrovisores, um para-choque e algumas portas voando, as outras portas penduradas.

**Feito em 7/11/2026 (aprovado pelo autor), fim da missão:**
- Carros estacionados trocados pelo carro 3D (`ImNotAnAlien > Trocar Carros Estacionados (Missao 15)`, `CarPaint`): começam desligados, cores sorteadas, mesma destruição do trânsito.
- Protagonista definitivo "Fugitivo H-19" (decisão D24), gerado por `Art/Blender/protagonist_builder.py` e ajustado à mão pelo autor (ajustes guardados em `Protagonista_H19_ajustes.blend`): macacão marfim com faixa coral e H-19 nas costas, gola oval larga costurada no macacão, manga rasgada, algema travada com luz piscando e marcas do experimento no braço da telecinese (esquerdo), botas com formato de pé, barba por fazer. `ProtagonistLook`: marcas em 4 estágios com brilho na cor do poder e cabeça que cresce a cada poder novo até virar um bulbo de alien.

**Ficou para depois:** versão de cueca e chinelo para a cutscene inicial; distintivo da viatura acompanhar a porta quando houver a animação de entrar no carro.

## Versão 0.5 — O planeta (decisão D21)

Ordem aprovada pelo autor em 6/11/2026. Cada missão só começa depois que a anterior estiver testada e aprovada.

### Missão 16 — Carregar o mundo sob demanda ✅
Cortar o Vilarejo do Bilu em blocos (posições fixas, sem sorteio ainda). Carregar os blocos em volta do jogador e descarregar os de trás, montando o NavMesh de cada bloco ao carregar, com alienígenas, trânsito e telecinese funcionando na fronteira entre blocos. Recentralizar o mundo de tempos em tempos para não tremer longe da origem.

**Concluída em 8/11/2026; testada e aprovada pelo autor.** Ferramenta `ImNotAnAlien > Cortar Vilarejo em Blocos (Missao 16)` (rodar com a cena `Regiao1_VilarejoDoBilu` aberta; ela não muda).

- Escolhas do autor: blocos de 100 m (grade 3 × 3, canto sudoeste em x = −60, z = −240), origem flutuante já nesta missão e névoa leve.
- Cena nova `Regiao1_VilarejoDoBilu_Blocos` com 9 prefabs em `Assets/Prefabs/Blocos/Vilarejo` (partes `Estatico` e `Dinamico`). Ruas, chão e rio cortados nas bordas (12 peças); casas, árvores, galpões e pontes ficam inteiros no bloco do seu centro. Para mudar o cenário, editar o prefab do bloco.
- Objeto `Mundo`: `WorldStreamer` (carrega a 170 m e descarrega a 200 m da borda do bloco; névoa linear 70–165 m), `WorldNavMesh` (um NavMesh só para os blocos carregados, refeito em segundo plano; sem costuras entre blocos) e `WorldOrigin` (recentraliza a cada 1 km; posições lógicas × de mundo).
- Alienígenas e trânsito só surgem em blocos carregados e somem com o bloco; objetos com física arremessados para outro bloco carregado mudam de dono; o que cai no vazio é apagado.
- Teclas de teste: **F8** modo teste (raio 25/40 m, sem névoa, informações na tela) e **F9** recentraliza na hora.
- Começam carregados 8 dos 9 blocos (o canto sudeste fica a mais de 170 m do início).

### Missão 17 — Estado permanente por região e salvamento ✅
Cada região guarda semente, cota total, quantos alienígenas restam e se está limpa. Região limpa continua limpa ao voltar. Salvar e carregar o jogo (DNA, evoluções, regiões).

**Concluída em 8/11/2026; testada e aprovada pelo autor.** Escolhas do autor ([D25](DECISOES.md)): salvamento automático **e** tecla, **três espaços** de jogo e **menu inicial** simples. Ferramenta `ImNotAnAlien > Preparar Salvamento (Missao 17)`.

- Guarda: semente do mundo, DNA, nível de cada evolução, posição do jogador e, por região, semente, cota, consumidos e setores limpos. Não guarda nível de procurado, casas, carros nem alienígenas soltos (D23).
- Salva sozinho ao limpar setor ou região, a cada 3 min e ao fechar o jogo; **F5** salva na mão; Tab > "Salvar e voltar ao menu".
- Menu inicial (cena `MenuInicial`, primeira do Build): Continuar, Novo jogo (semente nova) e Apagar em cada espaço.
- Arquivos `jogo_1.json` a `jogo_3.json` em `Application.persistentDataPath/Saves`, com cópia de segurança `.bak`.
- Renascer continua no ponto inicial da região (as bases chegam na Missão 19).

### Missão 18 — Blocos sorteados pela semente ✅
Peças de bloco feitas à mão com "conexões" nas bordas (rua, calçada, rio, mato). O jogo encaixa as peças a partir da semente, sem erros de ligação. Além de quarteirões com ruas, entram parques, florestas e outras áreas abertas para quebrar a sequência de ruas. Novo jogo sorteia uma semente nova.

**Concluída em 8/11/2026; testada e aprovada pelo autor.** Ferramenta `ImNotAnAlien > Gerar Mundo Sorteado (Missao 18)` ([D26](DECISOES.md)).

- Padrão das bordas: cada lado do bloco tem mato, rua (10 m, no meio do lado, com calçadas de 3 m) ou rio (25 m, no meio do lado).
- Cena `Mundo_Sorteado` (a do jogo no menu): grade 9 × 9 (900 m, canto sudoeste em x = −350, z = −550), paredes em volta. O Vilarejo fica fixo no meio (blocos 3..5 × 3..5).
- Vilarejo refeito no padrão novo e melhorado (9 prefabs em `Assets/Prefabs/Blocos/Vilarejo_Padrao`): ruas em x = 0 e 200, z = 0 e −200; rio em z = −100 com pontes; calçadas em todas as ruas, casas variadas (sobrados, chaminés), parquinho, cais e galpões com contêineres. A esquina feita à mão foi mantida.
- Sorteio (`WorldLayout`): colapso de função de onda com pesos; mesma semente = mesmo mapa. Simulado em 200 sementes sem falha.
- Tipos: Bairro, Parque, Rio (com ponte quando há rua), Galpões e os 6 novos — Floresta, Fazenda (celeiro destrutível, silo, plantações, fardos de 300 kg), Praça (chafariz e lojas), Prédios, Lago (píer e barco) e Campo.
- Blocos sorteados são montados por código ao carregar (`BlockBuilder` + `Assets/Art/BlockKit.asset` com os materiais e o carro).
- Trânsito segue a rede sorteada (curvas, ruas sem saída, pontes).
- Setor provisório **Arredores** (cota 150) fora do Vilarejo; cota da região passa a 400 até as regiões da Missão 19.
- Correções no teste: o kit precisa ser recarregado pela ferramenta depois de trocar de cena; posição salva abaixo de −10 m é ignorada.

### Missão 19 — Planeta que dá a volta, quatro bases e mapa
Grade com coordenadas de latitude/longitude que dá a volta. Bases fixas no norte, sul, leste e oeste (pontos de referência, descanso e recursos a definir). Mapa do planeta mostrando regiões limpas e o progresso até o teletransporte.

O autor começou a Missão 19 pela **polícia** (8–9/11/2026) e depois fez o planeta, as bases e o globo (9–10/11/2026).

**Planeta, bases e globo ([D28](DECISOES.md)).** Feito em 9–10/11/2026; o autor testou o globo e a seta; o horizonte curvo foi testado e aprovado bem sutil (raio 100000). Ferramentas `ImNotAnAlien > Preparar Planeta (Missao 19)` (rodar de novo depois de `Gerar Mundo Sorteado`) e `ImNotAnAlien > Horizonte Curvo (Missao 19)` (com `Desfazer Horizonte Curvo` para voltar).

- Regiões orgânicas (10/11/2026): `Planet.BuildRegions` sorteia o formato das 16 regiões por jogo (centros perto do lugar de sempre, divisas entortadas com ruído, tamanhos variados, pedaços soltos juntados ao vizinho) e os 16 setores de cada região (Lloyd); a cota de cada setor acompanha o tamanho.
- `World/Planet/Planet.cs`: grade 128 × 128 (o Vilarejo nos blocos 59..61, sem sair do lugar: só a origem da grade mudou para x = −5950, z = −6150), 16 regiões × 16 setores, bases, latitude/longitude.
- Sorteio do planeta inteiro em `WorldLayout.GeneratePlanet` (conjuntos de bits, linha por linha, conserto local quando algo não encaixa): simulado em 60 sementes sem nenhum conserto.
- `WorldStreamer` no modo planeta: só olha os blocos em volta do jogador, carrega pelo número "sem volta" (dá a volta sem emenda), monta os polos (neve, nevasca, mastro "POLO NORTE/SUL") e faz a passagem pelo polo. O trânsito monta a rede de ruas aos poucos em volta do jogador.
- `SectorTracker` no modo planeta (256 setores; o HUD mostra a região onde você está). O jogo salvo (versão 2) guarda as 16 regiões, as bases visitadas, a última base e o marcador; a posição é salva dentro da primeira volta.
- Bases (`BaseSite` + blocos montados pelo `BlockBuilder`): esconderijo diferente em cada uma, placa "LAR DOCE LAR", cama, foto da Terra, fogueira, farol e o anel da zona segura. Entrar: vida cheia, procurado zerado, salva e vira o ponto de renascer; ninguém surge lá dentro.
- `PlanetGlobe` (tecla M): globo 3D com o mapa sorteado, regiões pelo progresso, seta do jogador, bases (douradas se visitadas), marcador por clique (botão direito tira), marcador na tela com a distância e viagem rápida dentro das bases.
- Horizonte curvo: `Assets/Shaders/CurvedLit.shader` (cópia do URP Lit 17.3) + `CurvedWorld.hlsl` + `CurvedHorizon` (raio visual 100000 m, bem sutil, escolhido pelo autor; peças compridas de cubo são picadas em pedaços de ~5 m ao carregar o bloco).
- Teste: com F8 ligado, F10 leva de base em base e F7 liga/desliga a curva.
- Pendente das bases (D22): máquina de teletransporte, laboratório genético e mural de troféus. Ideia anotada: ambientes por região (hoje os tipos de bloco ficam misturados por igual em todas as regiões) — Missão 20.

**Polícia — viatura e níveis de procurado ([D27](DECISOES.md)). Testado e aprovado pelo autor em 9/11/2026.** Ferramentas `ImNotAnAlien > Preparar Viaturas (Missao 19)` (rodar de novo depois de `Gerar Mundo Sorteado`) e `ImNotAnAlien > Preparar Golpes da Policia (Missao 19)`.

- Viatura (`PoliceCar`, `TrafficCar`, `TrafficSystem`): azul-marinho #1E2A4A com faixa branca e para-choques #3A3D44 (pintada no código, porque o FBX divide nomes de material com o carro civil), giroflex, motorista e carona fardados. Ao parar, as portas da frente abrem e os dois policiais descem; telecinese na viatura ejeta os dois.
- Desconfiança: sem estrela, ficar 4 s a menos de 6 m de um policial que te vê mostra "?" com barra sobre ele e sobe para o nível 1.
- Nível 1: o policial corre até você e bate (soco ou cassetete, sorteado por policial; 6 de dano, a surra não passa de 10% da vida). Com 30% de vida ou menos ele algema: apertar ESPAÇO várias vezes em 4,5 s para se soltar (o policial voa longe, 6 s sem poder ser algemado, resistir à prisão gera calor). Não soltou: **PRESO** — perde 25% do DNA, volta ao início e o procurado zera (`PoliceArrest`).
- Nível 2: tiro, como antes. Nível 3+: viaturas vêm direto até onde você foi visto (1, 2 e 3 nos níveis 3, 4 e 5). A partir de 1 estrela, viatura patrulhando a menos de 28 m liga a sirene, encosta e os policiais descem.
- Nível 4: capacete. Nível 5: farda verde oliva do exército e tiro com 1,6× de dano (armas e tropas novas ficam para depois).
- Progressão mais lenta: mais calor por estrela (10/70/160/280/450) e no máximo uma estrela a cada 25 s (a primeira sobe na hora).
- Golpes: camada "Golpes" no `Alien.controller` (tronco, braços e cabeça) com Punch_Cross, Punch_Jab e Sword_Attack da Quaternius; cassetete feito em código na mão direita; dano no impacto do golpe.
- Correção junto: blocos sorteados nascem já no lugar (carros estacionados eram arremessados); poste, carro e lixeira espaçados na calçada.

### Missão 20 — Relevo e biomas ([D29](DECISOES.md))
**Etapa 1 implementada em 11/11/2026 (aguardando teste do autor).**

- `PlanetTerrain`: altura do chão (Perlin com o jeito do bioma), cor do chão por bloco (textura 64 × 64 com mistura entre regiões, areia na beira da água, neve no alto), água do relevo (lâmina + gatilho de nado) e pesos do sorteio por bioma.
- `BlockBuilder`: no planeta, o chão de todo bloco vira malha com textura do bioma; floresta e campo ganham relevo; árvores, pedras e arbustos sobem no morro (somem se ficarem debaixo d'água); copas e arbustos com a cor do bioma; beira do rio e anel da esquina com a grama do bioma.
- `WorldLayout.GeneratePlanet`: o tipo de cada bloco é sorteado com peso do bioma (`PlanetTerrain.LayoutBias`).
- `WorldStreamer`: configura o relevo depois do sorteio e sobe o jogador se ele ficar debaixo do morro. `WorldNavMesh`: altura mínima de 70 m para pegar os morros.
- Etapa 2 (11/11/2026, aguardando teste): ondulação geral também na cidade — ruas, calçadas e faixas drapeadas no chão (`PlanetTerrain.SettleObjects`/`Drape`), casas e postes sobem até o chão, carros do trânsito acompanham a rua (`TrafficCar`), civis surgem na altura do chão (`PopulationSpawner`). Rios, lagos, parques, Vilarejo, bases e polos continuam nivelados (nos vales).
- Etapa 3 (11/11/2026, aguardando teste): Sci-Fi Essentials Kit da Quaternius ([D30](DECISOES.md)) em `Assets/Art/Props/SciFiEssentials` (mapas ORM convertidos para o URP em `TexturesURP`). Rodar `ImNotAnAlien > Preparar Sci-Fi Kit (Missao 20)` uma vez: materiais, modelos, Kit de Blocos, `Resources/WeaponKit.asset` e os inimigos em `Resources/Enemies` (Drone, QuadShell, Trilobite). O `SciFiEnemySpawner` nasce sozinho no Play.
- Etapa 4 (11/11/2026, aguardando teste): natureza com o Stylized Nature MegaKit + Textured Stylized Trees ([D31](DECISOES.md)) em `Assets/Art/Nature`. Rodar `ImNotAnAlien > Preparar Natureza (Missao 20)` uma vez (materiais com recorte e cor do bioma, modelos, `NatureKit.asset` ligado ao Kit de Blocos) e começar um jogo novo. Grama de triângulos ao vento (`GrassField` + `Assets/Shaders/GrassWind.shader`; ajustes de vento, empurrão e distância no material `NK_GrassBlades`).
- Etapa 5 (11/11/2026, aguardando teste): casas retrofuturistas no bairro ([D32](DECISOES.md)), com uma malha por casa, LOD, um material e interior falso. Falta: medir FPS num bairro cheio; trocar as casas dos prefabs do Vilarejo; lojas no mesmo estilo.
- **Anotado pelo autor em 11/11/2026: a física do pulo do protagonista está ruim** (rever altura, gravidade, controle no ar e aterrissagem em `PrototypeThirdPersonController`).
- Próximas etapas: enfeites próprios de cada bioma (cactos nas dunas, pinheiros nos picos, cristais na cratera, antenas no morro), relevo no globo (tecla M), clima por bioma (neve na tundra, névoa no pântano) e dificuldade subindo com a distância do início.

## Depois da 0.1 (sem ordem aprovada)

- Trocar cápsulas por personagens estilizados (pacotes de assets prontos e animações do Mixamo, com ajustes no Blender).
- Sistema de consumo e DNA, setores e recompensas — regras ainda em aberto.
- Polimento dos efeitos visuais (pedido do autor em 7/11/2026): fumaça mais densa e escura, fogo, faíscas e brasas na carcaça do carro, explosão mais bonita. Hoje são partículas simples (`GameFeel`).
- Página "em breve" na Steam assim que houver imagens e um vídeo curto que mostrem o jogo.

## Decisão pendente para o autor: multijogador

O multijogador com até cinco jogadores está confirmado para o projeto (D06), mas sem versão definida. Multijogador online muda como quase todos os sistemas são escritos (movimento, física, inimigos, poderes) e costuma multiplicar o trabalho e os bugs, ainda mais num primeiro jogo.

**Recomendação do assistente:** manter a 0.1 e o primeiro lançamento como single player e tratar o multijogador como recurso de uma atualização posterior, aceitando que parte do código será adaptada. A alternativa é planejar a rede desde já, o que torna cada missão acima mais lenta. A escolha é do autor.
