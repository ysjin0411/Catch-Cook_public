Shader "Sushi2/Weather/Moon Reflection"
{
    Properties
    {
        [HDR] _Color("Moon Reflection Color", Color)=(0.9,0.8,0.55,1)
        _Intensity("Reflection Strength",Range(0,2))=.3
        _Speed("Ripple Speed",Range(0,2))=.3
    }
    SubShader
    {
        Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent+10"}
        Pass
        {
            Tags {"LightMode"="SRPDefaultUnlit"}
            Blend SrcAlpha One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            float _Intensity,_Speed;
            CBUFFER_END
            struct Attributes {float4 positionOS:POSITION;float2 uv:TEXCOORD0;};
            struct Varyings {float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0;};

            /// <summary>수면 위 반사 메시를 투영합니다.</summary>
            Varyings Vert(Attributes input)
            {
                // 실제 수평면의 깊이와 UV를 유지합니다.
                Varyings output;
                output.positionCS=TransformObjectToHClip(input.positionOS.xyz);
                output.uv=input.uv;
                return output;
            }

            /// <summary>길이가 다른 가느다란 물결에 달빛을 분산합니다.</summary>
            half4 Frag(Varyings input):SV_Target
            {
                // 규칙적인 사다리 모양 대신 여러 주기의 잔물결을 중첩합니다.
                float2 uv=input.uv;
                float time=_Time.y*_Speed;
                float wave=sin(uv.y*510+sin(uv.y*81)*3+time*2);
                float detail=sin(uv.y*937-uv.x*19-time)*.5+.5;
                float ripple=pow(saturate(wave),7)*(.25+.75*detail);
                // 반사의 중심과 폭을 물결마다 조금씩 달리합니다.
                float x=uv.x*2-1;
                x+=sin(uv.y*211+time)*.13;
                float width=.15+.20*(sin(uv.y*173)*.5+.5);
                float spread=exp(-x*x/max(.01,width*width));
                // 가까운 끝과 수평선 끝에서 빛이 자연스럽게 사라집니다.
                float fade=smoothstep(0,.22,uv.y)*(1-smoothstep(.97,1,uv.y));
                float glint=(ripple*.9+.035)*spread*fade;
                return half4(_Color.rgb,glint*_Intensity);
            }
            ENDHLSL
        }
    }
}
