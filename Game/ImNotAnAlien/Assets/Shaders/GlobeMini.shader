// Miniaturas, nuvens e pedestal do globo do mapa (Missão 20): cor de vértice (pintada no Blender) com
// a mesma luz presa à câmera do globo (ImNotAnAlien/Globe Relief). Aceita transparência (nuvens).
Shader "ImNotAnAlien/Globe Mini"
{
    Properties
    {
        [MainColor] _BaseColor("Cor", Color) = (1, 1, 1, 1)
        _Ambient("Sombra mínima", Range(0, 1)) = 0.6
        _RimStrength("Brilho na borda", Range(0, 1)) = 0.12
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite On

        Pass
        {
            Name "GlobeMini"
            Tags { "LightMode" = "UniversalForwardOnly" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _Ambient;
                half _RimStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float3 normalVS : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color * _BaseColor;
                output.normalVS = mul((float3x3)UNITY_MATRIX_V, TransformObjectToWorldNormal(input.normalOS));
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 n = normalize(input.normalVS);
                float3 l = normalize(float3(-0.55, 0.65, 0.5));
                half light = lerp(_Ambient, 1.15, saturate(dot(n, l)));
                half rim = pow(1.0 - saturate(n.z), 3.0) * _RimStrength;
                return half4(input.color.rgb * light + rim, input.color.a);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
