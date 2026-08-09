using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace FACM.MachineCatPrototype;

internal sealed class MachineCatRig
{
    private static readonly Brush Blue = FrozenBrush("#1677D2");
    private static readonly Brush BlueDark = FrozenBrush("#0B4E91");
    private static readonly Brush White = FrozenBrush("#FFFDF8");
    private static readonly Brush Outline = FrozenBrush("#18212C");
    private static readonly Brush Red = FrozenBrush("#E33131");
    private static readonly Brush MouthRed = FrozenBrush("#A61F24");
    private static readonly Brush Yellow = FrozenBrush("#F5C542");
    private static readonly Brush Black = FrozenBrush("#111820");
    private static readonly Brush Shadow = FrozenBrush("#4B111820");

    private readonly Canvas _surface;
    private readonly Canvas _root;
    private readonly Ellipse _shadow;

    private readonly ScaleTransform _rootScale = new(1d, 1d);
    private readonly RotateTransform _rootRotation = new(0d);
    private readonly TranslateTransform _rootTranslation = new(0d, 0d);

    private readonly RotateTransform _headRotation = new(0d);
    private readonly TranslateTransform _headTranslation = new(0d, 0d);

    private readonly RotateTransform _leftArmRotation = new(0d);
    private readonly TranslateTransform _leftArmTranslation = new(0d, 0d);
    private readonly RotateTransform _rightArmRotation = new(0d);
    private readonly TranslateTransform _rightArmTranslation = new(0d, 0d);
    private readonly RotateTransform _leftLegRotation = new(0d);
    private readonly TranslateTransform _leftLegTranslation = new(0d, 0d);
    private readonly RotateTransform _rightLegRotation = new(0d);
    private readonly TranslateTransform _rightLegTranslation = new(0d, 0d);

    private readonly TranslateTransform _leftPupilTranslation = new(0d, 0d);
    private readonly TranslateTransform _rightPupilTranslation = new(0d, 0d);
    private readonly ScaleTransform _leftEyeScale = new(1d, 1d);
    private readonly ScaleTransform _rightEyeScale = new(1d, 1d);
    private readonly ScaleTransform _mouthScale = new(1d, 1d);
    private readonly RotateTransform _bellRotation = new(0d);
    private readonly ScaleTransform _shadowScale = new(1d, 1d);

