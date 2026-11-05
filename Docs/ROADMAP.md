# Roadmap

[Índice](../README.md) · [Versão 0.1](VERSAO_0_1.md) · [Decisões](DECISOES.md) · [Fontes](FONTES.md)

A Versão 0.1 tem escopo confirmado, mas não tem datas de entrega. As etapas abaixo são uma proposta de produção para montar e avaliar esse recorte. [F09](FONTES.md#f09)

## Versão 0.1 — ambiente pequeno e telecinese

**Escopo confirmado:** uma esquina ou cruzamento; algumas casas e talvez um prédio; um pequeno parque com árvores em uma esquina; um humano, três alienígenas e telecinese como único poder jogável. A forma básica da telecinese levanta e arremessa inimigos, veículos e objetos. Consulte [Escopo da Versão 0.1](VERSAO_0_1.md).

**Estado observado:** o projeto Unity está em `Game/ImNotAnAlien`. A cena `Prototype` tem chão, cápsula provisória e controlador de movimento WASD/mouse. A validação contínua do movimento está pendente; o ambiente e a habilidade ainda não foram montados. [F07](FONTES.md#f07)

### Etapas propostas

1. Completar o teste manual do movimento e da câmera na cena Prototype.
2. Montar o espaço da esquina, com as casas e o possível prédio, e reservar uma das esquinas para o parque.
3. Definir a representação do humano e dos três alienígenas e colocá-los na cena.
4. Implementar a telecinese básica para levantar e arremessar alvos presentes no ambiente.
5. Percorrer a cena em Play Mode, avaliar o poder e registrar ajustes antes de ampliar o escopo.

**Critério de validação proposto:** o jogador consegue percorrer o cenário, reconhecer o parque e as casas, e usar a telecinese para levantar e arremessar os alvos disponíveis sem sair do espaço de teste. Se houver prédio, ele também fará parte da leitura do ambiente. Comandos, alcance, feedback e quais objetos estarão na cena ainda precisam ser definidos.

## Depois da Versão 0.1

**Proposta.** Usar o resultado do teste para decidir quais evoluções de telecinese e quais outros poderes entram em seguida. O catálogo do projeto inclui bioeletricidade, supervelocidade, superforça e metamorfose; suas descrições e evoluções estão em [Poderes](PODERES.md). A progressão por consumo e o teletransporte permanecem propostas anteriores, sem ordem ou regras aprovadas para as próximas versões.

## Requisito confirmado: multijogador

Um jogador poderá abrir a opção de multijogador e permitir que até cinco jogadores entrem no mesmo mundo. O limite está registrado como cinco pessoas no total, contando quem abre a opção, e deve ser confirmado antes da implementação. A inclusão desse requisito na Versão 0.1 não foi definida. Modelo de conexão, entrada na sessão e sincronização continuam em aberto. [F08](FONTES.md#f08)
