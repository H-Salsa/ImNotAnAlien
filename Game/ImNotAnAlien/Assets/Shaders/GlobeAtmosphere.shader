// Brilho de atmosfera em volta do globo do mapa (Missão 20): esfera um pouco maior que o globo, só a
// borda acende (quanto mais de lado, mais forte), somando luz azulada.
Shader "ImNotAnAlien/Globe Atmosphere"
{
    Properties
    {
        [MainColor] _BaseColor("Cor", Color) = (0.45, 0.75, 1, 1)
        _Power("Espessura (maior = mais fina)", Range(0.5, 8)) = 3
        _Intensity("Intensidade", Range(0, 3)) = 1.1
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+20" }
        Blend One One
        ZWrite Off
        Cull Back

        Pass
        {
            Name "GlobeAtmosphere"
            Tags { "LightMode" = "UniversalForwardOnly" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _Power;
                half _Intensity;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalVS : TEXCOORD0; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalVS = mul((float3x3)UNITY_MATRIX_V, TransformObjectToWorldNormal(input.normalOS));
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 n = normalize(input.normalVS);
                half edge = pow(1.0 - saturate(n.z), _Power) * _Intensity;
                return half4(_BaseColor.rgb * edge, 1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
