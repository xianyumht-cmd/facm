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
    private readonly Grid _layered;
    private readonly Ellipse _shadow;
    private readonly ScaleTransform _rootScale;
    private readonly RotateTransform _rootRotate;
    private readonly TranslateTransform _rootTranslate;
    private readonly ScaleTransform _primaryMirror;
    private readonly ScaleTransform _secondaryMirror;

    private readonly RotateTransform _headRotate = new();
    private readonly TranslateTransform _headTranslate = new();
    private readonly RotateTransform _leftArmRotate = new();
    private readonly RotateTransform _rightArmRotate = new();
    private readonly RotateTransform _leftFootRotate = new();
    private readonly RotateTransform _rightFootRotate = new();
    private readonly TranslateTransform _leftFootTranslate = new();
    private readonly TranslateTransform _rightFootTranslate = new();

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
        _primary = CreateWholeImage(_primaryMirror);
        _secondary = CreateWholeImage(_secondaryMirror);

        // Walk/Run no longer mirror/crossfade whole characters. Instead we display
        // the exact approved Idle bitmap several times with non-overlapping clips.
        // Each clipped copy rotates around the anatomical shoulder/foot pivot, so
        // the pixels stay identical to the approved character while limbs move continuously.
        _layered = BuildLayeredApprovedPixelRig();

        _root.Children.Add(_primary);
        _root.Children.Add(_secondary);
        _root.Children.Add(_layered);
        Visual.Children.Add(_root);

        Apply(RigPose.Single("Idle", layered: true));
    }

    public Grid Visual { get; }

    // Gate 1 keeps a conservative body envelope. The native window hook makes the
    // rest of the transparent 240x240 host click-through.
    public Rect ApproximateInteractiveBounds => new(30d, 20d, 180d, 205d);

    public void Apply(in RigPose pose)
    {
        if (pose.UseLayeredRig)
        {
            _primary.Visibility = Visibility.Collapsed;
            _secondary.Visibility = Visibility.Collapsed;
            _layered.Visibility = Visibility.Visible;

            _headTranslate.Y = pose.HeadY;
            _headRotate.Angle = pose.HeadRotation;
            _leftArmRotate.Angle = pose.LeftArmRotation;
            _rightArmRotate.Angle = pose.RightArmRotation;
            _leftFootRotate.Angle = pose.LeftFootRotation;
            _rightFootRotate.Angle = pose.RightFootRotation;
            _leftFootTranslate.Y = pose.LeftFootY;
            _rightFootTranslate.Y = pose.RightFootY;
        }
        else
        {
            _layered.Visibility = Visibility.Collapsed;
            _primary.Visibility = Visibility.Visible;

            var primarySource = MachineCatAssetCatalog.Get(pose.PrimaryAsset);
            if (!ReferenceEquals(_primary.Source, primarySource))
                _primary.Source = primarySource;
            _primaryMirror.ScaleX = pose.PrimaryMirror ? -1d : 1d;
            _primary.Opacity = pose.SecondaryAsset is null ? 1d : 1d - pose.SecondaryOpacity;

            if (pose.SecondaryAsset is null || pose.SecondaryOpacity <= 0d)
            {
                _secondary.Visibility = Visibility.Collapsed;
                _secondary.Source = null;
                _secondary.Opacity = 0d;
            }
            else
            {
                _secondary.Visibility = Visibility.Visible;
                var secondarySource = MachineCatAssetCatalog.Get(pose.SecondaryAsset);
                if (!ReferenceEquals(_secondary.Source, secondarySource))
                    _secondary.Source = secondarySource;
                _secondaryMirror.ScaleX = pose.SecondaryMirror ? -1d : 1d;
                _secondary.Opacity = pose.SecondaryOpacity;
            }
        }

        _rootTranslate.X = pose.RootX;
        _rootTranslate.Y = pose.RootY;
        _rootRotate.Angle = pose.RootRotation;
        _rootScale.ScaleX = pose.RootScaleX;
        _rootScale.ScaleY = pose.RootScaleY;

        _shadow.RenderTransform = new ScaleTransform(pose.ShadowScaleX, 1d);
        _shadow.Opacity = pose.ShadowOpacity;
    }

    private Grid BuildLayeredApprovedPixelRig()
    {
        var source = MachineCatAssetCatalog.Get("Idle");
        var grid = new Grid
        {
            Width = 214d,
            Height = 214d,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            IsHitTestVisible = false
        };

        // Idle is 136x180 and is rendered with Stretch.Uniform inside 214x214.
        // These clips target the rendered head/torso/limb zones, with a few pixels
        // of overlap at joints so rotations do not expose transparent seams.
        var leftFoot = CreateLayer(
            source,
            new RectangleGeometry(new Rect(47d, 165d, 64d, 49d)),
            new Point(0.36d, 0.82d),
            _leftFootRotate,
            _leftFootTranslate);
        var rightFoot = CreateLayer(
            source,
            new RectangleGeometry(new Rect(103d, 165d, 64d, 49d)),
            new Point(0.64d, 0.82d),
            _rightFootRotate,
            _rightFootTranslate);
        var leftArm = CreateLayer(
            source,
            new RectangleGeometry(new Rect(24d, 111d, 52d, 70d)),
            new Point(0.27d, 0.59d),
            _leftArmRotate);
        var rightArm = CreateLayer(
            source,
            new RectangleGeometry(new Rect(138d, 111d, 52d, 70d)),
            new Point(0.73d, 0.59d),
            _rightArmRotate);
        var torso = CreateLayer(
            source,
            new RectangleGeometry(new Rect(53d, 96d, 108d, 108d), 12d, 12d),
            new Point(0.5d, 0.5d));
        var head = CreateLayer(
            source,
            new EllipseGeometry(new Point(107d, 66d), 84d, 69d),
            new Point(0.5d, 0.58d),
            _headRotate,
            _headTranslate);

        // Back-to-front order: feet/arms, torso, head.
        grid.Children.Add(leftFoot);
        grid.Children.Add(rightFoot);
        grid.Children.Add(leftArm);
        grid.Children.Add(rightArm);
        grid.Children.Add(torso);
        grid.Children.Add(head);
        return grid;
    }

    private static Image CreateLayer(
        ImageSource source,
        Geometry clip,
        Point transformOrigin,
        params Transform[] transforms)
    {
        var group = new TransformGroup();
        foreach (var transform in transforms)
            group.Children.Add(transform);

        var image = new Image
        {
            Source = source,
            Width = 214d,
            Height = 214d,
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.Both,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            SnapsToDevicePixels = true,
            UseLayoutRounding = true,
            Clip = clip,
            RenderTransformOrigin = transformOrigin,
            RenderTransform = group,
            IsHitTestVisible = false
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        return image;
    }

    private static Image CreateWholeImage(ScaleTransform mirror)
    {
        var transform = new TransformGroup();
        transform.Children.Add(mirror);

        var image = new Image
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
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        return image;
    }
}
