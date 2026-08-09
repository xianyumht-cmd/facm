using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace FACM.MachineCatPrototype;

internal sealed class MachineCatRig
{
    private readonly Grid _root;
    private readonly Image _primary;
    private readonly Image _secondary;
    private readonly Ellipse _shadow;
    private readonly ScaleTransform _rootScale;
    private readonly RotateTransform _rootRotate;
    private readonly TranslateTransform _rootTranslate;
    private readonly ScaleTransform _primaryMirror;
    private readonly ScaleTransform _secondaryMirror;

    public MachineCatRig()
    {
        Visual = new Grid
        {
            Width = 240d,
            Height = 240d,
            Background = Brushes.Transparent,
            IsHitTestVisible = false
        };

        _shadow = new Ellipse
        {
            Width = 118d,
            Height = 24d,
            Fill = new SolidColorBrush(Color.FromArgb(84, 0, 0, 0)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0d, 0d, 0d, 12d),
            IsHitTestVisible = false
        };
        Visual.Children.Add(_shadow);

        _root = new Grid
        {
            Width = 218d,
            Height = 218d,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            RenderTransformOrigin = new Point(0.5d, 0.86d),
            IsHitTestVisible = false
        };

        _rootScale = new ScaleTransform(1d, 1d);
        _rootRotate = new RotateTransform(0d);
        _rootTranslate = new TranslateTransform(0d, 0d);
        var rootTransforms = new TransformGroup();
        rootTransforms.Children.Add(_rootScale);
        rootTransforms.Children.Add(_rootRotate);
        rootTransforms.Children.Add(_rootTranslate);
        _root.RenderTransform = rootTransforms;

        _primaryMirror = new ScaleTransform(1d, 1d);
        _secondaryMirror = new ScaleTransform(1d, 1d);
        _primary = CreateImage(_primaryMirror);
        _secondary = CreateImage(_secondaryMirror);
        _root.Children.Add(_primary);
        _root.Children.Add(_secondary);
        Visual.Children.Add(_root);

        Apply(RigPose.Single("Idle"));
    }

    public Grid Visual { get; }

    // Gate 1 keeps a conservative body envelope. The native window hook makes the
    // rest of the transparent 240x240 host click-through.
    public Rect ApproximateInteractiveBounds => new(30d, 20d, 180d, 205d);

    public void Apply(in RigPose pose)
    {
        _primary.Source = MachineCatAssetCatalog.Get(pose.PrimaryAsset);
        _primaryMirror.ScaleX = pose.PrimaryMirror ? -1d : 1d;
        _primary.Opacity = pose.SecondaryAsset is null ? 1d : 1d - pose.SecondaryOpacity;

        if (pose.SecondaryAsset is null)
        {
            _secondary.Source = null;
            _secondary.Opacity = 0d;
        }
        else
        {
            _secondary.Source = MachineCatAssetCatalog.Get(pose.SecondaryAsset);
            _secondaryMirror.ScaleX = pose.SecondaryMirror ? -1d : 1d;
            _secondary.Opacity = pose.SecondaryOpacity;
        }

        _rootTranslate.X = pose.RootX;
        _rootTranslate.Y = pose.RootY;
        _rootRotate.Angle = pose.RootRotation;
        _rootScale.ScaleX = pose.RootScaleX;
        _rootScale.ScaleY = pose.RootScaleY;

        _shadow.RenderTransform = new ScaleTransform(pose.ShadowScaleX, 1d);
        _shadow.Opacity = pose.ShadowOpacity;
    }

    private static Image CreateImage(ScaleTransform mirror)
    {
        var transform = new TransformGroup();
        transform.Children.Add(mirror);

        return new Image
        {
            Width = 214d,
            Height = 214d,
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.Both,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            SnapsToDevicePixels = true,
            UseLayoutRounding = true,
            RenderTransformOrigin = new Point(0.5d, 0.5d),
            RenderTransform = transform,
            IsHitTestVisible = false
        };
    }
}
