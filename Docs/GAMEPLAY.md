# Gameplay

[Índice](../README.md) · [Game design](GAME_DESIGN.md) · [Decisões](DECISOES.md)

## Experiência geral confirmada

O autor definiu sobrevivência, exploração e ação em mundo aberto, com foco na diversão durante a sessão. A Versão 0.1 é um recorte pequeno dessa direção. [F01](FONTES.md#f01) · [F09](FONTES.md#f09)

## Versão 0.1

O jogador controla um humano numa esquina ou cruzamento com um pequeno parque, algumas casas e talvez um prédio. Há três alienígenas e telecinese básica como único poder jogável. O jogador poderá levantar e arremessar inimigos, veículos e objetos que existam na cena. O conjunto exato de alvos, os comandos, o alcance e o feedback ainda precisam ser decididos. [F09](FONTES.md#f09)

O escopo da 0.1 não define consumo de alienígenas, desbloqueio de poderes, objetivo de campanha ou teletransporte. Esses elementos pertencem à proposta anterior para o jogo maior e permanecem sem regras aprovadas para esta versão.

## Ciclo de jogo proposto para o projeto maior

1. Iniciar com um poder escolhido entre cinco opções.
2. Explorar o planeta e encontrar alienígenas.
3. Lidar com o encontro e consumir os alvos elegíveis para progredir. A sequência entre confronto e consumo segue em aberto.
4. Desbloquear outros poderes ou melhorar os atuais.
5. Continuar até cumprir a condição de teletransporte e tentar voltar para casa.

O autor descreveu um único objetivo principal, eliminar todos os alienígenas, e associou o teletransporte ao consumo de todos. Essa proposta não foi incluída no escopo da 0.1. O catálogo e o escopo de cada poder estão em [Poderes](PODERES.md). [F01](FONTES.md#f01) · [F09](FONTES.md#f09)

## Multijogador

Um jogador pode abrir a opção de multijogador. Enquanto ela estiver aberta, até cinco jogadores podem entrar no mesmo mundo. Esta documentação interpreta o limite como cinco participantes simultâneos no total, contando quem abriu a opção; confirmar esse detalhe antes da implementação. [F08](FONTES.md#f08)

O modelo de conexão (local ou online), a forma de encontrar e entrar na sessão, a entrada após o início, a sincronização do mundo, o salvamento e o comportamento quando quem abriu a opção sai ainda precisam ser definidos. Também está em aberto se a opção entra na Versão 0.1. [F08](FONTES.md#f08) · [F09](FONTES.md#f09)

## Eliminação, consumo e estado do mundo

| Questão | Estado no histórico |
| --- | --- |
| Quando um alienígena pode ser consumido? | Em aberto. É preciso definir se consumo exige derrota, se pode ser direto e quais alvos são elegíveis. |
| Eliminação e consumo contam como o mesmo evento? | Em aberto. O autor usou as duas condições no desenho proposto. A relação entre elas ainda precisa de uma regra explícita. |
| O que acontece se um alvo for eliminado sem ser consumido? | Em aberto. A regra precisa esclarecer como esse caso afeta a possibilidade de concluir o jogo. |
| Quem conta no total? | Em aberto. População civil, inimigos, reforços e outras categorias ainda precisam de definição. |
| Como o jogador acompanha o progresso? | O assistente sugeriu população contabilizada por região e um contador. O valor exibido na conversa era ilustrativo. |
| A população pode se repor? | O assistente sugeriu centros de reprodução que repõem habitantes e podem ser desativados. A existência desses centros e a regra de reposição seguem em aberto. |

[F01](FONTES.md#f01)

## Sistemas discutidos

| Área | Propostas do assistente | Pontos em aberto |
| --- | --- | --- |
| Câmera e controle | Terceira pessoa atrás do personagem. Câmera mais ampla ou próxima do ombro foram levantadas em conversa. | Enquadramento, entradas, movimentação e interação. |
| Sobrevivência | Vida e cura, coleta de comida e equipamento em um modelo simplificado. | Recursos necessários, recuperação, derrota e retomada. |
| Exploração | Bairros, parques e instalações, com objetos e prédios interativos. | Acesso, densidade, travessia e motivos para visitar cada região. |
| Ação | Ataques, poderes, armas, veículos e destruição de objetos em diferentes propostas. | Ataque inicial, defesa, alvos, dano e comportamento de inimigos. |
| Resposta dos habitantes | Civis reativos, patrulhas, disfarces e um sistema de alerta escalonado. | Papéis dos NPCs, reação à presença do jogador e adoção de disfarce ou alerta. |
| Escala | Contabilidade regional de habitantes com NPCs ativos perto do jogador. | Forma de representar a população e de preservar o progresso entre encontros. |

Essas propostas surgiram em momentos diferentes. Parte delas antecede o desenho de abdução, poderes e consumo. [F01](FONTES.md#f01) · [F02](FONTES.md#f02)

## Validação proposta para a Versão 0.1

O teste proposto verifica se o jogador consegue percorrer a esquina, reconhecer o parque e usar telecinese para levantar e arremessar os alvos disponíveis. Não exige um sistema de consumo ou progressão. Veja as próximas etapas no [Roadmap](ROADMAP.md). [F09](FONTES.md#f09)
