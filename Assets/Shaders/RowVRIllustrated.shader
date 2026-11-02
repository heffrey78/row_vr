// The illustrated look (#42): stepped painterly shading from a fixed light, a paper
// grain fixed in world space, and an ink outline. No post-processing, no depth texture, no
// real-time lighting: all of it is per-material, which keeps it cheap on Quest and leaves
// alpha alone for passthrough (#39, #40).
//
// Two passes. "Illustrated" is the colour. "Ink" is the outline: the mesh inflated along
// smoothed normals (baked into TEXCOORD3 by IllustratedMesh, so hard-edged shapes do not
// crack at their corners) with its front faces culled, so only a rim shows around the
// shape. URP draws a SRPDefaultUnlit pass alongside UniversalForward, which is what makes
// the second pass run.
//
// Written for single-pass instanced stereo, like RowVR/Unlit.
Shader "RowVR/Illustrated"
{
    Properties
    {
        _BaseColor ("Colour", Color) = (1, 1, 1, 1)
        _ShadeColor ("Shade Tint", Color) = (0.62, 0.66, 0.8, 1)
        _InkColor ("Ink", Color) = (0.14, 0.12, 0.14, 1)
        _InkWidth ("Ink Width (m)", Float) = 0.006
        _Steps ("Tone Steps", Range(2, 4)) = 3
        _Softness ("Tone Edge Softness", Range(0.001, 0.2)) = 0.04
        _GrainTex ("Paper Grain", 2D) = "gray" {}
        _GrainScale ("Grain Scale (per m)", Float) = 1.6
        _GrainStrength ("Grain Strength", Range(0, 1)) = 0.35
        _GrainFade ("Grain Fade Distance (m)", Float) = 30
        _UseVertexColor ("Use Vertex Colour", Float) = 1
        _FogAmount ("Fog Amount", Range(0, 1)) = 1
        _LightDirection ("Painted Light Direction", Vector) = (-0.45, 0.8, -0.35, 0)
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_GrainTex);
        SAMPLER(sampler_GrainTex);

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _ShadeColor;
            half4 _InkColor;
            float _InkWidth;
            half _Steps;
            half _Softness;
            float4 _GrainTex_ST;
            float _GrainScale;
            half _GrainStrength;
            float _GrainFade;
            half _UseVertexColor;
            half _FogAmount;
            float4 _LightDirection;
            float _Cull;
        CBUFFER_END

        half3 ApplyFog(half3 colour, float clipZ)
        {
            // Per pixel, as in RowVR/Unlit: per-vertex fog clamps across long triangles.
            half3 fogged = MixFog(colour, ComputeFogFactor(clipZ));
            return lerp(colour, fogged, _FogAmount);
        }
        ENDHLSL

        Pass
        {
            Name "Illustrated"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]
            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                half4 color : COLOR;
                float clipZ : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.color = lerp(half4(1, 1, 1, 1), input.color, _UseVertexColor);
                output.clipZ = positions.positionCS.z;
                return output;
            }

            half4 Frag(Varyings input, bool frontFace : SV_IsFrontFace) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float3 n = normalize(input.normalWS) * (frontFace ? 1 : -1);
                float3 l = normalize(_LightDirection.xyz);

                // Stepped tone: light split into a few flat bands with soft painted edges.
                half t = saturate(dot(n, l) * 0.5 + 0.5);
                half steps = max(2, _Steps);
                half scaled = t * (steps - 1);
                half band = floor(scaled) + smoothstep(0.5 - _Softness, 0.5 + _Softness, frac(scaled));
                half lit = band / (steps - 1);

                half3 base = _BaseColor.rgb * input.color.rgb;
                half3 colour = lerp(base * _ShadeColor.rgb, base, lit);

                // Paper grain in world space, projected along the dominant normal axis, and
                // faded out with distance before it can shimmer.
                float3 w = input.positionWS * _GrainScale;
                float3 a = abs(n);
                half grain = a.y > a.x && a.y > a.z ? SAMPLE_TEXTURE2D(_GrainTex, sampler_GrainTex, w.xz).r
                           : a.x > a.z            ? SAMPLE_TEXTURE2D(_GrainTex, sampler_GrainTex, w.zy).r
                                                  : SAMPLE_TEXTURE2D(_GrainTex, sampler_GrainTex, w.xy).r;
                half distanceFade = saturate(1 - length(input.positionWS - GetCameraPositionWS()) / max(0.01, _GrainFade));
                colour *= 1 + (grain - 0.5) * _GrainStrength * distanceFade;

                return half4(ApplyFog(colour, input.clipZ), 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Ink"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float3 smoothNormalOS : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float clipZ : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                // Smoothed normals when baked, the mesh's own otherwise. Width is in world
                // metres, so the line thins with distance like a pen line in perspective.
                float3 normalOS = dot(input.smoothNormalOS, input.smoothNormalOS) > 0.01 ? input.smoothNormalOS : input.normalOS;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = normalize(TransformObjectToWorldNormal(normalOS));
                positionWS += normalWS * _InkWidth;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.clipZ = output.positionCS.z;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return half4(ApplyFog(_InkColor.rgb, input.clipZ), 1);
            }
            ENDHLSL
        }
    }
}
