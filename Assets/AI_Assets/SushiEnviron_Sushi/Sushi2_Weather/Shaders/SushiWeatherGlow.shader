Shader "Sushi2/Weather/Glow And Water Glint"
{
    Properties
    {
        [HDR] _Color("Light Color", Color) = (0.6,0.7,1,1)
        _Intensity("Intensity", Range(0,3)) = 0.3
        _Reflection("Water Glint (0 = Soft Glow)", Range(0,1)) = 0
        _Speed("Ripple Speed", Range(0,3)) = 0.7
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent+10" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            float _Intensity, _Reflection, _Speed;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; };

            /// <summary>달무리 또는 수면 빛 Quad를 화면에 투영합니다.</summary>
            Varyings Vert(Attributes input)
            {
                // 원본 UV는 빛의 중심과 물결 모양 계산에 사용합니다.
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            /// <summary>부드러운 섬광 또는 움직이는 수면 반짝임을 계산합니다.</summary>
            half4 Frag(Varyings input):SV_Target
            {
                // 부드러운 원형 광량이 Quad 경계에서 완전히 사라지도록 합니다.
                float2 p = input.uv*2-1;
                float glow = pow(saturate(1-length(p)),2);
                // 세로 빛 기둥을 가로로 끊어 수면의 잔물결을 흉내 냅니다.
                float phase = input.uv.y*100 + sin(input.uv.y*31)*2 + _Time.y*_Speed;
                float waves = smoothstep(.35,.9,sin(phase));
                float width = .3 + .2*sin(input.uv.y*47+_Time.y*_Speed);
                float glint = (1-smoothstep(width,width+.2,abs(p.x))) * waves * pow(saturate(1-abs(p.y)),.6);
                // 머티리얼별 모드와 강도로 달무리·달빛·번개 반사를 구분합니다.
                float alpha = lerp(glow,glint,_Reflection) * _Intensity * _Color.a;
                return half4(_Color.rgb,alpha);
            }
            ENDHLSL
        }
    }
}
