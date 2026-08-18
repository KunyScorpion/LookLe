using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace LeeyesViewer.Effects;

public class ColorAdjustEffect : ShaderEffect
{
    private static readonly PixelShader _pixelShader = new()
    {
        UriSource = new Uri("pack://application:,,,/LeeyesViewer;component/Effects/ColorAdjustEffect.ps", UriKind.Absolute)
    };

    public static readonly DependencyProperty InputProperty =
        RegisterPixelShaderSamplerProperty("Input", typeof(ColorAdjustEffect), 0);

    public static readonly DependencyProperty BrightnessProperty =
        DependencyProperty.Register("Brightness", typeof(double), typeof(ColorAdjustEffect),
            new UIPropertyMetadata(0.0, PixelShaderConstantCallback(0)));

    public static readonly DependencyProperty ContrastProperty =
        DependencyProperty.Register("Contrast", typeof(double), typeof(ColorAdjustEffect),
            new UIPropertyMetadata(1.0, PixelShaderConstantCallback(1)));

    public static readonly DependencyProperty GrayscaleProperty =
        DependencyProperty.Register("Grayscale", typeof(double), typeof(ColorAdjustEffect),
            new UIPropertyMetadata(0.0, PixelShaderConstantCallback(2)));

    public ColorAdjustEffect()
    {
        PixelShader = _pixelShader;
        UpdateShaderValue(InputProperty);
        UpdateShaderValue(BrightnessProperty);
        UpdateShaderValue(ContrastProperty);
        UpdateShaderValue(GrayscaleProperty);
    }

    public Brush Input
    {
        get => (Brush)GetValue(InputProperty);
        set => SetValue(InputProperty, value);
    }

    public double Brightness
    {
        get => (double)GetValue(BrightnessProperty);
        set => SetValue(BrightnessProperty, value);
    }

    public double Contrast
    {
        get => (double)GetValue(ContrastProperty);
        set => SetValue(ContrastProperty, value);
    }

    public double Grayscale
    {
        get => (double)GetValue(GrayscaleProperty);
        set => SetValue(GrayscaleProperty, value);
    }
}
