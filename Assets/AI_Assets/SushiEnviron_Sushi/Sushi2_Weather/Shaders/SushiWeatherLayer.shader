Shader "Sushi2/Weather/Transparent Layer"
{
    Properties
    {
        _BaseMap("Texture (RGBA)", 2D) = "white" {}
        _BaseColor("Tint", Color) = (1,1,1,1)
        _Opacity("Opacity", Range(0,1)) = 1
        _Brightness("Brightness", Range(0,4)) = 1
        _EdgeFade("Border Feather", Range(0.001,0.2)) = 0.03
        _FlashColor("Cloud Flash Color", Color) = (0.55,0.65,1,1)
        _FlashStrength("Cloud Flash Strength", Range(0,4)) = 0
        _FlashCenter("Flash Center (UV)", Vector) = (0.65,0.5,0,0)
        _FlashRadius("Flash Radius", Range(0.01,1)) = 0.3
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST, _FlashCenter;
            half4 _BaseColor, _FlashColor;
            float _Opacity, _Brightness, _EdgeFade, _FlashStrength, _FlashRadius;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; };

            /// <summary>독립적인 달·구름·번개 Quad를 투영합니다.</summary>
            Varyings Vert(Attributes input)
            {
                // 양면 Quad에 텍스처의 타일링과 오프셋을 적용합니다.
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv,_BaseMap);
                return output;
            }

            /// <summary>투명 이미지에 날씨 색상과 국소적인 구름 섬광을 적용합니다.</summary>
            half4 Frag(Varyings input):SV_Target
            {
                // PNG의 원래 투명도를 유지한 채 색상과 밝기를 조절합니다.
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,input.uv);
                half3 color = tex.rgb * _BaseColor.rgb * _Brightness;
                // 가장자리를 부드럽게 지워 배경에 사각형 경계가 남지 않도록 합니다.
                float2 edge = min(input.uv,1-input.uv);
                float fade = smoothstep(0,max(_EdgeFade,.001),min(edge.x,edge.y));
                // 섬광은 선택한 UV 부근의 구름에만 더해집니다.
                float flash = 1-smoothstep(0,_FlashRadius,distance(input.uv,_FlashCenter.xy));
                color += _FlashColor.rgb * _FlashStrength * flash;
                return half4(color,tex.a * _BaseColor.a * _Opacity * fade);
            }
            ENDHLSL
        }
    }
}