    public MachineCatRig()
    {
        _surface = new Canvas
        {
            Width = 240d,
            Height = 240d,
            Background = Brushes.Transparent,
            SnapsToDevicePixels = true,
            Focusable = true
        };

        _shadow = new Ellipse
        {
            Width = 104d,
            Height = 14d,
            Fill = Shadow,
            Opacity = 0.22d,
            RenderTransformOrigin = new Point(0.5d, 0.5d),
            RenderTransform = _shadowScale,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(_shadow, 68d);
        Canvas.SetTop(_shadow, 202d);
        _surface.Children.Add(_shadow);

        _root = new Canvas
        {
            Width = 180d,
            Height = 190d,
            RenderTransformOrigin = new Point(0.5d, 0.58d)
        };
        _root.RenderTransform = new TransformGroup
        {
            Children = new TransformCollection
            {
                _rootScale,
                _rootRotation,
                _rootTranslation
            }
        };
        Canvas.SetLeft(_root, 30d);
        Canvas.SetTop(_root, 18d);
        _surface.Children.Add(_root);

        var leftLeg = CreateLeg();
        leftLeg.RenderTransformOrigin = new Point(0.5d, 0.15d);
        leftLeg.RenderTransform = TransformGroup(_leftLegRotation, _leftLegTranslation);
        Canvas.SetLeft(leftLeg, 39d);
        Canvas.SetTop(leftLeg, 143d);
        _root.Children.Add(leftLeg);

        var rightLeg = CreateLeg();
        rightLeg.RenderTransformOrigin = new Point(0.5d, 0.15d);
        rightLeg.RenderTransform = TransformGroup(_rightLegRotation, _rightLegTranslation);
        Canvas.SetLeft(rightLeg, 96d);
        Canvas.SetTop(rightLeg, 143d);
        _root.Children.Add(rightLeg);

        var leftArm = CreateArm(left: true);
        leftArm.RenderTransformOrigin = new Point(0.5d, 0.12d);
        leftArm.RenderTransform = TransformGroup(_leftArmRotation, _leftArmTranslation);
        Canvas.SetLeft(leftArm, 28d);
        Canvas.SetTop(leftArm, 87d);
        _root.Children.Add(leftArm);

        var rightArm = CreateArm(left: false);
        rightArm.RenderTransformOrigin = new Point(0.5d, 0.12d);
        rightArm.RenderTransform = TransformGroup(_rightArmRotation, _rightArmTranslation);
        Canvas.SetLeft(rightArm, 119d);
        Canvas.SetTop(rightArm, 87d);
        _root.Children.Add(rightArm);

        var body = new Ellipse
        {
            Width = 92d,
            Height = 106d,
            Fill = Blue,
            Stroke = Outline,
            StrokeThickness = 2.2d
        };
        Canvas.SetLeft(body, 44d);
        Canvas.SetTop(body, 73d);
        _root.Children.Add(body);

        var belly = new Ellipse
        {
            Width = 62d,
            Height = 73d,
            Fill = White,
            Stroke = Outline,
            StrokeThickness = 1.7d
        };
        Canvas.SetLeft(belly, 59d);
        Canvas.SetTop(belly, 96d);
        _root.Children.Add(belly);

        var pocketArc = new System.Windows.Shapes.Path
        {
            Stroke = Outline,
            StrokeThickness = 1.8d,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Data = Geometry.Parse("M 67,132 Q 90,153 113,132")
        };
        _root.Children.Add(pocketArc);

        var collar = new Border
        {
            Width = 86d,
            Height = 12d,
            Background = Red,
            BorderBrush = Outline,
            BorderThickness = new Thickness(1.6d),
            CornerRadius = new CornerRadius(6d)
        };
        Canvas.SetLeft(collar, 47d);
        Canvas.SetTop(collar, 78d);
        _root.Children.Add(collar);

        var head = CreateHead();
        head.RenderTransformOrigin = new Point(0.5d, 0.78d);
        head.RenderTransform = TransformGroup(_headRotation, _headTranslation);
        Canvas.SetLeft(head, 20d);
        Canvas.SetTop(head, 0d);
        _root.Children.Add(head);

        var bell = CreateBell();
        bell.RenderTransformOrigin = new Point(0.5d, 0.1d);
        bell.RenderTransform = _bellRotation;
        Canvas.SetLeft(bell, 78d);
        Canvas.SetTop(bell, 79d);
        _root.Children.Add(bell);
    }

    public FrameworkElement Visual => _surface;

    public Rect ApproximateInteractiveBounds => new(36d, 26d, 168d, 188d);

    public void Apply(in RigPose pose)
    {
        _rootScale.ScaleX = pose.RootScaleX;
        _rootScale.ScaleY = pose.RootScaleY;
        _rootRotation.Angle = pose.RootRotation;
        _rootTranslation.X = pose.RootX;
        _rootTranslation.Y = pose.RootY;

        _headRotation.Angle = pose.HeadRotation;
        _headTranslation.Y = pose.HeadY;

        _leftArmRotation.Angle = pose.LeftArmRotation;
        _rightArmRotation.Angle = pose.RightArmRotation;
        _leftLegRotation.Angle = pose.LeftLegRotation;
        _rightLegRotation.Angle = pose.RightLegRotation;
        _leftArmTranslation.Y = pose.LeftArmY;
        _rightArmTranslation.Y = pose.RightArmY;
        _leftLegTranslation.Y = pose.LeftLegY;
        _rightLegTranslation.Y = pose.RightLegY;

        _leftPupilTranslation.X = pose.EyeX;
        _leftPupilTranslation.Y = pose.EyeY;
        _rightPupilTranslation.X = pose.EyeX;
        _rightPupilTranslation.Y = pose.EyeY;
        _leftEyeScale.ScaleY = Math.Max(0.05d, pose.EyeOpen);
        _rightEyeScale.ScaleY = Math.Max(0.05d, pose.EyeOpen);
        _mouthScale.ScaleY = Math.Clamp(pose.MouthOpen, 0.25d, 1.3d);
        _bellRotation.Angle = pose.BellRotation;

        _shadowScale.ScaleX = pose.ShadowScaleX;
        _shadow.Opacity = pose.ShadowOpacity;
    }

    private Canvas CreateHead()
    {
        var head = new Canvas
        {
            Width = 140d,
            Height = 120d
        };

        var shell = new Ellipse
        {
            Width = 140d,
            Height = 116d,
            Fill = Blue,
            Stroke = Outline,
            StrokeThickness = 2.4d
        };
        Canvas.SetLeft(shell, 0d);
        Canvas.SetTop(shell, 0d);
        head.Children.Add(shell);

        var face = new Ellipse
        {
            Width = 109d,
            Height = 92d,
            Fill = White,
            Stroke = Outline,
            StrokeThickness = 1.6d
        };
        Canvas.SetLeft(face, 15.5d);
        Canvas.SetTop(face, 19d);
        head.Children.Add(face);

        var leftEye = CreateEye(_leftPupilTranslation, _leftEyeScale);
        Canvas.SetLeft(leftEye, 47d);
        Canvas.SetTop(leftEye, 13d);
        head.Children.Add(leftEye);

        var rightEye = CreateEye(_rightPupilTranslation, _rightEyeScale);
        Canvas.SetLeft(rightEye, 69d);
        Canvas.SetTop(rightEye, 13d);
        head.Children.Add(rightEye);

        var nose = new Ellipse
        {
            Width = 23d,
            Height = 23d,
            Fill = Red,
            Stroke = Outline,
            StrokeThickness = 1.6d
        };
        Canvas.SetLeft(nose, 58.5d);
        Canvas.SetTop(nose, 45d);
        head.Children.Add(nose);

        var noseGlint = new Ellipse
        {
            Width = 6d,
            Height = 6d,
            Fill = Brushes.White,
            Opacity = 0.78d,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(noseGlint, 62d);
        Canvas.SetTop(noseGlint, 48d);
        head.Children.Add(noseGlint);

        var centerLine = new Line
        {
            X1 = 70d,
            X2 = 70d,
            Y1 = 68d,
            Y2 = 82d,
            Stroke = Outline,
            StrokeThickness = 1.8d,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        };
        head.Children.Add(centerLine);

        AddWhisker(head, 34d, 66d, 12d, 61d);
        AddWhisker(head, 34d, 74d, 10d, 74d);
        AddWhisker(head, 35d, 82d, 13d, 88d);
        AddWhisker(head, 106d, 66d, 128d, 61d);
        AddWhisker(head, 106d, 74d, 130d, 74d);
        AddWhisker(head, 105d, 82d, 127d, 88d);

        var mouth = new Ellipse
        {
            Width = 55d,
            Height = 30d,
            Fill = MouthRed,
            Stroke = Outline,
            StrokeThickness = 1.8d,
            RenderTransformOrigin = new Point(0.5d, 0d),
            RenderTransform = _mouthScale
        };
        Canvas.SetLeft(mouth, 42.5d);
        Canvas.SetTop(mouth, 78d);
        head.Children.Add(mouth);

        var tongue = new Ellipse
        {
            Width = 25d,
            Height = 10d,
            Fill = FrozenBrush("#ED776D"),
            Opacity = 0.92d,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(tongue, 57.5d);
        Canvas.SetTop(tongue, 94d);
        head.Children.Add(tongue);

        return head;
    }

    private static Grid CreateEye(TranslateTransform pupilTranslation, ScaleTransform eyeScale)
    {
        var eye = new Grid
        {
            Width = 28d,
            Height = 37d,
            RenderTransformOrigin = new Point(0.5d, 0.55d),
            RenderTransform = eyeScale
        };

        eye.Children.Add(new Ellipse
        {
            Fill = White,
            Stroke = Outline,
            StrokeThickness = 1.5d
        });

        var pupil = new Ellipse
        {
            Width = 9d,
            Height = 15d,
            Fill = Black,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            RenderTransform = pupilTranslation
        };
        eye.Children.Add(pupil);

        var glint = new Ellipse
        {
            Width = 3d,
            Height = 4d,
            Fill = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(-3d, -5d, 0d, 0d),
            IsHitTestVisible = false
        };
        eye.Children.Add(glint);

        return eye;
    }

    private static Grid CreateArm(bool left)
    {
        var arm = new Grid
        {
            Width = 35d,
            Height = 67d
        };

        var bluePart = new Ellipse
        {
            Width = 28d,
            Height = 47d,
            Fill = Blue,
            Stroke = Outline,
            StrokeThickness = 1.8d,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top
        };
        arm.Children.Add(bluePart);

        var hand = new Ellipse
        {
            Width = 27d,
            Height = 27d,
            Fill = White,
            Stroke = Outline,
            StrokeThickness = 1.8d,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom
        };
        arm.Children.Add(hand);

        if (left)
            arm.RenderTransformOrigin = new Point(0.65d, 0.12d);
        else
            arm.RenderTransformOrigin = new Point(0.35d, 0.12d);

        return arm;
    }

    private static Grid CreateLeg()
    {
        var leg = new Grid
        {
            Width = 47d,
            Height = 39d
        };

        leg.Children.Add(new Ellipse
        {
            Width = 42d,
            Height = 31d,
            Fill = White,
            Stroke = Outline,
            StrokeThickness = 1.9d,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom
        });
        return leg;
    }

    private static Grid CreateBell()
    {
        var bell = new Grid
        {
            Width = 24d,
            Height = 27d
        };

        bell.Children.Add(new Ellipse
        {
            Width = 24d,
            Height = 24d,
            Fill = Yellow,
            Stroke = Outline,
            StrokeThickness = 1.6d,
            VerticalAlignment = VerticalAlignment.Top
        });

        bell.Children.Add(new Line
        {
            X1 = 5d,
            X2 = 19d,
            Y1 = 10d,
            Y2 = 10d,
            Stroke = Outline,
            StrokeThickness = 1.4d
        });

        bell.Children.Add(new Ellipse
        {
            Width = 4d,
            Height = 4d,
            Fill = Black,
            Margin = new Thickness(0d, 7d, 0d, 0d),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        });

        bell.Children.Add(new Line
        {
            X1 = 12d,
            X2 = 12d,
            Y1 = 16d,
            Y2 = 24d,
            Stroke = Outline,
            StrokeThickness = 1.3d
        });

        return bell;
    }

    private static void AddWhisker(Canvas canvas, double x1, double y1, double x2, double y2)
    {
        canvas.Children.Add(new Line
        {
            X1 = x1,
            Y1 = y1,
            X2 = x2,
            Y2 = y2,
            Stroke = Outline,
            StrokeThickness = 1.5d,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            IsHitTestVisible = false
        });
    }

    private static TransformGroup TransformGroup(params Transform[] transforms)
    {
        var group = new TransformGroup();
        foreach (var transform in transforms) group.Children.Add(transform);
        return group;
    }

    private static SolidColorBrush FrozenBrush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
