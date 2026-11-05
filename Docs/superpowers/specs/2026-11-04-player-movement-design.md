# Design: primeiro teste de movimento do jogador

**Status:** rascunho para aprovação  
**Data:** 4 de novembro de 2026

## Intenção

Testar se é confortável controlar um personagem em terceira pessoa na cena pequena `Assets/Scenes/Prototype.unity`. Este é um experimento de protótipo; não fixa a câmera ou os controles finais do jogo.

## Objetivos

- Mover a cápsula da cena com WASD.
- Usar o mouse para orientar a câmera ao redor do personagem.
- Manter o personagem no chão e impedir que atravesse o piso.
- Usar o `CharacterController` e o Input System já presentes no projeto.

## Fora deste recorte

- Pulo, corrida, combate, poderes, inimigos, animações e interface.
- Rede ou sincronização multiplayer. O limite de até cinco jogadores continua como requisito do projeto, mas este teste é local.
- Decidir a câmera e os controles finais.

## Abordagens consideradas

- **CharacterController — escolhido para o teste.** Dá controle direto e previsível para caminhar e colidir com o chão, com poucos ajustes de física.
- **Rigidbody — não escolhido para este teste.** É adequado quando forças e reações físicas são centrais, mas exige mais ajuste para obter controles de personagem previsíveis.

## Escolhas confirmadas nesta conversa

- WASD + mouse como controle do primeiro teste.
- `CharacterController` para o movimento da cápsula.

## Proposta de implementação do experimento

- WASD move o personagem em relação à direção horizontal da câmera.
- O mouse gira a câmera em torno do personagem e altera seu ângulo vertical.
- O personagem vira para acompanhar a direção do movimento.
- O `CharacterController` responde à gravidade e às colisões com a cena.
- Pulo fica fora desta primeira versão para manter o teste focado em caminhar e olhar.
- Este teste é local e não inclui sincronização de rede; o requisito de multijogador do projeto permanece para uma etapa posterior.
- Velocidade, sensibilidade e limites verticais da câmera são ajustes do protótipo, não decisões de design final.

## Restrições e estado atual

- Projeto Unity `6000.3.28f1`, Universal 3D (URP), com Input System `1.20.1`.
- A cena `Prototype` contém o chão, uma cápsula e uma câmera. O asset `InputSystem_Actions.inputactions` já tem ações `Move` e `Look` com bindings de teclado/mouse e gamepad.
- VS Code está selecionado como editor externo no Unity.

## Critério para avaliar o experimento

Em Play Mode, o jogador consegue caminhar pelo piso usando WASD, orientar a câmera com o mouse e permanecer apoiado no chão. O ajuste de velocidade e câmera será decidido pela sensação do teste.

## Chamadas deixadas ao implementador

Escolher valores iniciais de velocidade, sensibilidade, suavização e limite vertical que permitam testar sem travar a câmera. Registrar os valores finais se forem aprovados para o projeto.
