using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SS14.Launcher.Views;

public partial class ImageCropWindow : Window
{
    private readonly int _outputWidth;
    private readonly int _outputHeight;
    private Bitmap? _source;
    private byte[]? _sourceBytes;
    private int _sourceWidth;
    private int _sourceHeight;
    private bool _updatingTransform;

    public ImageCropWindow() => InitializeComponent();

    public ImageCropWindow(byte[] source, bool banner) : this()
    {
        if (!banner) source = TrimTransparentPadding(source);
        _sourceBytes = source;
        _source = new Bitmap(new MemoryStream(source));
        _sourceWidth = _source.PixelSize.Width;
        _sourceHeight = _source.PixelSize.Height;
        CropImage.Source = _source;
        _outputWidth = banner ? 1200 : 512;
        _outputHeight = banner ? 400 : 512;
        CropHost.Width = banner ? 720 : 420;
        CropHost.Height = banner ? 240 : 420;
        CropHost.CornerRadius = banner ? new CornerRadius(0) : new CornerRadius(210);
        EditorTitle.Text = banner ? "Настройте кадр баннера" : "Настройте кадр аватара";
        ZoomSlider.PropertyChanged += (_, _) => UpdateTransform();
        XSlider.PropertyChanged += (_, _) => UpdateTransform();
        YSlider.PropertyChanged += (_, _) => UpdateTransform();
    }

    private void UpdateTransform()
    {
        if (_updatingTransform || _sourceWidth == 0 || _sourceHeight == 0) return;
        _updatingTransform = true;
        var viewportWidth = CropHost.Width;
        var viewportHeight = CropHost.Height;
        var baseScale = Math.Max(viewportWidth / _sourceWidth, viewportHeight / _sourceHeight);
        CropImage.Width = _sourceWidth * baseScale;
        CropImage.Height = _sourceHeight * baseScale;
        var displayWidth = _sourceWidth * baseScale * ZoomSlider.Value;
        var displayHeight = _sourceHeight * baseScale * ZoomSlider.Value;
        var maxX = Math.Max(0, (displayWidth - viewportWidth) / 2);
        var maxY = Math.Max(0, (displayHeight - viewportHeight) / 2);
        XSlider.Minimum = -maxX; XSlider.Maximum = maxX;
        YSlider.Minimum = -maxY; YSlider.Maximum = maxY;
        XSlider.Value = Math.Clamp(XSlider.Value, -maxX, maxX);
        YSlider.Value = Math.Clamp(YSlider.Value, -maxY, maxY);
        ZoomValueText.Text = $"{ZoomSlider.Value * 100:0}%";
        CropImage.RenderTransform = new TransformGroup
        {
            Children =
            {
                new ScaleTransform(ZoomSlider.Value, ZoomSlider.Value),
                new TranslateTransform(XSlider.Value, YSlider.Value)
            }
        };
        _updatingTransform = false;
    }

    private void ResetClicked(object? sender, RoutedEventArgs e)
    { ZoomSlider.Value = 1; XSlider.Value = 0; YSlider.Value = 0; UpdateTransform(); }
    private void CancelClicked(object? sender, RoutedEventArgs e) => Close(null);
    private void TitleBarPressed(object? sender,PointerPressedEventArgs e){if(e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)BeginMoveDrag(e);}
    private void MinimizeClicked(object? sender,RoutedEventArgs e)=>WindowState=WindowState.Minimized;
    private void MaximizeClicked(object? sender,RoutedEventArgs e)=>WindowState=WindowState==WindowState.Maximized?WindowState.Normal:WindowState.Maximized;
    private void CloseWindowClicked(object? sender,RoutedEventArgs e)=>Close(null);
    private void ApplyClicked(object? sender, RoutedEventArgs e)
    {
        if (_sourceBytes == null) { Close(null); return; }
        var viewportWidth = CropHost.Width;
        var viewportHeight = CropHost.Height;
        var scale = Math.Max(viewportWidth / _sourceWidth, viewportHeight / _sourceHeight) * ZoomSlider.Value;
        var cropWidth = Math.Min(_sourceWidth, viewportWidth / scale);
        var cropHeight = Math.Min(_sourceHeight, viewportHeight / scale);
        var centerX = _sourceWidth / 2d - XSlider.Value / scale;
        var centerY = _sourceHeight / 2d - YSlider.Value / scale;
        var left = Math.Clamp(centerX - cropWidth / 2, 0, _sourceWidth - cropWidth);
        var top = Math.Clamp(centerY - cropHeight / 2, 0, _sourceHeight - cropHeight);
        var rectangle = new SixLabors.ImageSharp.Rectangle(
            (int)Math.Round(left), (int)Math.Round(top),
            Math.Max(1, (int)Math.Round(cropWidth)), Math.Max(1, (int)Math.Round(cropHeight)));
        if (rectangle.Right > _sourceWidth) rectangle.Width = _sourceWidth - rectangle.X;
        if (rectangle.Bottom > _sourceHeight) rectangle.Height = _sourceHeight - rectangle.Y;
        using var image = SixLabors.ImageSharp.Image.Load<Rgba32>(_sourceBytes);
        image.Mutate(x => x.Crop(rectangle).Resize(_outputWidth, _outputHeight));
        // Profile images must be fully opaque. Transparent padding otherwise reveals the
        // avatar control background and looks like empty strips at the sides of the circle.
        for (var y = 0; y < image.Height; y++) for (var x = 0; x < image.Width; x++)
        {
            var pixel = image[x, y];
            if (pixel.A == 255) continue;
            var alpha = pixel.A / 255f;
            pixel.R = (byte)Math.Round(pixel.R * alpha + 24 * (1 - alpha));
            pixel.G = (byte)Math.Round(pixel.G * alpha + 24 * (1 - alpha));
            pixel.B = (byte)Math.Round(pixel.B * alpha + 24 * (1 - alpha));
            pixel.A = 255;
            image[x, y] = pixel;
        }
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        Close(stream.ToArray());
    }

    protected override void OnClosed(System.EventArgs e) { _source?.Dispose(); base.OnClosed(e); }

    private static byte[] TrimTransparentPadding(byte[] source)
    {
        using var image = SixLabors.ImageSharp.Image.Load<Rgba32>(source);
        var left=image.Width;var top=image.Height;var right=-1;var bottom=-1;
        for(var y=0;y<image.Height;y++) for(var x=0;x<image.Width;x++)
        {
            if(image[x,y].A<=24)continue;
            if(x<left)left=x;if(x>right)right=x;if(y<top)top=y;if(y>bottom)bottom=y;
        }
        if(right<left||bottom<top||(left==0&&top==0&&right==image.Width-1&&bottom==image.Height-1))return source;
        image.Mutate(x=>x.Crop(new SixLabors.ImageSharp.Rectangle(left,top,right-left+1,bottom-top+1)));
        using var output=new MemoryStream();image.SaveAsPng(output);return output.ToArray();
    }
}
