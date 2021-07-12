Shader "Unlit/ConstantBuffersUnlit"
{
    Properties
    {
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float4 col : COLOR0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 col : COLOR0;
                float4 vertex : SV_POSITION;
            };
            
            #if defined(UNITY_INSTANCING_ENABLED)
                #if defined(SHADER_API_GLES3)
                    cbuffer UnityInstancing_Props{
                #else
                    CBUFFER_START(UnityInstancing_Props)
                #endif
                struct {
                    float4 _Color;
                    float4x4 _Matrix;
                    float4 _Color2;
                    float _Value;
                } PropsArray[UNITY_INSTANCED_ARRAY_SIZE];
                #if defined(SHADER_API_GLES3)
                    }
                #else
                    CBUFFER_END
                #endif
            #endif


            v2f vert (appdata v)
            {
                UNITY_SETUP_INSTANCE_ID(v);
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                #if defined(UNITY_INSTANCING_ENABLED)
                #if defined(SHADER_API_GLES3)
                o.col = float4(1, 0, 0, 1);
                #else
                o.col = PropsArray[unity_InstanceID]._Color;
                #endif
                #else
                o.col = float4(1, 1, 1, 1);
                #endif
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                return i.col;
            }
            ENDCG
        }
    }
}