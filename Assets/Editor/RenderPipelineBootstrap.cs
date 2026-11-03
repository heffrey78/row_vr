using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace RowVR.Editor
{
    /// <summary>
    /// URP, configured for Quest from code (#40). Called from ProjectBootstrap.Apply, so
    /// make bootstrap and every CLI build apply it. Idempotent.
    ///
    /// Settings are lean on purpose: stereo at 72 Hz on a tiled mobile GPU. HDR and
    /// post-processing also stay off because either can write alpha 1 and block passthrough
    /// (#39).
    /// </summary>
    public static class RenderPipelineBootstrap
    {
        const string Directory = "Assets/Settings";
        const string AssetPath = Directory + "/Quest_URP.asset";
        const string RendererPath = Directory + "/Quest_Renderer.asset";

        public static void Apply()
        {
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(AssetPath);
            if (asset == null)
            {
                if (!AssetDatabase.IsValidFolder(Directory)) AssetDatabase.CreateFolder("Assets", "Settings");
                var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, RendererPath);
                asset = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(asset, AssetPath);
                Debug.Log("[RowVR] Created " + AssetPath);
            }

            Configure(asset);
            GraphicsSettings.defaultRenderPipeline = asset;
            // Quality levels keep no override of their own, so they all use the default.
            ConvertStandardMaterials();
            AssetDatabase.SaveAssets();
        }

        static void Configure(UniversalRenderPipelineAsset asset)
        {
            asset.msaaSampleCount = 4;
            asset.supportsHDR = false;
            asset.renderScale = 1f;
            asset.useSRPBatcher = true;
            asset.supportsCameraDepthTexture = false;
            asset.supportsCameraOpaqueTexture = false;
            asset.shadowDistance = 15f;
            asset.shadowCascadeCount = 1;

            // No public setters for these; go through the serialized fields and say so if a
            // URP upgrade renames one.
            var serialized = new SerializedObject(asset);
            Set(serialized, "m_MainLightShadowsSupported", true);
            Set(serialized, "m_SoftShadowsSupported", false);
            Set(serialized, "m_AdditionalLightsRenderingMode", 0);   // Disabled: no extra lights in the scene
            Set(serialized, "m_AdditionalLightShadowsSupported", false);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
        }

        static void Set(SerializedObject serialized, string name, bool value)
        {
            var p = serialized.FindProperty(name);
            if (p == null) Debug.LogWarning($"[RowVR] URP asset has no {name}; not set.");
            else p.boolValue = value;
        }

        static void Set(SerializedObject serialized, string name, int value)
        {
            var p = serialized.FindProperty(name);
            if (p == null) Debug.LogWarning($"[RowVR] URP asset has no {name}; not set.");
            else p.intValue = value;
        }

        /// <summary>
        /// Moves any material still on Built-in's Standard shader to URP Lit, keeping its
        /// colour, main texture and transparency. Under URP, Standard renders pink.
        /// </summary>
        static void ConvertStandardMaterials()
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null)
            {
                Debug.LogError("[RowVR] URP Lit shader not found; materials left on Standard.");
                return;
            }

            var converted = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null || material.shader == null || material.shader.name != "Standard") continue;

                var colour = material.HasProperty("_Color") ? material.GetColor("_Color") : Color.white;
                var texture = material.HasProperty("_MainTex") ? material.GetTexture("_MainTex") : null;
                // Standard's _Mode: 0 opaque, 1 cutout, 2 fade, 3 transparent.
                var transparent = material.HasProperty("_Mode") && material.GetFloat("_Mode") >= 2f;

                material.shader = lit;
                material.SetColor("_BaseColor", colour);
                if (texture != null) material.SetTexture("_BaseMap", texture);
                if (transparent) MakeTransparent(material);

                EditorUtility.SetDirty(material);
                converted.Add(path);
            }

            if (converted.Count > 0)
                Debug.Log("[RowVR] Converted to URP Lit:\n  " + string.Join("\n  ", converted));
        }

        static void MakeTransparent(Material material)
        {
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;
        }

        public static string Describe()
        {
            var sb = new StringBuilder();
            var active = GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
            sb.AppendLine("  render pipeline   : " + (active != null ? "URP (" + active.name + ")" : "Built-in") + " (want URP)");
            if (active != null)
            {
                sb.AppendLine("  URP MSAA / HDR    : " + active.msaaSampleCount + "x / " + (active.supportsHDR ? "on" : "off") + " (want 4x / off)");
                sb.AppendLine("  SRP Batcher       : " + (active.useSRPBatcher ? "on" : "off") + " (want on)");
            }
            return sb.ToString().TrimEnd();
        }
    }
}
