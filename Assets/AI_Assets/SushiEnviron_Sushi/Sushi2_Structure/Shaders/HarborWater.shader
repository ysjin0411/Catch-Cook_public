Shader "Sushi2/HarborWater"
{
    Properties
    {
        _BaseColor("Deep Water", Color) = (0.025,0.12,0.17,1)
        _CrestColor("Wave Crest", Color) = (0.14,0.32,0.36,1)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _CrestColor;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; };
            struct Varyings { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; half fog:TEXCOORD1; };
            // 월드 좌표의 실제 수면을 카메라 원근에 맞춰 투영합니다.
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS=TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS=TransformWorldToHClip(output.positionWS);
                output.fog=ComputeFogFactor(output.positionCS.z);
                return output;
            }
            // 시간에 따라 움직이는 잔물결과 방향광 반사를 계산합니다.
            half4 Frag(Varyings input):SV_Target
            {
                float2 p=input.positionWS.xz;
                float t=_Time.y;
                float a=sin(p.x*1.7+p.y*2.4+t*.9+sin(p.y*.72)*1.8);
                float b=sin(p.x*3.3-p.y*1.1-t*.65+sin(p.x*.45+p.y*.39));
                float3 normal=normalize(float3(a*.16,1,b*.12));
                Light sun=GetMainLight();
                float3 view=normalize(GetWorldSpaceViewDir(input.positionWS));
                float spec=pow(saturate(dot(normal,normalize(view+sun.direction))),100);
                float fresnel=pow(1-saturate(dot(normal,view)),3);
                float ripple=smoothstep(.91,1,(a+b)*.25+.5);
                half3 col=lerp(_BaseColor.rgb,_CrestColor.rgb,fresnel*.38+ripple*.012);
                col+=sun.color*spec*.008;
                return half4(MixFog(col,input.fog),1);
            }
            ENDHLSL
        }
    }
}
