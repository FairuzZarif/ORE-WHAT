// Outline for world pickups (PickupHighlight). Used by two materials on the pickup's outline renderer:
//   PickupOutlineMask  (queue 3000): writes the item's exact silhouette into the stencil buffer, draws nothing
//   PickupOutline      (queue 3001): draws the item's shape pushed outward by Outline Width pixels,
//                                    only where the mask isn't -> a thin ring around the silhouette.
// ZTest Always so the ring stays visible through grass; PickupHighlighter only turns it on when the
// item is in line of sight, so it never shows through walls or rocks. The item's own materials are untouched.
// Normals are smoothed when the outline mesh is baked (PickupHighlightSetupTool), so hard edges don't crack.
Shader "Ore What/Pickup Outline"
{
    Properties
    {
        [HDR] _OutlineColor ("Outline Color", Color) = (1, 0.75, 0.2, 1)
        _OutlineWidth ("Outline Width (pixels)", Float) = 3
        [IntRange] _StencilRef ("Stencil Bit", Range(1, 255)) = 64
        [Enum(UnityEngine.Rendering.CompareFunction)] _StencilComp ("Stencil Compare", Float) = 6 // NotEqual
        [Enum(UnityEngine.Rendering.StencilOp)] _StencilPass ("Stencil Pass", Float) = 0          // Keep
        _ColorMask ("Color Mask", Float) = 15
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent+1" "IgnoreProjector" = "True" }
        Pass
        {
            Name "PickupOutline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Off
            ZWrite Off
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha
            ColorMask [_ColorMask]
            Stencil
            {
                Ref [_StencilRef]
                ReadMask [_StencilRef]
                WriteMask [_StencilRef]
                Comp [_StencilComp]
                Pass [_StencilPass]
            }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _OutlineColor;
                float _OutlineWidth;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float4 positionCS = TransformObjectToHClip(input.positionOS.xyz);
                // Push each vertex outward on screen along its (smoothed) normal: a constant width in pixels.
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                float2 normalCS = mul((float3x3)GetWorldToHClipMatrix(), normalWS).xy;
                float len = length(normalCS);
                float2 dir = len > 1e-5 ? normalCS / len : float2(0, 0);
                positionCS.xy += dir * (_OutlineWidth * 2.0 / _ScreenParams.xy) * positionCS.w;
                output.positionCS = positionCS;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                return _OutlineColor;
            }
            ENDHLSL
        }
    }
}
