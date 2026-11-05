# Escopo da Versão 0.1

[Índice do projeto](../README.md) · [Decisões](DECISOES.md) · [Roadmap](ROADMAP.md)

**Estado:** escopo confirmado pelo autor em 4 de novembro de 2026. A versão não tem prazo de entrega definido.

## Conteúdo confirmado

- Um ambiente 3D pequeno organizado em torno de uma esquina ou cruzamento.
- Algumas casas; um prédio pode fazer parte do cenário.
- Um pequeno parque com árvores em uma das esquinas.
- Um humano controlável e três alienígenas.
- Telecinese como o único poder jogável nesta versão. Seu efeito básico é levantar e arremessar inimigos, veículos e objetos.

O catálogo de poderes do projeto também inclui bioeletricidade, supervelocidade, superforça e metamorfose. Os quatro ficam fora do conteúdo jogável da 0.1. As evoluções de telecinese — erguer objetos maiores, controlar vários alvos e criar ondas de impacto — também ficam para versões futuras.

## O que permanece em aberto

- Se haverá um prédio além das casas; arquitetura, proporções e detalhes do parque.
- Aparência, identidade, papel e comportamento dos três alienígenas e do humano.
- Quais objetos concretos poderão ser levantados na cena; a presença de veículos na 0.1 não foi definida.
- Comandos, alcance, limites, feedback e regras de arremesso da telecinese.
- Se a opção de multijogador para até cinco jogadores, confirmada para o projeto, estará incluída na 0.1.
- Como o consumo de alienígenas, a progressão e o teletransporte da proposta anterior se encaixam em versões futuras.

## Estado observado no Unity

O projeto Unity está em `Game/ImNotAnAlien`, na versão `6000.3.28f1`, com URP `17.3.0` e Input System `1.20.1`. A cena `Assets/Scenes/Prototype.unity` contém o chão, uma cápsula provisória e o controlador de movimento WASD/mouse. A esquina, o parque, os personagens e a telecinese da Versão 0.1 ainda não foram montados.

## Próximas etapas propostas

1. Completar a validação manual do controlador existente.
2. Bloquear o espaço da esquina, das casas ou prédio e do parque.
3. Definir como representar o humano e os três alienígenas na cena.
4. Implementar e ajustar o efeito básico de telecinese com os alvos presentes no ambiente.
5. Avaliar o conjunto em Play Mode antes de decidir o que entra na versão seguinte.

Esta ordem é uma proposta de produção, sem calendário aprovado. O [Roadmap](ROADMAP.md) registra as dependências e as questões ainda abertas.
