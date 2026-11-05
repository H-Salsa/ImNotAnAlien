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

### Missão 0 — Preparar a base

**Objetivo:** proteger o projeto e conhecer o editor.

- Instalar o Git e criar o repositório na raiz `C:\WorkSpace\ImNotAnAlien`, com `.gitignore` para Unity (ignora `Library/`, `Temp/`, `Logs/`, `UserSettings/` e os arquivos `.csproj`/`.slnx` gerados).
- Opcional: enviar para um repositório privado no GitHub (cópia de segurança fora do computador).
- Conhecer as janelas principais: Scene, Game, Hierarchy, Inspector, Project e Console.

**Pronto quando:** o primeiro commit existe e o autor sabe selecionar um objeto e mudar valores no Inspector.

### Missão 1 — Andar e olhar

**Objetivo:** concluir o teste do movimento que já existe ([plano de movimento](superpowers/plans/2026-11-04-player-movement.md), passo 6 pendente).

- Testar WASD + mouse na cena `Prototype`.
- Ajuste proposto: travar e esconder o cursor do mouse durante o jogo (Esc libera).
- Ajustar velocidade, sensibilidade e distância da câmera pelo Inspector até ficar confortável.

**Pronto quando:** andar pela cena é agradável por um minuto inteiro sem incômodo.

### Missão 2 — Bloquear a esquina (blockout)

**Objetivo:** montar o espaço da 0.1 com formas simples.

- Ruas e calçadas com planos/cubos; casas e o possível prédio com cubos; árvores do parque com cilindro + esfera.
- Proposta: usar o pacote **ProBuilder** (gratuito, da Unity) para formas um pouco melhores que cubos.
- Cores simples por material para diferenciar rua, casa, parque.

**Pronto quando:** é possível reconhecer a esquina, as casas e o parque andando pela cena.

### Missão 3 — Os três alienígenas

**Objetivo:** colocar alvos vivos na cena.

- Alienígenas provisórios: cápsulas coloridas com "olhos" (esferas).
- Comportamento simples com NavMesh (pacote já instalado): passear pelo bairro; fugir ao ver o jogador.
- Vida básica: podem ser derrotados.

**Pronto quando:** os três se movem pela esquina e reagem à presença do jogador.

### Missão 4 — Telecinese básica

**Objetivo:** o coração da 0.1 — levantar e arremessar.

- Mira no centro da tela; segurar o botão para levantar o objeto/alienígena mirado; soltar ou clicar para arremessar na direção da câmera.
- Objetos arremessáveis com física (Rigidbody): caixas, lixeiras, bancos do parque, um "carro" de cubos.
- Alienígenas viram corpos físicos quando levantados e podem ser derrotados ao colidir com força.

**Pronto quando:** é possível levantar e arremessar objetos e alienígenas de forma controlável.

### Missão 5 — Deixar gostoso (game feel)

**Objetivo:** fazer o poder parecer poderoso e engraçado.

- Tremida de câmera no impacto, partículas, brilho no objeto levantado, sons provisórios.
- Reações exageradas dos alienígenas (pulo de susto, giro no ar).

**Pronto quando:** arremessar um alienígena num prédio faz o autor rir ou querer repetir.

### Missão 6 — Avaliar a 0.1

**Objetivo:** decidir o próximo passo com base no que foi jogado.

- Jogar por 10 minutos, gravar um vídeo curto, mostrar para 2 ou 3 pessoas.
- Registrar o que funcionou e o que não funcionou em [Decisões](DECISOES.md).

**Pronto quando:** existe uma lista do que entra na Versão 0.2.

## Depois da 0.1 (sem ordem aprovada)

- Trocar cápsulas por personagens estilizados (pacotes de assets prontos e animações do Mixamo, com ajustes no Blender).
- Sistema de consumo e DNA, setores e recompensas — regras ainda em aberto.
- Página "em breve" na Steam assim que houver imagens e um vídeo curto que mostrem o jogo.

## Decisão pendente para o autor: multijogador

O multijogador com até cinco jogadores está confirmado para o projeto (D06), mas sem versão definida. Multijogador online muda como quase todos os sistemas são escritos (movimento, física, inimigos, poderes) e costuma multiplicar o trabalho e os bugs, ainda mais num primeiro jogo.

**Recomendação do assistente:** manter a 0.1 e o primeiro lançamento como single player e tratar o multijogador como recurso de uma atualização posterior, aceitando que parte do código será adaptada. A alternativa é planejar a rede desde já, o que torna cada missão acima mais lenta. A escolha é do autor.
