// Planetas no céu (Missão 20): os planetas do Ultimate Space Kit lá no alto, longe.
// - Sem névoa (senão sumiriam no horizonte) e sem horizonte curvo (estão "no espaço").
// - Luz do sol de verdade (lado iluminado e lado escuro) + um pouco da luz do céu no lado escuro.
// - "Haze": mistura com a cor do céu para parecer longe (os menores ficam mais apagados).
// - Borda com brilho de atmosfera (cor por planeta).
Shader "ImNotAnAlien/Sky Planet"
{
    Properties
    {
        [MainTexture] _BaseMap("Textura (atlas)", 2D) = "white" {}
        _Haze("Apagado pela distância", Range(0, 1)) = 0.25
        _HazeColor("Cor do céu", Color) = (0.62, 0.72, 0.88, 1)
        _RimColor("Brilho da atmosfera", Color) = (0.6, 0.8, 1, 1)
        _RimStrength("Força do brilho", Range(0, 2)) = 0.6
        _Dark("Lado escuro (mínimo)", Range(0, 1)) = 0.35
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+10" }

        Pass
        {
            Name "SkyPlanetForward"
            Tags { "LightMode" = "UniversalForwardOnly" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half _Haze;
                half4 _HazeColor;
                half4 _RimColor;
                half _RimStrength;
                half _Dark;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb;
                float3 n = normalize(input.normalWS);
                float3 v = normalize(GetWorldSpaceViewDir(input.positionWS));
                Light sun = GetMainLight();
                half diffuse = saturate(dot(n, sun.direction));
                half3 lit = albedo * (sun.color * diffuse + SampleSH(n) * _Dark);
                half rim = pow(1.0 - saturate(dot(n, v)), 3.0) * _RimStrength;
                lit += _RimColor.rgb * rim * (0.35 + 0.65 * diffuse);
                lit = lerp(lit, _HazeColor.rgb, _Haze);
                return half4(lit, 1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
