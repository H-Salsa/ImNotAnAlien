// Globo do mapa (tecla M) com relevo 3D (Missão 20).
// A cor é a da textura pintada (como no Unlit: nada de luz ambiente lavando as cores), mas com uma
// luz própria vinda de cima/esquerda DA CÂMERA: quando o globo gira, as encostas viradas para a luz
// clareiam e as do outro lado escurecem — é isso que faz as montanhas "saltarem".
Shader "ImNotAnAlien/Globe Relief"
{
    Properties
    {
        [MainTexture] _BaseMap("Textura", 2D) = "white" {}
        [MainColor] _BaseColor("Cor", Color) = (1, 1, 1, 1)
        _LightStrength("Força da luz", Range(0, 1.5)) = 0.75
        _Ambient("Sombra mínima", Range(0, 1)) = 0.6
        _RimStrength("Brilho na borda", Range(0, 1)) = 0.18
        _SectorStrength("Divisas dos setores (zoom)", Range(0, 1)) = 0.15
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "GlobeForward"
            Tags { "LightMode" = "UniversalForwardOnly" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _LightStrength;
                half _Ambient;
                half _RimStrength;
                half _SectorStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalVS : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.normalVS = mul((float3x3)UNITY_MATRIX_V, normalWS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                // Alfa da textura = divisas dos setores (0 na linha): aparecem mais com zoom perto.
                half sector = (1.0 - tex.a) * _SectorStrength;
                half4 color = half4(lerp(tex.rgb, half3(1.0, 0.97, 0.9), sector * 0.55), 1.0) * _BaseColor;
                float3 n = normalize(input.normalVS);
                // Luz fixa em relação à câmera: de cima e da esquerda, um pouco de frente.
                float3 l = normalize(float3(-0.55, 0.65, 0.5));
                half diffuse = saturate(dot(n, l));
                half light = lerp(_Ambient, 1.0 + _LightStrength * 0.2, diffuse);
                half rim = pow(1.0 - saturate(n.z), 3.0) * _RimStrength;
                return half4(color.rgb * light + rim, 1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
