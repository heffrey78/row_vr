// Fades the scene out to the room (REQ-0005, ADR-0014). Drawn full screen, last, over everything but the
// cards, it multiplies the eye buffer's colour AND alpha by (1 - _Fade). Quest composites
// passthrough under the app wherever the app's alpha is below 1, so the room shows through in
// proportion. Scaling colour and alpha together keeps the premultiplied result correct.
Shader "RowVR/PassthroughFade"
{
    Properties
    {
        _Fade ("Fade", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+500" }

        Pass
        {
            Name "Fade"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZWrite Off
            ZTest Always
            Blend Zero OneMinusSrcAlpha, Zero OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _Fade;
            CBUFFER_END

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
                // Full screen in clip space, whatever the quad's transform: a unit quad's corners
                // (+-0.5) go to the corners of the view, in each eye. The quad's placement then
                // only has to keep it from being culled.
                output.positionCS = float4(input.positionOS.xy * 2.0, UNITY_NEAR_CLIP_VALUE, 1.0);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return half4(0, 0, 0, _Fade);
            }
            ENDHLSL
        }
    }
}
