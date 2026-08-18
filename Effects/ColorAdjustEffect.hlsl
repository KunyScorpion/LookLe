sampler2D inputSampler : register(s0);
float Brightness : register(c0);
float Contrast : register(c1);
float Grayscale : register(c2);

float4 main(float2 uv : TEXCOORD) : COLOR
{
    float4 color = tex2D(inputSampler, uv);
    
    // 1. Grayscale conversion
    float gray = dot(color.rgb, float3(0.299, 0.587, 0.114));
    color.rgb = lerp(color.rgb, float3(gray, gray, gray), Grayscale);
    
    // 2. Contrast adjustment
    color.rgb = (color.rgb - 0.5) * Contrast + 0.5;
    
    // 3. Brightness adjustment
    color.rgb += Brightness;
    
    color.rgb = saturate(color.rgb);
    return color;
}
