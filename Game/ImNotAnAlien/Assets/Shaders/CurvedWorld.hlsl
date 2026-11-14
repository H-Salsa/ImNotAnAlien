#ifndef IMNOTANALIEN_CURVED_WORLD_INCLUDED
#define IMNOTANALIEN_CURVED_WORLD_INCLUDED

// Horizonte curvo do planeta (Missão 19).
// O chão "cai" conforme a distância do jogador, como num planetinha: queda = força × distância².
// Só muda o desenho (a física continua plana). Valores globais postos pelo CurvedHorizon.cs:
//   _CurveOrigin   posição do jogador (MUNDO)
//   _CurveStrength 0 = plano; 1 / (2 × raio) = curva de uma esfera com esse raio
float4 _CurveOrigin;
float _CurveStrength;

float3 CurveObjectPosition(float3 positionOS)
{
    float3 positionWS = TransformObjectToWorld(positionOS);
    float2 offset = positionWS.xz - _CurveOrigin.xz;
    positionWS.y -= _CurveStrength * dot(offset, offset);
    return TransformWorldToObject(positionWS);
}

#endif
