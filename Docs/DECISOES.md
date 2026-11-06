# Registro de decisões

[Índice do projeto](../README.md) · [Fontes e evidências](FONTES.md)

## Como interpretar o registro

| Classificação | Evidência |
| --- | --- |
| **Confirmado** | O autor escolheu o item ou o declarou como direção do projeto. |
| **Proposta do autor** | O autor apresentou uma ideia de trabalho para avaliação. |
| **Proposta do assistente** | Uma resposta do histórico apresentou a ideia sem aceite explícito localizado. |
| **Registro anterior** | Uma síntese anterior registrou o item sem confirmação direta localizada nas mensagens consultadas. |
| **Em aberto** | Falta uma escolha ou a evidência disponível permite mais de uma interpretação. |
| **Observado no ambiente** | A inspeção dos arquivos mostrou um estado local. |

## Confirmado

| ID | Decisão | Escopo e fonte |
| --- | --- | --- |
| D01 | Usar **I'm not an Alien** como nome do protótipo. | O autor escolheu essa grafia em [F03](FONTES.md#f03) e reiterou a escolha em [F02](FONTES.md#f02). |
| D02 | Acompanhar um ser humano num planeta alienígena e misturar sobrevivência, exploração e ação. | Direção declarada em [F01](FONTES.md#f01). |
| D03 | Adotar mundo aberto, voltado para diversão imediata, como visão geral do jogo. | Direção declarada em [F01](FONTES.md#f01). O recorte confirmado da primeira versão é menor e está em D07. |
| D04 | Trabalhar com visual 3D estilizado. | Escolha explícita em [F01](FONTES.md#f01). |
| D05 | Usar Unity para o projeto. | A configuração começou em [F02](FONTES.md#f02) e o pedido atual identifica o repositório como Unity. |
| D06 | Incluir uma opção de multijogador que permita até cinco jogadores no mesmo mundo quando um jogador abrir essa opção. | Direção confirmada em [F08](FONTES.md#f08). Esta documentação assume cinco participantes no total, incluindo quem abre a opção; confirmar esse detalhe antes da implementação. |
| D07 | Fazer a Versão 0.1 como um ambiente pequeno de esquina ou cruzamento, com algumas casas, talvez um prédio, um pequeno parque com árvores, um humano, três alienígenas e um poder. | Escopo confirmado pelo autor em [F09](FONTES.md#f09). A participação do multijogador nesta versão não foi especificada. |
| D08 | Usar telecinese como único poder jogável na Versão 0.1 e manter cinco poderes no catálogo do projeto: telecinese, bioeletricidade, supervelocidade, superforça e metamorfose. | Lista e efeitos descritos pelo autor em [F09](FONTES.md#f09). As evoluções listadas e os outros quatro poderes ficam para versões futuras. |
| D09 | Controlar a telecinese com um único botão (botão direito do mouse): toque rápido levanta o alvo ou solta o objeto segurado; segurar e soltar arremessa na direção da mira. | Escolha do autor em 5 de novembro de 2026, na implementação da Missão 4 ([Plano de trabalho](PLANO_DE_TRABALHO.md)). O toque que levanta mantém o objeto flutuando e o limite de 0,25 s entre toque e segurar são detalhes do assistente, ajustáveis no componente `TelekinesisPower`. Alcance, pesos, dano e feedback seguem como valores de protótipo. |
| D10 | Coletar DNA por orbes híbridos: ao morrer, o alienígena solta orbes de DNA; os orbes voam até o jogador quando ele chega perto (raio de atração, que pode crescer com evoluções); orbe não coletado depois de um tempo vem sozinho de qualquer distância (rede de segurança). Um alienígena conta como consumido quando o último orbe dele é absorvido. Uma ação futura, **Devorar**, consumirá um alienígena enfraquecido ainda vivo e renderá o dobro de DNA. | Proposta do assistente aprovada pelo autor em 5 de novembro de 2026, entre quatro opções levantadas pelo autor (coletar do chão, automático, botão, híbrido). Com a rede de segurança, todo alienígena eliminado acaba consumido, o que resolve a relação entre eliminar e consumir citada abaixo. O Devorar fica para uma versão posterior à 0.2. Valores (10 de DNA em 5 orbes, raio 6 m, 15 s) são de protótipo. |
| D11 | Mostrar no canto superior direito da tela o contador de alienígenas consumidos e o total de DNA, separados: alienígenas consumidos é o **objetivo** do setor (cada alienígena conta 1); DNA é o **recurso** das evoluções (o Devorar rende o dobro). O mesmo canto receberá um minimapa no futuro. Limpar o setor mostra "SETOR LIMPO!" e dá um bônus de DNA. | Contador e posição definidos pelo autor em 5 de novembro de 2026; a separação objetivo/recurso responde à observação do autor de que DNA e alienígenas consumidos não terão o mesmo número. Bônus de 25 de DNA é valor de protótipo, coerente com a recompensa por setor limpo da descrição do projeto. |
| D12 | **Devorar** é uma evolução da telecinese: ao mirar um alienígena ferido e próximo (ou segurando um com a telecinese), aparece a tecla de ação sobre ele; a tecla **1** puxa e devora o alienígena, que conta como consumido e rende o dobro de DNA. | Definido pelo autor em 5 de novembro de 2026 (detalha D10). "Ferido" = metade da vida ou menos e "próximo" = até 10 m são valores do assistente. Até o menu de evoluções existir (Missão 9), a habilidade vem liberada para teste. |
| D13 | Criar **alienígenas policiais** que atiram de volta, com mais vida que os civis e um **quepe de polícia** para diferenciá-los. | Definido pelo autor em 5 de novembro de 2026. Valores do assistente: 160 de vida (civis: 100), 20 de DNA, tiro visível com aviso antes do disparo e 10 de dano. Para isso o jogador ganhou vida, regeneração e renascimento sem perda de progresso. |
| D20 | **Visual dos alienígenas e do protagonista (Missão 15).** Protagonista: modelo base Quaternius (CC0) com animações UAL, provisório até haver roupas/visual próprio. Alienígenas: humanoides magrelos, **cabeça grande em bulbo com listras**, **olhos grandes laranja** com pálpebra que dá a expressão, **mãos de 3 dedos** (polegar + 2 dedos grandes de ponta arredondada), mesmo esqueleto do protagonista (usam as mesmas animações). Civis com roupas humanas comuns (camiseta, calça, tênis) e peles variadas; **verde-azulado é exclusivo da polícia**. Policiais de macacão azul-marinho, colete, cinto com coldre e botas, com **boné** nos níveis de procurado mais baixos e **capacete** nos mais altos (policiais mais agressivos). | Arte conceitual e regras do autor em 6 de novembro de 2026; refinamentos sugeridos pelo assistente e aceitos (verde-azulado exclusivo, 3 dedos, estilo low poly com cores chapadas, detalhe ciano brilhante no policial). Modelo gerado no Blender por `Art/Blender/alien_builder.py` a partir do corpo base. A partir de qual nível de procurado entra o capacete ainda será definido. |
| D19 | **População dinâmica com cota e nível de procurado.** Em vez de um número fixo de alienígenas no mapa, eles surgem fora da visão do jogador conforme ele anda (e somem longe e fora de vista quando estão calmos). Cada região tem uma **cota** de alienígenas a consumir; o **Vilarejo do Bilu tem cota 250**, dividida por setor (Esquina 50, Vila das Casas 75, Parque e Lago 55, Margem do Rio 70), com **+40 DNA por setor limpo** e **+100 DNA pela região**. O **nível de procurado** (1 a 5 estrelas) sobe ao causar caos e traz mais policiais. Policiais **sempre dão DNA**; os que surgem nos **níveis 1 e 2 contam para a cota**, os dos **níveis 3, 4 e 5 não contam**. | Ideia e regras do autor em 6 de novembro de 2026; cotas por setor e bônus são sugestão do assistente aceita pelo autor. Trânsito com carros e motoristas fica para depois. Substitui os 65 alienígenas fixos gerados na Missão 11. A economia de DNA (evoluções) precisará de novo ajuste, já que a região passa a render bem mais DNA. |
| D18 | **Região 1: "Subúrbio Tranquilo"** — bairro residencial grande que expande a esquina atual. Quatro setores: Esquina (a atual, ampliada), Vila das Casas, Parque e Lago, Margem do Rio (com 2 pontes). Cerca de 300 × 300 m e cerca de 65 alienígenas (50 civis e 15 policiais). | Escolha do autor em 5 de novembro de 2026 entre três opções do assistente (A: Subúrbio Tranquilo; B: Centro da Cidade; C: Da Fuga à Cidade, com laboratório). Motivos apresentados: reaproveita o que existe e é a mais rápida de construir; risco apontado: pouca variedade visual entre setores. Detalhes definidos pelo autor em 5 de novembro de 2026: nome **Vilarejo do Bilu**; quem cai no rio nada até a margem (sem dano); todos os setores abertos desde o início; disposição aprovada (Esquina e Vila das Casas ao norte do rio, Parque e Lago e Margem do Rio ao sul, 2 pontes). |
| D17 | **Mundo em regiões planas com ilusão de planeta.** O planeta é formado por regiões (cada uma um mundo aberto plano, dividido em setores). A ideia de "planeta inteiro" vem da apresentação: mapa-múndi em forma de globo mostrando as regiões e o progresso, viagem rápida entre regiões (ex.: estações de teletransporte alienígenas) e horizonte curvo como efeito visual. O objetivo global continua sendo consumir todos os alienígenas do planeta. | Definido pelo autor em 5 de novembro de 2026, após comparar com um planeta esférico de verdade (inspiração: Moon Sweepers). Planeta esférico descartado por exigir gravidade esférica e movimento próprio dos alienígenas (o NavMesh não funciona em esferas). Sugestão do assistente: lançar com 1 região e adicionar regiões em atualizações; salvamento por região, contador global, globo e viagem rápida entram quando houver a segunda região. |
| D16 | **Evoluções compradas com DNA** num menu (Tab, jogo pausado): Ímã de DNA, Força Telecinética, Alcance Mental, Pele Alienígena, Devorar (agora começa bloqueado, conforme D12) e Onda de Impacto (tecla 2, evolução da telecinese já prevista em [Poderes](PODERES.md)). | Rumo (gastar DNA em evoluções) aprovado pelo autor em 5 de novembro de 2026. A lista, os níveis e os custos são proposta do assistente para teste, editáveis no componente `EvolutionSystem`. Um setor rende cerca de 165 a 300 de DNA; o total das evoluções é cerca de 1.000. |
| D15 | **Estruturas destrutíveis** (detalha A07): desmoronam ao serem destruídas; os pedaços caem, atravessam o chão e são apagados; fica um pequeno escombro no lugar. Uma casa pequena cai com alguns arremessos de carro, com uma arma futura e, com certeza, com a Superforça; objetos leves (caixas, lixeiras) não derrubam casas. Destruir **não dá recompensa**, é só visual. **Alienígenas não ficam dentro das casas**, para o jogador não precisar destruir todas para achá-los. | Definido pelo autor em 5 de novembro de 2026. Vida de cada estrutura e a arma futura ainda serão definidas. |
| D14 | Reduzir a facilidade de matar alienígenas: o dano por impacto passa a depender também do peso do objeto (caixa machuca pouco; carro e paredes machucam muito). | Pedido do autor em 5 de novembro de 2026. Fórmula e números são do assistente e ajustáveis em `AlienHealth`. |

## Propostas do autor

| ID | Ideia de trabalho | Fonte e destino |
| --- | --- | --- |
| A01 | Usar prédios, casas e parques familiares, com alienígenas de aparência humanoide. | [F01](FONTES.md#f01) · [Direção artística](DIRECAO_ARTISTICA.md) e [personagens](PERSONAGENS.md) |
| A02 | Ter um único objetivo principal, a eliminação de todos os alienígenas do planeta. | [F01](FONTES.md#f01) · [Game design](GAME_DESIGN.md) |
| A03 | Abrir a história com um humano capturado por alienígenas. Os experimentos desencadeiam os poderes. A mistura de DNA é uma hipótese para explicar o efeito. Um vídeo curto é uma opção de abertura. | [F01](FONTES.md#f01) · [História](HISTORIA.md) |
| A04 | Proposta anterior: começar escolhendo um entre cinco poderes e usar o consumo para desbloquear ou melhorar poderes. | [F01](FONTES.md#f01) · [Poderes](PODERES.md) e [gameplay](GAMEPLAY.md). A escolha entre cinco não se aplica à 0.1, que tem apenas telecinese; progressão por consumo segue sem confirmação. |
| A05 | Liberar o teletransporte ao consumir todos os alienígenas, com o objetivo de voltar para casa. | [F01](FONTES.md#f01) · [História](HISTORIA.md) e [gameplay](GAMEPLAY.md) |
| A07 | Estruturas destrutíveis: ao serem destruídas, desmoronam; os pedaços caem, atravessam o chão e são apagados (para não pesar o computador); no lugar fica um pequeno escombro com aspecto de destruído. | Proposta do autor em 5 de novembro de 2026 · [Plano de trabalho](PLANO_DE_TRABALHO.md). Confirmada e detalhada em D15. Notas do assistente: deixar os pedaços caírem 1–2 s antes de afundar; atualizar o NavMesh quando a estrutura cair; para a arte final, pré-fraturar os modelos no Blender (Cell Fracture). |
| A06 | Mostrar em vídeo um teletransporte que dá errado e leva a outro planeta com alienígenas diferentes. A cena deixa um gancho para uma possível continuação. | [F01](FONTES.md#f01) · [História](HISTORIA.md) |

**Atualização (D10):** com a coleta híbrida e a rede de segurança, todo alienígena eliminado acaba consumido; a contagem do setor usa alienígenas consumidos. Texto original mantido para histórico: o objetivo de eliminar todos e a condição de consumir todos aparecem no mesmo desenho. Como as duas ações se relacionam em combate e como contabilizar os casos em que um alienígena é eliminado sem ser consumido seguem em aberto.

## Propostas do assistente

| ID | Ideia discutida | Estado |
| --- | --- | --- |
| P01 | Primeira sugestão do assistente para um catálogo de telecinese, bioeletricidade, supervelocidade, superforça e metamorfose. | O autor especificou o catálogo e seus efeitos em [F09](FONTES.md#f09); a direção atual está registrada em D08 e [Poderes](PODERES.md). |
| P02 | Consumo por partículas luminosas, por transformação orgânica ou por animação cartunesca. Recursos separados de DNA e energia genética, com possibilidade de inimigos especiais. | Alternativas e regras candidatas em [F01](FONTES.md#f01). |
| P03 | População por região, reposição por centros de reprodução, contagem no HUD, pontos de interesse, reação das autoridades, veículos e destruição. | Propostas exploratórias em [F01](FONTES.md#f01). |
| P04 | Câmera em terceira pessoa, com enquadramento distante ou sobre o ombro. | Recomendação em [F02](FONTES.md#f02), registrada como formato na síntese [F06](FONTES.md#f06). A escolha do autor segue em aberto. |
| P05 | Universal 3D (URP), VS Code e o nome técnico `ImNotAnAlien`. | Sugestões de configuração em [F02](FONTES.md#f02). |
| P06 | Começar com uma área pequena e testar movimento, perseguição, ataque, consumo e um poder demonstrável. | Proposta anterior em [F06](FONTES.md#f06), substituída para a 0.1 pelo escopo confirmado em D07. |
| P07 | Referências visuais, silhuetas, paletas, categorias de NPC, motivações alternativas e detalhes de roteiro. | Explorações de [F01](FONTES.md#f01), detalhadas nos documentos temáticos conforme a pertinência. |
| P08 | Frases promocionais e avaliação de lembrança do nome. | Sugestões em [F03](FONTES.md#f03). O autor rejeitou a primeira lista de títulos e escolheu o nome do protótipo. |

## Registro anterior e ambiente

| Assunto | O que a evidência permite afirmar |
| --- | --- |
| Nome de trabalho | **INTRUSO** aparece nas mensagens iniciais. A escolha posterior se refere ao nome do protótipo. [F01](FONTES.md#f01), [F02](FONTES.md#f02) e [F03](FONTES.md#f03). |
| Intenção comercial e equipe | PC/Steam e desenvolvimento solo aparecem na recapitulação do assistente. A síntese anterior registra intenção de receita, primeiro jogo, experiência em programação e a possibilidade de ajuda de outro desenvolvedor. Confirmação direta desses itens segue em aberto. [F02](FONTES.md#f02) e [F06](FONTES.md#f06). |
| Tecnologia | Projeto Unity localizado em `Game/ImNotAnAlien`, Unity `6000.3.28f1`, URP `17.3.0` e Input System `1.20.1`. [F07](FONTES.md#f07). |
| Estado observado no ambiente | `Prototype.unity` contém chão, cápsula provisória e controlador de movimento. A cena da Versão 0.1 ainda não foi montada; a validação sustentada de movimento está pendente. [F07](FONTES.md#f07). |

## Em aberto

| Área | Decisão ou evidência necessária |
| --- | --- |
| Identidade e narrativa | Nome comercial, identidade do protagonista e do planeta, tom, abertura, detalhes do desfecho e continuação. |
| Controle e regras | Câmera, controles, combate, condições de sobrevivência, população, contagem de progresso e interação de eliminação com consumo. |
| Poderes | Comandos, alcance, limites e feedback da telecinese; momento e forma de adicionar os quatro outros poderes; regras de evolução e progressão. |
| Multijogador | Modelo de conexão, forma de entrada, sincronização, entrada durante a sessão, salvamento, comportamento ao sair, confirmação do limite de cinco e inclusão ou não na Versão 0.1. |
| Arte e produção | Se haverá prédio, arquitetura, escala, personagens, referências aprovadas, produção dos assets e organização técnica da cena. |
| Lançamento | Plataforma e estratégia comerciais confirmadas pelo autor, cronograma, preço e demais detalhes de publicação. |

## Como registrar uma confirmação

Registre a escolha com data, responsável, fonte, escopo e efeitos nos documentos temáticos. Preserve o ID quando houver evolução da mesma decisão. Se uma decisão substituir outra, indique a relação e mantenha o histórico. Em caso de divergência, consulte primeiro a declaração direta do autor e documente a interpretação em aberto até haver uma escolha.
