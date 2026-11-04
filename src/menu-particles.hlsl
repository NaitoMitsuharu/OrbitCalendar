sampler2D Input : register(s0);
float2 PixelSize : register(c0);
float Pattern : register(c1);

float4 main(float2 uv : TEXCOORD) : COLOR
{
    float4 ink = tex2D(Input, uv);
    float2 p = floor(uv * PixelSize);
    float2 parity = fmod(p, 2);
    float light = parity.x < .5 && parity.y < .5 ? 1.12 :
                  parity.x > .5 && parity.y > .5 ? .42 : .90;
    if (Pattern > .5 && Pattern < 1.5)
        light = fmod(p.x, 3) < .5 && fmod(p.y, 3) < .5 ? 1.15 : .42;
    if (Pattern > 1.5 && Pattern < 2.5)
        light = fmod(p.y, 3) < .5 ? 1.12 : .45;
    if (Pattern > 2.5)
        light = fmod(p.x * 17 + p.y * 31, 11) < 3 ? 1.3 : .66;
    // Keep the quiet navy surfaces; match AssemblyEffect's settled grains on
    // text, icons, outlines and controls, including live text and IME input.
    if (max(ink.r, max(ink.g, ink.b)) >= 61.0 / 255.0)
        ink.rgb = min(ink.rgb * light, ink.a);
    return ink;
}
