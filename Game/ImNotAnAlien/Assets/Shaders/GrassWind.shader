// Grama de triângulos balançando ao vento (Missão 20).
// Cada folha é um triângulo fino: os dois vértices do pé ficam parados e a ponta balança.
// A malha é montada pelo GrassField.cs: cor de cada folha no vertex color (pé escuro, ponta clara),
// uv.x = fase sorteada da folha e uv.y = altura da folha em metros (0 nos vértices do pé).
// - Vento: ondas grandes e lentas que passam pelo campo + tremidinha rápida de cada folha.
// - Empurrão: a grama deita para longe do protagonista (_GrassPusher, posto pelo GrassField.cs).
// - Some ao longe (a ponta desce até o pé) e entorta com o horizonte curvo como o resto do mundo.
// - Não projeta sombra (economia), mas recebe a sombra das árvores e dos prédios.
Shader "ImNotAnAlien/Grass Wind"
{
    Properties
    {
        _WindStrength("Força do vento", Range(0, 1)) = 0.35
        _WindSpeed("Velocidade das ondas", Float) = 1.4
        _WindScale("Frequência das ondas (1/m)", Float) = 0.12
        _WindDirection("Direção do vento (X, _, Z)", Vector) = (1, 0, 0.35, 0)
        _Flutter("Tremido das folhas", Range(0, 0.4)) = 0.08
        _PushRadius("Raio do empurrão (m)", Float) = 1.5
        _PushStrength("Força do empurrão", Range(0, 2)) = 1.1
        _FadeStart("Começa a sumir (m)", Float) = 70
        _FadeEnd("Sumiu (m)", Float) = 105
        _Brightness("Brilho", Range(0.3, 2)) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float _WindStrength;
            float _WindSpeed;
            float _WindScale;
            float4 _WindDirection;
            float _Flutter;
            float _PushRadius;
            float _PushStrength;
            float _FadeStart;
            float _FadeEnd;
            float _Brightness;
        CBUFFER_END

        // Globais (GrassField.cs): xyz = pés do protagonista (MUNDO), w = 1 ligado / 0 desligado.
        float4 _GrassPusher;
        // Deslocamento da origem flutuante: o vento usa a posição LÓGICA para não "pular" ao recentralizar.
        float4 _GrassWorldOffset;

        #include "Assets/Shaders/CurvedWorld.hlsl"

        // Posição no MUNDO já com vento, empurrão e sumiço (sem a curva do horizonte).
        float3 GrassWorldPosition(float3 positionOS, float2 uv)
        {
            float3 p = TransformObjectToWorld(positionOS);
            float h = uv.y;
            float tip = step(0.0001, h);

            float3 logical = p + _GrassWorldOffset.xyz;
            float2 dir = normalize(_WindDirection.xz + float2(0.0001, 0.0));
            float t = _Time.y;
            float wave = sin(dot(logical.xz, dir) * _WindScale - t * _WindSpeed) * 0.5 + 0.5;
            float gust = sin(dot(logical.xz, float2(dir.y, -dir.x)) * _WindScale * 0.31 + t * 0.27) * 0.5 + 0.5;
            float flutter = sin(t * 5.3 + uv.x * 6.2831 + logical.x * 0.9 + logical.z * 0.7) * _Flutter;
            float2 offset = dir * (wave * (0.35 + 0.65 * gust) * _WindStrength + flutter) * h;

            // Empurrão: deita para longe dos pés do protagonista.
            float2 away = p.xz - _GrassPusher.xz;
            float d = length(away);
            float near = saturate(1.0 - d / max(_PushRadius, 0.01)) * step(abs(p.y - _GrassPusher.y), 2.5) * _GrassPusher.w;
            offset += away / max(d, 0.001) * near * _PushStrength * h;

            p.xz += offset * tip;
            float lean = saturate(length(offset) / max(h, 0.0001));
            p.y -= h * lean * lean * 0.6 * tip; // ao deitar, a ponta desce (a folha não "estica")

            float fade = saturate((distance(p, _WorldSpaceCameraPos) - _FadeStart) / max(_FadeEnd - _FadeStart, 0.01));
            p.y -= h * fade * tip;
            return p;
        }

        float3 CurveWorld(float3 p)
        {
            float2 o = p.xz - _CurveOrigin.xz;
            p.y -= _CurveStrength * dot(o, o);
            return p;
        }
        ENDHLSL

        Pass
        {
            Name "GrassForward"
            Tags { "LightMode" = "UniversalForwardOnly" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half4 color : COLOR;
                float fog : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 p = GrassWorldPosition(input.positionOS.xyz, input.uv);
                output.positionWS = p; // sombra na posição de verdade (sem a curva)
                output.positionCS = TransformWorldToHClip(CurveWorld(p));
                output.color = input.color;
                output.fog = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // Luz "de cima" igual para todas as folhas: visual de tapete, sem faces escuras.
                half3 normal = half3(0, 1, 0);
                Light light = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half wrap = saturate(dot(normal, light.direction)) * 0.6 + 0.4;
                half3 lit = light.color * wrap * light.shadowAttenuation + SampleSH(normal);
                half3 color = input.color.rgb * lit * _Brightness;
                color = MixFog(color, input.fog);
                return half4(color, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ColorMask R

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            float4 Vert(Attributes input) : SV_POSITION
            {
                return TransformWorldToHClip(CurveWorld(GrassWorldPosition(input.positionOS.xyz, input.uv)));
            }

            half Frag() : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            float4 Vert(Attributes input) : SV_POSITION
            {
                return TransformWorldToHClip(CurveWorld(GrassWorldPosition(input.positionOS.xyz, input.uv)));
            }

            half4 Frag() : SV_Target
            {
                return half4(0, 1, 0, 0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
