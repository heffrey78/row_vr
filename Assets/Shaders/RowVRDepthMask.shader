// Keeps the river out of the raft (#90). A lid over the raft's opening, at the tubes' top,
// that writes depth and no colour. The raft itself draws first (queue 1998), then this
// (Geometry-1), then the river (Geometry): a standing wave passing under the hull is behind
// the lid, so it never shows through the floor, while the floor, already drawn, stays.
//
// Written for single-pass instanced stereo on Quest: without the instancing and stereo
// macros below, a custom shader renders into the left eye only.
Shader "RowVR/DepthMask"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry-1" }

        Pass
        {
            Name "DepthMask"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return 0;
            }
            ENDHLSL
        }
    }
}
