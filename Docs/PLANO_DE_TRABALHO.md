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

### Missão 14 — Trânsito

**Em andamento.** Ferramenta `ImNotAnAlien > Preparar Transito (Missao 14)`.

- Rede montada a partir das 4 ruas (cruzamentos e pontas automáticos); cerca de 6 carros por perto, surgindo e sumindo fora da vista.
- Conversíveis com motorista alienígena visível; mão direita, curvas nos cruzamentos, retorno no fim da rua; 9 m/s na reta, 4,5 m/s nas curvas.
- Freiam diante de jogador, alienígenas, carros e objetos; buzinam se o jogador ficar na frente; destravam sozinhos após alguns segundos.
- Telecinese ou Onda de Impacto num carro em movimento: vira carro comum e o motorista é ejetado (vira civil e conta para a cota, se houver vaga).
- Carros estacionados foram tirados das faixas.
- Ideia futura: carros de polícia com o nível de procurado.

### Missão 15 — Arte dos alienígenas e do protagonista

Pedido do autor em 6/11/2026: trocar as cápsulas por personagens estilizados (protagonista com animações, alienígenas civis e policiais).

## Depois da 0.1 (sem ordem aprovada)

- Trocar cápsulas por personagens estilizados (pacotes de assets prontos e animações do Mixamo, com ajustes no Blender).
- Sistema de consumo e DNA, setores e recompensas — regras ainda em aberto.
- Página "em breve" na Steam assim que houver imagens e um vídeo curto que mostrem o jogo.

## Decisão pendente para o autor: multijogador

O multijogador com até cinco jogadores está confirmado para o projeto (D06), mas sem versão definida. Multijogador online muda como quase todos os sistemas são escritos (movimento, física, inimigos, poderes) e costuma multiplicar o trabalho e os bugs, ainda mais num primeiro jogo.

**Recomendação do assistente:** manter a 0.1 e o primeiro lançamento como single player e tratar o multijogador como recurso de uma atualização posterior, aceitando que parte do código será adaptada. A alternativa é planejar a rede desde já, o que torna cada missão acima mais lenta. A escolha é do autor.
