Shader "Sushi2/Weather/Moon Surface"
{
    Properties
    {
        _Color("Moonlight Color", Color) = (1,0.95,0.82,1)
        _Brightness("Luminous Surface", Range(0,3)) = 1.1
        _PatchStrength("Lunar Markings", Range(0,0.4)) = 0.12
        _LunarMap("Lunar Surface", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Back
            ZWrite On
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            float _Brightness,_PatchStrength;
            CBUFFER_END
            TEXTURE2D(_LunarMap);
            SAMPLER(sampler_LunarMap);
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; };
            struct Varyings { float4 positionCS:SV_POSITION; float3 normalOS:TEXCOORD0; };

            /// <summary>달 구체의 위치와 구면 표면 방향을 전달합니다.</summary>
            Varyings Vert(Attributes input)
            {
                // 달은 배경 구체로 투영하며 원래 법선으로 표면 무늬를 계산합니다.
                Varyings output;
                output.positionCS=TransformObjectToHClip(input.positionOS.xyz);
                output.normalOS=input.normalOS;
                return output;
            }

            /// <summary>투명도를 낮추지 않고 구체 표면 자체를 밝게 표시합니다.</summary>
            half4 Frag(Varyings input):SV_Target
            {
                // 여러 공간 주파수를 합쳐 단순한 달 표면 명암을 만듭니다.
                float3 n=normalize(input.normalOS);
                float broad=sin(n.x*11+n.y*7)*sin(n.y*9-n.z*8);
                float fine=sin(n.x*27-n.z*19)*sin(n.y*23+n.z*13);
                float markings=smoothstep(-.2,.65,broad*.7+fine*.3);
                // 원형 윤곽은 구체 메시가 만들고 색과 밝기는 주변 가게 조명과 독립적으로 유지합니다.
                // 구체 앞면에 달의 표면 지형을 투영하고 흰색으로 포화되지 않게 합니다.
                float2 uv=float2(n.x,n.y)*.36+.5;
                half3 lunar=SAMPLE_TEXTURE2D(_LunarMap,sampler_LunarMap,uv).rgb;
                float shading=.65+.35*saturate(dot(n,normalize(float3(-.4,.55,-1))));
                half3 color=_Color.rgb*_Brightness*lunar*shading*(1-markings*_PatchStrength);
                return half4(color,1);
            }
            ENDHLSL
        }
    }
}
