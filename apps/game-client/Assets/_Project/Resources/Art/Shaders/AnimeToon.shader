// Two-tone anime shading for the VRoid avatars (built-in render pipeline): the texture is lit where the sun
// reaches and tinted by the material's shade colour where it doesn't (like MToon, much cheaper), plus a faint
// rim light. Alpha cut-out for eyelashes, hair tips and lace; drawn double-sided because VRoid clothes and
// hair are single sheets. Skin materials multiply in the player's skin tone (_SkinTint, set per renderer).
// It lives in Resources so it is always in the build (nothing else references it).
Shader "HappyWorld/AnimeToon"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _ShadeColor ("Shade colour", Color) = (0.8, 0.75, 0.8, 1)
        _Cutoff ("Alpha cut-off", Range(0, 1)) = 0.5
        _IsSkin ("Is skin", Float) = 0
        _SkinTint ("Skin tint", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags { "Queue" = "AlphaTest" "RenderType" = "TransparentCutout" "IgnoreProjector" = "True" }
        LOD 150
        Cull Off

        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma target 3.0
            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _ShadeColor;
            fixed _Cutoff;
            fixed _IsSkin;
            fixed4 _SkinTint;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normal : TEXCOORD1;
                float3 view : TEXCOORD2;
                half3 ambient : TEXCOORD3;
                UNITY_FOG_COORDS(4)
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.view = WorldSpaceViewDir(v.vertex);
                o.ambient = ShadeSH9(half4(o.normal, 1));
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag (v2f i, fixed facing : VFACE) : SV_Target
            {
                fixed4 tex = tex2D(_MainTex, i.uv);
                clip(tex.a - _Cutoff);
                tex.rgb *= lerp(fixed3(1, 1, 1), _SkinTint.rgb, _IsSkin);
                float3 n = normalize(i.normal) * (facing > 0 ? 1 : -1);
                float ndl = dot(n, normalize(_WorldSpaceLightPos0.xyz)) * 0.5 + 0.5;
                // soft-edged two tones: lit above ~0.45, shade colour below
                fixed lit = smoothstep(0.38, 0.52, ndl);
                fixed3 albedo = tex.rgb * lerp(_ShadeColor.rgb, fixed3(1, 1, 1), lit);
                // sunlight and the sky's ambient both colour the character (dusk and night in the city show on it)
                half3 light = saturate(_LightColor0.rgb * 0.75 + i.ambient * 0.6 + 0.08);
                fixed rim = pow(1 - saturate(dot(n, normalize(i.view))), 4) * 0.18;
                fixed4 col = fixed4(albedo * light + rim * _LightColor0.rgb, 1);
                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
            ENDCG
        }
    }
    Fallback "Legacy Shaders/Transparent/Cutout/Diffuse"
}
