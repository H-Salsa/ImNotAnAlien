// Efeitos dos poderes elementais (Missão 21): fogo, gelo e eletricidade.
// Um shader só para tudo que brilha: partículas, bolas de fogo, cristais de gelo e raios.
// - Sem luz (a cor é a que o jogo manda), com a cor do vértice (as partículas mudam de cor ao longo da vida).
// - "Borda": brilho no contorno de objetos 3D (cristal de gelo, miolo da bola de fogo). 0 = desligado.
// - Mistura escolhida pelo material: aditiva (fogo, raio, brilho) ou transparente comum (fumaça, gelo).
// - Some na névoa do jogo como o resto do mundo.
// Sem "keywords": na build não há variação para o Unity cortar (está na lista do BuildShadersSetup).
Shader "ImNotAnAlien/Elemental FX"
{
    Properties
    {
        [MainTexture] _BaseMap("Textura", 2D) = "white" {}
        [HDR] _Color("Cor", Color) = (1, 1, 1, 1)
        [HDR] _RimColor("Cor da borda", Color) = (0, 0, 0, 0)
        _RimPower("Borda (0 = sem)", Range(0, 8)) = 0
        _SrcBlend("Mistura (origem)", Float) = 5
        _DstBlend("Mistura (destino)", Float) = 1
        _ZWrite("Escreve profundidade", Float) = 0
        _Cull("Faces escondidas", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "ElementalForward"
            Tags { "LightMode" = "UniversalForwardOnly" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _Color;
                half4 _RimColor;
                half _RimPower;
                half _SrcBlend;
                half _DstBlend;
                half _ZWrite;
                half _Cull;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 viewWS : TEXCOORD2;
                float fog : TEXCOORD3;
            };

            Varyings Vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.color = input.color;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.viewWS = GetWorldSpaceViewDir(positionWS);
                output.fog = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 color = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _Color * input.color;
                if (_RimPower > 0.001)
                {
                    half facing = abs(dot(normalize(input.normalWS), normalize(input.viewWS)));
                    half rim = pow(saturate(1.0 - facing), _RimPower);
                    color.rgb += _RimColor.rgb * rim * input.color.a;
                    color.a = saturate(color.a + rim * _RimColor.a * input.color.a);
                }

                // Névoa do jogo: o brilho (mistura aditiva) apaga para o preto; fumaça e gelo tomam a cor da névoa.
                half3 fogTarget = _DstBlend < 1.5 ? half3(0.0, 0.0, 0.0) : unity_FogColor.rgb;
                color.rgb = MixFogColor(color.rgb, fogTarget, input.fog);
                return color;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
