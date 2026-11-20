using Godot;

namespace Otchlan.View;

/// <summary>Shadery 2D: błysk trafienia i przebarwienie wrogów pod krainę (zachowuje kontur i biel).</summary>
public static class Shaders
{
    private static Shader _tint;

    /// <summary>
    /// hue: przesunięcie barwy 0..1, sat/val: mnożniki nasycenia i jasności, flash: 0..1 biały błysk.
    /// Piksele prawie czarne (kontur) i prawie białe (oczy, połysk) nie zmieniają barwy.
    /// </summary>
    public static Shader Tint => _tint ??= new Shader
    {
        Code = @"
shader_type canvas_item;
uniform float hue = 0.0;
uniform float sat = 1.0;
uniform float val = 1.0;
uniform float flash = 0.0;
uniform float alpha_mul = 1.0;

vec3 rgb2hsv(vec3 c) {
    vec4 K = vec4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
    vec4 p = mix(vec4(c.bg, K.wz), vec4(c.gb, K.xy), step(c.b, c.g));
    vec4 q = mix(vec4(p.xyw, c.r), vec4(c.r, p.yzx), step(p.x, c.r));
    float d = q.x - min(q.w, q.y);
    float e = 1.0e-10;
    return vec3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
}
vec3 hsv2rgb(vec3 c) {
    vec4 K = vec4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
    vec3 p = abs(fract(c.xxx + K.xyz) * 6.0 - K.www);
    return c.z * mix(K.xxx, clamp(p - K.xxx, 0.0, 1.0), c.y);
}
void fragment() {
    vec4 c = texture(TEXTURE, UV) * COLOR;
    vec3 hsv = rgb2hsv(c.rgb);
    // tylko kolorowe, średnio jasne piksele: kontur i biel bez zmian
    float colorful = smoothstep(0.12, 0.3, hsv.y) * smoothstep(0.12, 0.3, hsv.z);
    vec3 shifted = hsv2rgb(vec3(fract(hsv.x + hue), clamp(hsv.y * sat, 0.0, 1.0), clamp(hsv.z * val, 0.0, 1.0)));
    c.rgb = mix(c.rgb, shifted, colorful);
    c.rgb = mix(c.rgb, vec3(1.0), flash);
    c.a *= alpha_mul;
    COLOR = c;
}",
    };

    public static ShaderMaterial NewTint(float hue = 0, float sat = 1, float val = 1)
    {
        var m = new ShaderMaterial { Shader = Tint };
        m.SetShaderParameter("hue", hue);
        m.SetShaderParameter("sat", sat);
        m.SetShaderParameter("val", val);
        return m;
    }
}
