using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace FACM.MachineCat3DPrototype;

internal enum MotionState
{
    Idle,
    Walk,
    Run,
    Turn
}

internal sealed class RigidPartRig
{
    private static readonly Point3D ModelCenter = new(-0.03, 1.30, 0.0);
    private static readonly Point3D HeadPivot = new(-0.05, 1.55, 0.02);
    private static readonly Point3D LeftShoulder = new(-0.74, 1.57, 0.03);
    private static readonly Point3D RightShoulder = new(0.66, 1.60, 0.03);
    private static readonly Point3D LeftElbow = new(-1.08, 1.18, 0.12);
    private static readonly Point3D RightElbow = new(1.00, 1.24, 0.09);
    private static readonly Point3D LeftHip = new(-0.52, 0.39, 0.00);
    private static readonly Point3D RightHip = new(0.40, 0.40, 0.00);
    private static readonly Point3D LeftAnkle = new(-0.62, -0.16, 0.02);
    private static readonly Point3D RightAnkle = new(0.45, -0.15, 0.03);
    private static readonly Point3D BellPivot = new(-0.05, 1.56, 0.86);
    private static readonly Point3D TailPivot = new(0.00, 0.42, -1.02);

    private readonly Dictionary<string, GeometryModel3D> _parts = new(StringComparer.OrdinalIgnoreCase);
    private readonly GeometryModel3D _leftPupil;
    private readonly GeometryModel3D _rightPupil;

    public RigidPartRig(RigidModel model)
    {
        var group = new Model3DGroup();
        foreach (var part in model.Parts)
        {
            var material = CreateMaterial(ColorFor(part.Name));
            var geometryModel = new GeometryModel3D(part.Geometry, material)
            {
                BackMaterial = material
            };
            _parts[part.Name] = geometryModel;
            group.Children.Add(geometryModel);
        }

        // The downloaded asset has only one generic material and therefore no visible
        // pupil colour separation. Two tiny procedural spheres restore the eye focus
        // without modifying or redistributing the third-party model file.
        _leftPupil = CreateSphereModel(new Point3D(-0.342, 2.96, 1.145), 0.085, Colors.Black);
        _rightPupil = CreateSphereModel(new Point3D(0.161, 2.965, 1.17), 0.085, Colors.Black);
        group.Children.Add(_leftPupil);
        group.Children.Add(_rightPupil);

        Visual = new ModelVisual3D { Content = group };
        Apply(MotionState.Idle, 0d);
    }

    public ModelVisual3D Visual { get; }

    public void Apply(MotionState state, double time)
    {
        var cycle = state switch
        {
            MotionState.Walk => time * Math.PI * 2d * 1.35d,
            MotionState.Run => time * Math.PI * 2d * 2.65d,
            _ => time * Math.PI * 2d * 0.35d
        };

        var sine = Math.Sin(cycle);
        var cosine = Math.Cos(cycle);
        var absoluteSine = Math.Abs(sine);
        var walk = state == MotionState.Walk;
        var run = state == MotionState.Run;

        var armSwing = walk ? sine * 24d : run ? sine * 39d : Math.Sin(time * 1.1d) * 1.5d;
        var legSwing = walk ? sine * 19d : run ? sine * 33d : 0d;
        var elbowBend = walk ? 8d + Math.Max(0d, -cosine) * 10d : run ? 18d + Math.Max(0d, -cosine) * 18d : 3d;
        var ankle = walk ? cosine * 7d : run ? cosine * 13d : 0d;
        var bodyBob = walk ? absoluteSine * 0.035d : run ? absoluteSine * 0.070d : Math.Sin(time * 2.0d) * 0.006d;
        var headCounter = walk ? -sine * 2.0d : run ? -sine * 3.2d : Math.Sin(time * 0.75d) * 1.0d;
        var bodyRoll = walk ? sine * 1.25d : run ? sine * 2.2d : Math.Sin(time * 0.7d) * 0.35d;
        var yaw = state == MotionState.Turn
            ? PositiveModulo(time * 72d, 360d)
            : -11d;

        // Opposite arm/leg phase gives a real rigid-part walk cycle. No vertices are
        // stretched: every mesh is only translated/rotated around an explicit joint.
        SetArm(".L", LeftShoulder, LeftElbow, armSwing, elbowBend);
        SetArm(".R", RightShoulder, RightElbow, -armSwing, elbowBend);
        SetLeg(".L", LeftHip, LeftAnkle, -legSwing, -ankle);
        SetLeg(".R", RightHip, RightAnkle, legSwing, ankle);

        var headTransforms = Group(
            Rotate(new Vector3D(0, 1, 0), headCounter, HeadPivot),
            Rotate(new Vector3D(0, 0, 1), -bodyRoll * 0.28d, HeadPivot));
        SetHeadGroup(headTransforms);

        var bellSwing = walk ? -sine * 5d : run ? -sine * 10d : Math.Sin(time * 1.4d) * 1.5d;
        SetContains("bell", Group(Rotate(new Vector3D(1, 0, 0), bellSwing, BellPivot)));
        SetContains("tail", Group(Rotate(new Vector3D(1, 0, 0), run ? sine * 8d : sine * 3d, TailPivot)));

        var root = new Transform3DGroup();
        root.Children.Add(new TranslateTransform3D(0d, bodyBob, 0d));
        root.Children.Add(Rotate(new Vector3D(0, 0, 1), bodyRoll, ModelCenter));
        root.Children.Add(Rotate(new Vector3D(0, 1, 0), yaw, ModelCenter));
        Visual.Transform = root;
    }

