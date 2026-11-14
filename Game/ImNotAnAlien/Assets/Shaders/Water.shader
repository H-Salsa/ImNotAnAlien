// Água transparente (Missão 20): mar, lago, rios e chafariz.
// Dá para ver o protagonista e os alienígenas nadando e o fundo (areia/leito) por baixo.
// - Cor com transparência: mais clara/transparente olhando de cima, mais forte de lado (fresnel).
// - Ondinhas: a normal balança com ondas que andam (posição LÓGICA: não pula ao recentralizar o mundo).
// - Brilho do sol nas ondas.
// - Entorta com o horizonte curvo como o resto do mundo.
Shader "ImNotAnAlien/Water"
{
    Properties
    {
        [MainColor] _BaseColor("Cor", Color) = (0.30, 0.70, 0.82, 1)
        _DeepColor("Cor de lado (fresnel)", Color) = (0.10, 0.38, 0.62, 1)
        _Clarity("Transparência olhando de cima", Range(0, 1)) = 0.55
        _EdgeAlpha("Opacidade de lado", Range(0, 1)) = 0.9
        _WaveScale("Tamanho das ondas (1/m)", Float) = 0.35
        _WaveSpeed("Velocidade das ondas", Float) = 1.4
        _Shimmer("Brilho que dança na água", Range(0, 1)) = 0.35
        _WaveStrength("Força das ondas", Range(0, 1)) = 0.45
        _Gloss("Brilho do sol", Range(8, 512)) = 140
        _SunStrength("Força do brilho", Range(0, 3)) = 1.2
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-10" "IgnoreProjector" = "True" }

        Pass
        {
            Name "WaterForward"
            Tags { "LightMode" = "UniversalForwardOnly" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _DeepColor;
                half _Clarity;
                half _EdgeAlpha;
                float _WaveScale;
                float _WaveSpeed;
                half _WaveStrength;
                half _Gloss;
                half _SunStrength;
                half _Shimmer;
            CBUFFER_END

            float4 _CurveOrigin;
            float _CurveStrength;
            float4 _GrassWorldOffset; // deslocamento da origem flutuante (posto pelo GrassField)

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float fog : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 p = TransformObjectToWorld(input.positionOS.xyz);
                output.positionWS = p;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                float2 o = p.xz - _CurveOrigin.xz;
                float3 curved = p;
                curved.y -= _CurveStrength * dot(o, o);
                output.positionCS = TransformWorldToHClip(curved);
                output.fog = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 logical = input.positionWS + _GrassWorldOffset.xyz;
                float t = _Time.y * _WaveSpeed;
                float2 q = logical.xz * _WaveScale;
                // Inclinação das ondas (derivadas de três ondas cruzadas).
                float2 slope = float2(cos(q.x * 1.0 + q.y * 0.3 + t), cos(q.y * 1.1 - q.x * 0.2 + t * 0.8)) * 0.5
                             + float2(cos(q.x * 2.3 - q.y * 1.7 - t * 1.3), cos(q.y * 2.1 + q.x * 1.4 + t * 1.1)) * 0.25;
                float3 n = normalize(input.normalWS);
                if (n.y > 0.5)
                {
                    n = normalize(float3(-slope.x * _WaveStrength, 1.0, -slope.y * _WaveStrength));
                }

                float3 view = normalize(GetWorldSpaceViewDir(input.positionWS));
                half fresnel = pow(1.0 - saturate(dot(n, view)), 3.0);
                Light light = GetMainLight();
                half3 lit = light.color * (saturate(dot(n, light.direction)) * 0.35 + 0.65) + SampleSH(n) * 0.25;
                half3 color = lerp(_BaseColor.rgb, _DeepColor.rgb, fresnel) * lit;
                half3 h = normalize(light.direction + view);
                color += light.color * pow(saturate(dot(n, h)), _Gloss) * _SunStrength;
                // Brilho que dança: duas redes de ondas cruzadas andando em direções diferentes; onde
                // as cristas se encontram aparecem linhas claras (como luz refletida na água mexendo).
                float2 c1 = logical.xz * 0.9 + float2(t * 0.35, t * 0.22);
                float2 c2 = logical.xz * 1.3 - float2(t * 0.18, t * 0.4);
                float web = abs(sin(c1.x + sin(c1.y * 1.3))) * abs(sin(c2.y + sin(c2.x * 1.1)));
                half shimmer = pow(saturate(1.0 - web), 6.0) * _Shimmer;
                color += shimmer * light.color * 0.6;
                half alpha = lerp(1.0 - _Clarity, _EdgeAlpha, fresnel) + shimmer * 0.35;
                color = MixFog(color, input.fog);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
