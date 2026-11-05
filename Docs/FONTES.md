# Fontes e evidências

[Índice do projeto](../README.md) · [Registro de decisões](DECISOES.md)

Levantamento de 4 de novembro de 2026. As fontes F01 a F05 pertencem ao projeto **I'm not an Alien** no ChatGPT. A leitura abrangeu os turnos disponibilizados nesses chats e as duas capturas anexadas em F02. As respostas do assistente fornecem contexto para as propostas. Uma afirmação de aprovação nelas depende de uma declaração correspondente do autor. F06 é uma síntese anterior. F07 descreve os arquivos observados no computador. F08 registra o requisito de multijogador e F09 atualiza o escopo da primeira versão e o catálogo de poderes.

## F01

**Descrição do jogo** · [Abrir conversa](chatgpt-conversation://6aea1e38-1e74-83ea-8db1-dcc880dfafbf)

Mensagens do autor para localizar as evidências na conversa.

| Trecho ou assunto | ID da mensagem |
| --- | --- |
| Humano no planeta alienígena, arquitetura próxima da terrestre e alienígenas humanoides. | `eb421406-00d1-4cac-90ca-0b3a34b4eb2e` |
| Escolha da mistura de sobrevivência, exploração e ação. | `01d2da57-9510-477f-9ea3-1c4988e9d595` |
| Escolha do 3D estilizado na época em que o jogo era chamado de INTRUSO. | `c3d96763-a52f-4ade-96a6-b0746c6d6755` |
| Mundo aberto, diversão imediata e proposta de eliminar todos os alienígenas. | `e23b4c36-33fd-4d21-9b0c-eae9bafe3a0e` |
| Captura, experimentos, escolha de um de cinco poderes, consumo e teletransporte. Inclui a abertura em vídeo e a ideia de um final em outro planeta. | `0b9fbe77-42d6-49f6-b9c0-334ee5c92be8` |

A resposta `d1092656-c874-462a-b1f3-1c146da5f746` reúne a lista sugerida de poderes, recursos de progressão, gestão de população e formas de consumo. As respostas anteriores exploram uma chegada acidental, motivações, sistemas de alerta e direções visuais. Os grupos de imagens embutidos nas respostas são referências apresentadas na discussão. A leitura disponível forneceu as descrições das consultas dessas referências.

## F02

**Projeto jogo alienígenas** · [Abrir conversa](chatgpt-conversation://6aea5a14-4eac-83e9-ab0e-e7d2664b1c7b)

| Evidência | ID da mensagem do autor |
| --- | --- |
| Pergunta sobre o estado do Hub com captura do download da Unity 6.6. | `eafa463f-458a-4e60-850d-9981e589c360` |
| O Hub iniciou automaticamente a instalação da 6.6. | `17629231-9c0a-4387-97c3-083509dd520f` |
| Visual Studio Community 2026 e Windows Build Support (IL2CPP) apareciam marcados na seleção de módulos. | `20e1c470-59a0-45d6-b814-b95e392577f6` |
| Download da 6.6 concluído e início da instalação da LTS. | `af3db7dd-cc4e-422e-955c-e68c3594a5e9` |
| Reiteração de **I'm not an Alien** como nome do protótipo. | `4f823ef0-6550-4fd6-b751-0afc0ce5c8b4` |

A primeira captura, `image(2).png`, mostra `6000.6.8f1` em download. A segunda, `image(3).png`, mostra o download dessa versão concluído, `6000.3.28f1` começando a baixar e o módulo Windows IL2CPP em fila. As duas imagens mostram estados do Hub no momento da conversa. Consulte [F07](#f07) para o que foi observado depois no disco.

A recomendação de terceira pessoa está na resposta `bb185580-2e1d-4b25-8b7d-b564712806dc`, que deixa o enquadramento como pergunta. Unity/C#, PC/Steam e trabalho solo aparecem na recapitulação `1baf2855-83d9-40f8-8bdd-29c822fc5abc`. Universal 3D (URP) e VS Code foram sugeridos durante a configuração.

## F03

**Escolher nome do game** · [Abrir conversa](chatgpt-conversation://6aeaa373-e848-83e8-914b-8fa97f48362f)

A mensagem do autor `2dc73b92-0dad-4e68-8349-4043bb3dfdb3` escolhe **I'm not an Alien** para o protótipo. A mensagem `00a7abd2-dcfc-4a37-a414-6693bb840e03` rejeita a primeira lista de nomes. As alternativas de título e a frase *I was human when I got here.* ficaram como sugestões.

## F04

**Arquivo Markdown Decisões Codex** · [Abrir conversa](chatgpt-conversation://6aeaa75b-3168-83ea-8d8a-39aa3e14cc2c)

A mensagem `77256de9-bb5f-4787-bfb7-8b4e35286c8b` pede a consolidação anterior para o Codex. A resposta aponta para um arquivo gerado chamado `IM_NOT_AN_ALIEN.md` e descreve seu conteúdo. A leitura da conversa retornou o link do arquivo, sem seus bytes. [F06](#f06) identifica o Markdown com esse nome encontrado localmente.

## F05

**Enviar conversas ao Codex** · [Abrir conversa](chatgpt-conversation://6aeaaa0f-c078-83e9-b207-84e6339484a2)

A mensagem `2f93eddc-01c3-4b78-a552-538cd59f7576` pede documentação Markdown completa com `AGENTS.md` e os documentos de design. A resposta `3dcc7c7a-c47d-4e8f-86e9-db9f8c35b895` registra o escopo de entrega proposto e o encaminhamento para o Work.

## F06

**Síntese anterior** · `C:/WorkSpace/ImNotAnAlien/Docs/IM_NOT_AN_ALIEN.md`

Markdown local intitulado **I'm not an Alien**, com data de 4 de novembro de 2026. Reúne visão, premissa, DNA, cenas, estado do Unity, protótipo e pendências. O documento registra terceira pessoa, Steam, objetivo de receita e início solo. Esses itens conservam a classificação de registro anterior quando as mensagens do autor consultadas deixam a confirmação em aberto. O caminho identifica a evidência na máquina de origem.

## F07

**Inspeção do ambiente local** · 4 de novembro de 2026

- Na inspeção inicial, a pasta de trabalho ainda não tinha `Assets`, `Packages` ou `ProjectSettings/ProjectVersion.txt`; as pastas `Game` e `Blender` estavam vazias.
- A pasta `C:/Program Files/Unity/Hub/Editor` continha `Unity.exe` em `6000.3.28f1` e `6000.6.8f1`. Também foram observados arquivos das variantes Windows 64 bits com IL2CPP na LTS.
- A inspeção posterior localizou o projeto Unity em `C:/WorkSpace/ImNotAnAlien/Game/ImNotAnAlien`, com `Assets`, `Packages`, `ProjectSettings`, Unity `6000.3.28f1`, URP `17.3.0` e Input System `1.20.1`.
- `Assets/Scenes/Prototype.unity` contém `Main Camera`, `Directional Light`, `Global Volume`, `Chao` e `Capsule`. A cápsula tem `CharacterController` e `PrototypeThirdPersonController`, com `InputSystem_Actions.inputactions` e `Main Camera` atribuídos. O ambiente da Versão 0.1, os personagens e a telecinese ainda não estão na cena.
- O editor abriu a cena em Play Mode. Na observação, o Console não mostrava erros, avisos ou logs e um arraste do mouse alterou a câmera. O teste sustentado de WASD foi interrompido antes da validação; não foi gerado build.

## F08

**Pedido do autor sobre multijogador** · 4 de novembro de 2026

O autor pediu para adicionar ao projeto uma opção de multijogador, permitindo que até cinco jogadores entrem no mundo quando um dos jogadores abrir essa opção. O pedido não define o modelo de conexão, a forma de entrada dos participantes nem regras de sincronização. Nesta documentação, o limite de cinco foi interpretado como o total simultâneo, incluindo quem abre a opção; confirmar essa interpretação antes da implementação.

## F09

**Escopo da Versão 0.1 e catálogo de poderes do autor** · 4 de novembro de 2026

O autor definiu a primeira versão como um ambiente pequeno de esquina ou cruzamento, com algumas casas, talvez um prédio e um pequeno parque com árvores em uma das esquinas. O elenco terá um humano e três alienígenas. A versão terá um único poder: telecinese. O autor também descreveu cinco poderes para o catálogo do projeto — telecinese, bioeletricidade, supervelocidade, superforça e metamorfose — com seus efeitos básicos e evoluções. A resposta seguinte escolheu atualizar os Docs primeiro; não autorizou a montagem da cena Unity nesta etapa.