    private void SetArm(string side, Point3D shoulder, Point3D elbow, double shoulderAngle, double elbowAngle)
    {
        var shoulderTransform = Rotate(new Vector3D(1, 0, 0), shoulderAngle, shoulder);
        var elbowTransform = Rotate(new Vector3D(1, 0, 0), elbowAngle, elbow);
        foreach (var (name, model) in _parts)
        {
            var lower = name.ToLowerInvariant();
            if (!lower.Contains(side.ToLowerInvariant())) continue;
            if (lower.Contains("upper.arm"))
                model.Transform = Group(shoulderTransform);
            else if (lower.Contains("010.arm") || lower.Contains("hand"))
                model.Transform = Group(shoulderTransform, elbowTransform);
        }
    }

    private void SetLeg(string side, Point3D hip, Point3D anklePivot, double hipAngle, double ankleAngle)
    {
        var hipTransform = Rotate(new Vector3D(1, 0, 0), hipAngle, hip);
        var ankleTransform = Rotate(new Vector3D(1, 0, 0), ankleAngle, anklePivot);
        foreach (var (name, model) in _parts)
        {
            var lower = name.ToLowerInvariant();
            if (!lower.Contains(side.ToLowerInvariant())) continue;
            if (lower.Contains("012.leg"))
                model.Transform = Group(hipTransform);
            else if (lower.Contains("013.foot"))
                model.Transform = Group(hipTransform, ankleTransform);
        }
    }

    private void SetHeadGroup(Transform3D transform)
    {
        foreach (var (name, model) in _parts)
        {
            var lower = name.ToLowerInvariant();
            if (lower.Contains("head") || lower.Contains("eye") || lower.Contains("nose") ||
                lower.Contains("mouth") || lower.Contains("moustache"))
                model.Transform = transform;
        }
        _leftPupil.Transform = transform;
        _rightPupil.Transform = transform;
    }

    private void SetContains(string token, Transform3D transform)
    {
        foreach (var (name, model) in _parts)
            if (name.Contains(token, StringComparison.OrdinalIgnoreCase))
                model.Transform = transform;
    }

    private static RotateTransform3D Rotate(Vector3D axis, double angle, Point3D center)
        => new(new AxisAngleRotation3D(axis, angle), center);

    private static Transform3DGroup Group(params Transform3D[] transforms)
    {
        var group = new Transform3DGroup();
        foreach (var transform in transforms)
            group.Children.Add(transform);
        return group;
    }

    private static Material CreateMaterial(Color color)
    {
        var diffuseBrush = new SolidColorBrush(color);
        diffuseBrush.Freeze();
        var specularBrush = new SolidColorBrush(Color.FromArgb(85, 255, 255, 255));
        specularBrush.Freeze();
        var group = new MaterialGroup();
        group.Children.Add(new DiffuseMaterial(diffuseBrush));
        group.Children.Add(new SpecularMaterial(specularBrush, 26d));
        group.Freeze();
        return group;
    }

    private static Color ColorFor(string name)
    {
        var lower = name.ToLowerInvariant();
        if (lower.Contains("moustache")) return Color.FromRgb(40, 44, 48);
        if (lower.Contains("nose") || lower.Contains("collar") || lower.Contains("tail.2")) return Color.FromRgb(225, 46, 55);
        if (lower.Contains("bell")) return Color.FromRgb(246, 190, 34);
        if (lower.Contains("eye") || lower.Contains("mouth") || lower.Contains("hand") ||
            lower.Contains("foot") || lower.Contains("bag")) return Color.FromRgb(248, 249, 250);
        return Color.FromRgb(43, 153, 222);
    }

    private static GeometryModel3D CreateSphereModel(Point3D center, double radius, Color color)
    {
        const int latitude = 8;
        const int longitude = 12;
        var mesh = new MeshGeometry3D();
        for (var y = 0; y <= latitude; y++)
        {
            var v = y / (double)latitude;
            var phi = Math.PI * v;
            for (var x = 0; x <= longitude; x++)
            {
                var u = x / (double)longitude;
                var theta = Math.PI * 2d * u;
                var normal = new Vector3D(
                    Math.Sin(phi) * Math.Cos(theta),
                    Math.Cos(phi),
                    Math.Sin(phi) * Math.Sin(theta));
                mesh.Normals.Add(normal);
                mesh.Positions.Add(center + normal * radius);
            }
        }

        for (var y = 0; y < latitude; y++)
        for (var x = 0; x < longitude; x++)
        {
            var a = y * (longitude + 1) + x;
            var b = a + longitude + 1;
            mesh.TriangleIndices.Add(a);
            mesh.TriangleIndices.Add(b);
            mesh.TriangleIndices.Add(a + 1);
            mesh.TriangleIndices.Add(a + 1);
            mesh.TriangleIndices.Add(b);
            mesh.TriangleIndices.Add(b + 1);
        }
        mesh.Freeze();
        var material = CreateMaterial(color);
        return new GeometryModel3D(mesh, material) { BackMaterial = material };
    }

    private static double PositiveModulo(double value, double period)
    {
        var result = value % period;
        return result < 0d ? result + period : result;
    }
}
