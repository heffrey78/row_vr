// Unlit colour for the scenery layers (#39): texture x colour x vertex colour, with
// fog. A URP SubShader and a Built-in one; see the second for why both. One shader for the sky, the water and the shoreline keeps the scenery to one
// cheap pass, and it is referenced from the scene so a device build cannot strip it.
//
// _FogAmount is a plain float rather than a keyword: the sky sets it to 0 (the sky IS the
// horizon the fog fades to) and everything else leaves it at 1.
//
// Written for single-pass instanced stereo on Quest: without the instancing and stereo
// macros below, a custom shader renders into the left eye only.
Shader "RowVR/Unlit"
{
    Properties
    {
        _BaseMap ("Texture", 2D) = "white" {}
        _BaseColor ("Colour", Color) = (1, 1, 1, 1)
        _FogAmount ("Fog Amount", Range(0, 1)) = 1
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]
            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _FogAmount;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                // Clip-space z, not a fog factor: see Frag.
                float clipZ : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.color = input.color;
                output.clipZ = positions.positionCS.z;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 colour = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv) * _BaseColor * input.color;
                // Fog per pixel. URP's usual per-vertex fog factor is clamped at each vertex,
                // so across the water's long triangles it would ramp from the boat outward
                // instead of starting at the fog distance.
                half3 fogged = MixFog(colour.rgb, ComputeFogFactor(input.clipZ));
                // Opaque scenery writes alpha 1; where nothing draws, the clear's alpha 0
                // is what lets passthrough through (#39).
                return half4(lerp(colour.rgb, fogged, _FogAmount), 1);
            }
            ENDHLSL
        }
    }

    // The same shader for the Built-in pipeline, which is what this project actually renders
    // with: the URP package is installed but no URP pipeline asset is assigned. Unity picks the
    // first SubShader the active pipeline accepts, so the URP one above wins if URP is ever
    // switched on, and this one runs until then. Without it the scenery is silently skipped:
    // a SubShader rejected by pipeline tag draws nothing, not the pink error shader.
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }

        Pass
        {
            Cull [_Cull]
            ZWrite On

            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "UnityCG.cginc"

            sampler2D _BaseMap;
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _FogAmount;

            struct Attributes
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                float clipZ : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(Varyings, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.color = v.color;
                o.clipZ = o.pos.z;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                half4 colour = tex2D(_BaseMap, i.uv) * _BaseColor * i.color;
                half3 rgb = colour.rgb;
                // Fog per pixel, for the same reason as the URP pass. Built-in's own fog macros
                // compute the factor per vertex on mobile, which is exactly the Quest.
                #if defined(FOG_LINEAR) || defined(FOG_EXP) || defined(FOG_EXP2)
                    UNITY_CALC_FOG_FACTOR_RAW(UNITY_Z_0_FAR_FROM_CLIPSPACE(i.clipZ));
                    half3 fogged = lerp(unity_FogColor.rgb, rgb, saturate(unityFogFactor));
                    rgb = lerp(rgb, fogged, _FogAmount);
                #endif
                return half4(rgb, 1);
            }
            ENDCG
        }
    }
}
