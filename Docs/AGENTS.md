# AGENTS.md

## Escopo

Este arquivo orienta o trabalho no repositório de **I'm not an Alien**, nome do protótipo. Comece pelo [README](../README.md), consulte o [registro de decisões](DECISOES.md) e leia os documentos do assunto que a tarefa modifica. A [lista de fontes](FONTES.md) reúne o histórico consultado e o estado observado no ambiente.

## Como interpretar o design

| Estado | Significado |
| --- | --- |
| **Confirmado** | O autor escolheu a direção de forma explícita ou ela consta da solicitação atual. |
| **Proposta do autor** | O autor apresentou uma ideia para discussão. |
| **Proposta do assistente** | A conversa contém uma sugestão do assistente sem aprovação explícita registrada. |
| **Registro anterior** | A informação está em uma síntese ou artefato anterior. Consulte a fonte primária antes de tratá-la como decisão. |
| **Em aberto** | Falta evidência suficiente para fixar uma definição ou há uma divergência por resolver. |
| **Observado no ambiente** | Informação verificada nos arquivos ou diretórios acessíveis. Registre a data e o alcance da verificação. |

- Preserve o grau de certeza de cada item. Uma síntese, um mockup ou um título como “história oficial” em resposta do assistente não concede aprovação ao conteúdo.
- O escopo confirmado da Versão 0.1 é um ambiente pequeno de esquina/cruzamento, algumas casas e talvez um prédio, um parque com árvores, um humano, três alienígenas e telecinese básica como único poder jogável. O catálogo com cinco poderes também foi definido pelo autor; os quatro poderes restantes e as evoluções estão fora da 0.1. Consulte [Versão 0.1](VERSAO_0_1.md), [decisões](DECISOES.md) e [fontes](FONTES.md#f09). A progressão por consumo e o teletransporte seguem como propostas anteriores, sem escopo confirmado para esta versão.
- Quando a tarefa aprovar ou alterar uma direção de design, atualize [decisões](DECISOES.md) e os documentos afetados. Registre a fonte, o estado e o alcance da mudança. Se a fonte e a implementação divergirem, registre a diferença e confirme a intenção quando ela alterar o comportamento solicitado.
- Mantenha as mudanças no escopo da tarefa. Trate protótipos de opções em aberto como experimentos identificados e preserve o estado da decisão até haver evidência de aprovação.

## Trabalho no Unity

- Antes de editar, identifique a raiz real do Unity por `Assets/`, `Packages/` e `ProjectSettings/`. Leia `ProjectSettings/ProjectVersion.txt` e `Packages/manifest.json` quando existirem. Se os arquivos ainda estiverem ausentes, trate a configuração como pendente e documente a limitação.
- Use a versão registrada pelo projeto existente. Mudanças de versão, pipeline de renderização, pacotes, sistema de entrada ou configurações globais devem decorrer do escopo da tarefa.
- Preserve os arquivos `.meta` e os GUIDs dos assets. Ao mover ou renomear um asset, mova seu `.meta` junto e confira referências de cenas, prefabs e componentes. Prefira a operação pelo editor quando a tarefa depender de importação ou serialização do Unity.
- Reutilize sistemas, componentes e convenções já presentes no projeto. Edite cenas, prefabs e configurações serializadas no menor escopo possível. Revise o diff para detectar reserialização em massa, referências ausentes e alterações geradas pelo editor.
- Mantenha caches e saídas de build fora das mudanças de código e documentação. Preserve a configuração de versionamento existente e verifique as regras para arquivos binários antes de adicionar assets grandes.

## Verificação e entrega

- Para mudanças jogáveis, identifique uma cena de teste real. Verifique compilação e Console, reproduza o comportamento em Play Mode e execute os testes existentes que cubram a mudança quando o ambiente permitir.
- Para mudanças em build, valide o alvo e a configuração pedidos. Para documentação, confira links relativos, rastreabilidade e coerência dos estados de decisão.
- Registre o que foi verificado, os caminhos relevantes e qualquer etapa que dependa de um projeto inicializado ou de acesso ao editor.
