using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace FACM.Pets
{
    internal sealed class Pet3DModel
    {
        public Model3DGroup Model { get; set; }
        public List<ScaleTransform3D> BlinkTargets { get; } = new List<ScaleTransform3D>();
    }

    internal static class Pet3DModelFactory
    {
        private static readonly object MeshLock = new object();
        private static MeshGeometry3D _sphere;
        private static MeshGeometry3D _box;
        private static MeshGeometry3D _cone;
        private static MeshGeometry3D _cylinder;

        public static Pet3DModel Create(PetDefinition pet)
        {
            var result = new Pet3DModel { Model = new Model3DGroup() };
            AddShadow(result.Model);

            switch (pet.Kind)
            {
                case PetKind.Jelly: BuildJelly(result, pet); break;
                case PetKind.Cat: BuildCat(result, pet); break;
                case PetKind.Fox: BuildFox(result, pet); break;
                case PetKind.Robot: BuildRobot(result, pet); break;
                case PetKind.Ghost: BuildGhost(result, pet); break;
                case PetKind.Chick: BuildChick(result, pet); break;
                case PetKind.Dragon: BuildDragon(result, pet); break;
                case PetKind.Star: BuildStar(result, pet); break;
                case PetKind.PixelBot: BuildPixelBot(result, pet); break;
                case PetKind.CloudBunny: BuildBunny(result, pet); break;
                default: BuildJelly(result, pet); break;
            }

            return result;
        }

        private static void BuildJelly(Pet3DModel model, PetDefinition pet)
        {
            AddSphere(model.Model, new Point3D(0, -0.05, 0), new Vector3D(1.18, 1.02, 0.92), pet.Primary, 46);
            AddSphere(model.Model, new Point3D(-0.52, -0.98, 0.02), new Vector3D(0.42, 0.25, 0.45), pet.Secondary, 30);
            AddSphere(model.Model, new Point3D(0.52, -0.98, 0.02), new Vector3D(0.42, 0.25, 0.45), pet.Secondary, 30);
            AddFace(model, new Point3D(0, 0.08, 0.86), pet);
            AddSphere(model.Model, new Point3D(-0.37, 0.54, 0.70), new Vector3D(0.26, 0.12, 0.11), pet.Accent, 65, 0.76);
        }

        private static void BuildCat(Pet3DModel model, PetDefinition pet)
        {
            AddSphere(model.Model, new Point3D(0, -0.42, -0.04), new Vector3D(0.86, 0.93, 0.74), pet.Secondary, 38);
            AddSphere(model.Model, new Point3D(0, 0.47, 0.10), new Vector3D(0.97, 0.82, 0.78), pet.Primary, 44);
            AddCone(model.Model, new Point3D(-0.52, 1.11, 0.03), new Vector3D(0.38, 0.62, 0.38), pet.Secondary, new Vector3D(0, 0, 1), -10);
            AddCone(model.Model, new Point3D(0.52, 1.11, 0.03), new Vector3D(0.38, 0.62, 0.38), pet.Secondary, new Vector3D(0, 0, 1), 10);
            AddSphere(model.Model, new Point3D(-0.36, -1.05, 0.42), new Vector3D(0.33, 0.24, 0.42), pet.Primary, 28);
            AddSphere(model.Model, new Point3D(0.36, -1.05, 0.42), new Vector3D(0.33, 0.24, 0.42), pet.Primary, 28);
            AddCylinder(model.Model, new Point3D(0.82, -0.24, -0.34), new Vector3D(0.18, 1.28, 0.18), pet.Secondary, new Vector3D(0, 0, 1), -48);
            AddFace(model, new Point3D(0, 0.52, 0.83), pet);
            AddSphere(model.Model, new Point3D(0, 0.23, 0.92), new Vector3D(0.11, 0.08, 0.07), pet.Accent, 20);
        }

        private static void BuildFox(Pet3DModel model, PetDefinition pet)
        {
            AddSphere(model.Model, new Point3D(-0.08, -0.43, -0.02), new Vector3D(0.88, 0.92, 0.70), pet.Secondary, 36);
            AddSphere(model.Model, new Point3D(-0.05, 0.48, 0.07), new Vector3D(0.98, 0.79, 0.75), pet.Primary, 44);
            AddCone(model.Model, new Point3D(-0.56, 1.13, -0.01), new Vector3D(0.37, 0.68, 0.37), pet.Secondary, new Vector3D(0, 0, 1), -13);
            AddCone(model.Model, new Point3D(0.46, 1.13, -0.01), new Vector3D(0.37, 0.68, 0.37), pet.Secondary, new Vector3D(0, 0, 1), 13);
            AddSphere(model.Model, new Point3D(-0.05, 0.22, 0.86), new Vector3D(0.52, 0.32, 0.24), pet.Accent, 22);
            AddCylinder(model.Model, new Point3D(0.94, -0.19, -0.24), new Vector3D(0.31, 1.45, 0.31), pet.Primary, new Vector3D(0, 0, 1), -58);
            AddSphere(model.Model, new Point3D(1.38, 0.37, -0.21), new Vector3D(0.38, 0.48, 0.38), pet.Accent, 34);
            AddFace(model, new Point3D(-0.05, 0.48, 0.84), pet);
            AddSphere(model.Model, new Point3D(-0.05, 0.17, 1.02), new Vector3D(0.10, 0.08, 0.08), pet.Outline, 12);
        }

        private static void BuildRobot(Pet3DModel model, PetDefinition pet)
        {
            AddBox(model.Model, new Point3D(0, -0.42, 0), new Vector3D(1.13, 0.86, 0.72), pet.Secondary, 42);
            AddBox(model.Model, new Point3D(0, 0.54, 0.06), new Vector3D(1.02, 0.72, 0.72), pet.Primary, 50);
            AddBox(model.Model, new Point3D(0, 0.53, 0.73), new Vector3D(0.72, 0.38, 0.06), Color.FromRgb(29, 42, 67), 20);
            AddEye(model, new Point3D(-0.30, 0.55, 0.81), pet.Accent, 0.12, 0.14);
            AddEye(model, new Point3D(0.30, 0.55, 0.81), pet.Accent, 0.12, 0.14);
            AddCylinder(model.Model, new Point3D(0, 1.25, 0), new Vector3D(0.06, 0.50, 0.06), pet.Outline, new Vector3D(0, 0, 1), 0);
            AddSphere(model.Model, new Point3D(0, 1.52, 0), new Vector3D(0.13, 0.13, 0.13), pet.Accent, 70);
            AddBox(model.Model, new Point3D(-0.60, -0.34, 0), new Vector3D(0.18, 0.54, 0.24), pet.Primary, 30);
            AddBox(model.Model, new Point3D(0.60, -0.34, 0), new Vector3D(0.18, 0.54, 0.24), pet.Primary, 30);
            AddBox(model.Model, new Point3D(-0.38, -1.12, 0.10), new Vector3D(0.32, 0.22, 0.44), pet.Primary, 28);
            AddBox(model.Model, new Point3D(0.38, -1.12, 0.10), new Vector3D(0.32, 0.22, 0.44), pet.Primary, 28);
        }

        private static void BuildGhost(Pet3DModel model, PetDefinition pet)
        {
            AddSphere(model.Model, new Point3D(0, 0.28, 0), new Vector3D(0.98, 1.13, 0.78), pet.Primary, 58, 0.92);
            AddCone(model.Model, new Point3D(-0.58, -0.91, 0.03), new Vector3D(0.46, 0.68, 0.46), pet.Secondary, new Vector3D(1, 0, 0), 180, 0.92);
            AddCone(model.Model, new Point3D(0, -1.01, 0.03), new Vector3D(0.46, 0.72, 0.46), pet.Primary, new Vector3D(1, 0, 0), 180, 0.92);
            AddCone(model.Model, new Point3D(0.58, -0.91, 0.03), new Vector3D(0.46, 0.68, 0.46), pet.Secondary, new Vector3D(1, 0, 0), 180, 0.92);
            AddSphere(model.Model, new Point3D(-0.92, 0.02, -0.10), new Vector3D(0.48, 0.22, 0.22), pet.Secondary, 34, 0.90);
            AddSphere(model.Model, new Point3D(0.92, 0.02, -0.10), new Vector3D(0.48, 0.22, 0.22), pet.Secondary, 34, 0.90);
            AddFace(model, new Point3D(0, 0.40, 0.79), pet);
        }

        private static void BuildChick(Pet3DModel model, PetDefinition pet)
        {
            AddSphere(model.Model, new Point3D(0, -0.28, 0), new Vector3D(1.00, 1.08, 0.80), pet.Primary, 42);
            AddSphere(model.Model, new Point3D(-0.89, -0.20, -0.03), new Vector3D(0.45, 0.26, 0.22), pet.Secondary, 28);
            AddSphere(model.Model, new Point3D(0.89, -0.20, -0.03), new Vector3D(0.45, 0.26, 0.22), pet.Secondary, 28);
            AddCone(model.Model, new Point3D(0, 0.10, 0.93), new Vector3D(0.22, 0.38, 0.22), Color.FromRgb(255, 132, 36), new Vector3D(1, 0, 0), 90);
            AddSphere(model.Model, new Point3D(-0.35, -1.15, 0.24), new Vector3D(0.28, 0.16, 0.36), pet.Secondary, 20);
            AddSphere(model.Model, new Point3D(0.35, -1.15, 0.24), new Vector3D(0.28, 0.16, 0.36), pet.Secondary, 20);
            AddFace(model, new Point3D(0, 0.32, 0.83), pet);
        }

        private static void BuildDragon(Pet3DModel model, PetDefinition pet)
        {
            AddSphere(model.Model, new Point3D(0, -0.44, -0.02), new Vector3D(0.90, 1.02, 0.72), pet.Secondary, 40);
            AddSphere(model.Model, new Point3D(0, 0.54, 0.13), new Vector3D(0.92, 0.76, 0.78), pet.Primary, 48);
            AddCone(model.Model, new Point3D(-0.46, 1.12, -0.02), new Vector3D(0.20, 0.55, 0.20), pet.Accent, new Vector3D(0, 0, 1), -18);
            AddCone(model.Model, new Point3D(0.46, 1.12, -0.02), new Vector3D(0.20, 0.55, 0.20), pet.Accent, new Vector3D(0, 0, 1), 18);
            AddBox(model.Model, new Point3D(-0.96, -0.12, -0.12), new Vector3D(0.70, 0.08, 0.76), pet.Primary, 30, new Vector3D(0, 0, 1), 24);
            AddBox(model.Model, new Point3D(0.96, -0.12, -0.12), new Vector3D(0.70, 0.08, 0.76), pet.Primary, 30, new Vector3D(0, 0, 1), -24);
            AddCylinder(model.Model, new Point3D(0.72, -0.71, -0.24), new Vector3D(0.22, 1.48, 0.22), pet.Secondary, new Vector3D(0, 0, 1), -58);
            AddFace(model, new Point3D(0, 0.54, 0.86), pet);
            AddSphere(model.Model, new Point3D(0, 0.24, 0.97), new Vector3D(0.15, 0.10, 0.09), pet.Accent, 24);
        }

        private static void BuildStar(Pet3DModel model, PetDefinition pet)
        {
            AddSphere(model.Model, new Point3D(0, 0, 0), new Vector3D(0.67, 0.67, 0.48), pet.Primary, 62);
            for (var i = 0; i < 5; i++)
            {
                var angle = -90 + i * 72;
                var radians = angle * Math.PI / 180.0;
                var position = new Point3D(Math.Cos(radians) * 0.90, Math.Sin(radians) * 0.90, -0.03);
                AddCone(model.Model, position, new Vector3D(0.38, 0.92, 0.38), i % 2 == 0 ? pet.Primary : pet.Secondary, new Vector3D(0, 0, 1), -angle);
            }
            AddFace(model, new Point3D(0, 0.03, 0.51), pet);
            AddSphere(model.Model, new Point3D(1.25, 0.72, 0.14), new Vector3D(0.12, 0.12, 0.12), pet.Accent, 80);
            AddSphere(model.Model, new Point3D(-1.18, -0.62, 0.02), new Vector3D(0.08, 0.08, 0.08), pet.Accent, 80);
        }

        private static void BuildPixelBot(Pet3DModel model, PetDefinition pet)
        {
            AddBox(model.Model, new Point3D(0, -0.44, 0), new Vector3D(0.95, 0.82, 0.68), pet.Secondary, 25);
            AddBox(model.Model, new Point3D(0, 0.54, 0.04), new Vector3D(1.03, 0.68, 0.70), pet.Primary, 28);
            AddBox(model.Model, new Point3D(-0.36, 0.56, 0.76), new Vector3D(0.17, 0.17, 0.08), pet.Accent, 12);
            AddBox(model.Model, new Point3D(0.36, 0.56, 0.76), new Vector3D(0.17, 0.17, 0.08), pet.Accent, 12);
            AddBox(model.Model, new Point3D(0, 0.22, 0.78), new Vector3D(0.40, 0.06, 0.06), pet.Outline, 10);
            AddBox(model.Model, new Point3D(-0.62, -0.36, 0), new Vector3D(0.18, 0.50, 0.22), pet.Primary, 20);
            AddBox(model.Model, new Point3D(0.62, -0.36, 0), new Vector3D(0.18, 0.50, 0.22), pet.Primary, 20);
            AddBox(model.Model, new Point3D(-0.38, -1.13, 0.12), new Vector3D(0.34, 0.20, 0.42), pet.Primary, 18);
            AddBox(model.Model, new Point3D(0.38, -1.13, 0.12), new Vector3D(0.34, 0.20, 0.42), pet.Primary, 18);
            AddCylinder(model.Model, new Point3D(0, 1.27, 0), new Vector3D(0.06, 0.40, 0.06), pet.Outline, new Vector3D(0, 0, 1), 0);
            AddBox(model.Model, new Point3D(0, 1.50, 0), new Vector3D(0.13, 0.13, 0.13), pet.Accent, 20);
        }

        private static void BuildBunny(Pet3DModel model, PetDefinition pet)
        {
            AddSphere(model.Model, new Point3D(0, -0.46, -0.03), new Vector3D(0.88, 0.92, 0.74), pet.Secondary, 44);
            AddSphere(model.Model, new Point3D(0, 0.48, 0.08), new Vector3D(0.93, 0.80, 0.76), pet.Primary, 54);
            AddCylinder(model.Model, new Point3D(-0.36, 1.34, 0), new Vector3D(0.22, 1.00, 0.22), pet.Primary, new Vector3D(0, 0, 1), -7);
            AddCylinder(model.Model, new Point3D(0.36, 1.34, 0), new Vector3D(0.22, 1.00, 0.22), pet.Primary, new Vector3D(0, 0, 1), 7);
            AddCylinder(model.Model, new Point3D(-0.36, 1.36, 0.22), new Vector3D(0.10, 0.76, 0.10), pet.Accent, new Vector3D(0, 0, 1), -7);
            AddCylinder(model.Model, new Point3D(0.36, 1.36, 0.22), new Vector3D(0.10, 0.76, 0.10), pet.Accent, new Vector3D(0, 0, 1), 7);
            AddSphere(model.Model, new Point3D(-0.37, -1.12, 0.40), new Vector3D(0.35, 0.24, 0.42), pet.Primary, 28);
            AddSphere(model.Model, new Point3D(0.37, -1.12, 0.40), new Vector3D(0.35, 0.24, 0.42), pet.Primary, 28);
            AddSphere(model.Model, new Point3D(0.76, -0.56, -0.48), new Vector3D(0.34, 0.34, 0.34), pet.Primary, 38);
            AddFace(model, new Point3D(0, 0.47, 0.84), pet);
            AddSphere(model.Model, new Point3D(0, 0.18, 0.94), new Vector3D(0.12, 0.09, 0.08), pet.Accent, 18);
        }

        private static void AddFace(Pet3DModel model, Point3D center, PetDefinition pet)
        {
            AddEye(model, new Point3D(center.X - 0.29, center.Y + 0.08, center.Z), ToMedia(pet.Outline), 0.11, 0.15);
            AddEye(model, new Point3D(center.X + 0.29, center.Y + 0.08, center.Z), ToMedia(pet.Outline), 0.11, 0.15);
            AddSphere(model.Model, new Point3D(center.X, center.Y - 0.18, center.Z + 0.02), new Vector3D(0.11, 0.06, 0.06), ToMedia(pet.Outline), 14);
        }

        private static void AddEye(Pet3DModel model, Point3D center, System.Drawing.Color color, double x, double y)
        {
            AddEye(model, center, ToMedia(color), x, y);
        }

        private static void AddEye(Pet3DModel model, Point3D center, Color color, double x, double y)
        {
            ScaleTransform3D scale;
            AddSphere(model.Model, center, new Vector3D(x, y, 0.09), color, 18, 1.0, out scale);
            model.BlinkTargets.Add(scale);
        }

        private static void AddShadow(Model3DGroup group)
        {
            AddCylinder(group, new Point3D(0, -1.43, -0.22), new Vector3D(1.10, 0.05, 0.72), Color.FromArgb(70, 0, 0, 0), new Vector3D(0, 0, 1), 0, 0.75);
        }

        private static void AddSphere(Model3DGroup group, Point3D position, Vector3D scale, System.Drawing.Color color, double shine, double opacity = 1.0)
        {
            AddSphere(group, position, scale, ToMedia(color), shine, opacity);
        }

        private static void AddSphere(Model3DGroup group, Point3D position, Vector3D scale, Color color, double shine, double opacity = 1.0)
        {
            ScaleTransform3D ignored;
            AddSphere(group, position, scale, color, shine, opacity, out ignored);
        }

        private static void AddSphere(Model3DGroup group, Point3D position, Vector3D scale, Color color, double shine, double opacity, out ScaleTransform3D scaleTransform)
        {
            scaleTransform = new ScaleTransform3D(scale.X, scale.Y, scale.Z);
            var transforms = new Transform3DGroup();
            transforms.Children.Add(scaleTransform);
            transforms.Children.Add(new TranslateTransform3D(position.X, position.Y, position.Z));
            group.Children.Add(CreateModel(GetSphere(), color, shine, opacity, transforms));
        }

        private static void AddBox(Model3DGroup group, Point3D position, Vector3D scale, System.Drawing.Color color, double shine, Vector3D? axis = null, double angle = 0, double opacity = 1.0)
        {
            AddBox(group, position, scale, ToMedia(color), shine, axis, angle, opacity);
        }

        private static void AddBox(Model3DGroup group, Point3D position, Vector3D scale, Color color, double shine, Vector3D? axis = null, double angle = 0, double opacity = 1.0)
        {
            group.Children.Add(CreateModel(GetBox(), color, shine, opacity, CreateTransform(position, scale, axis, angle)));
        }

        private static void AddCone(Model3DGroup group, Point3D position, Vector3D scale, System.Drawing.Color color, Vector3D axis, double angle, double opacity = 1.0)
        {
            AddCone(group, position, scale, ToMedia(color), axis, angle, opacity);
        }

        private static void AddCone(Model3DGroup group, Point3D position, Vector3D scale, Color color, Vector3D axis, double angle, double opacity = 1.0)
        {
            group.Children.Add(CreateModel(GetCone(), color, 34, opacity, CreateTransform(position, scale, axis, angle)));
        }

        private static void AddCylinder(Model3DGroup group, Point3D position, Vector3D scale, System.Drawing.Color color, Vector3D axis, double angle, double opacity = 1.0)
        {
            AddCylinder(group, position, scale, ToMedia(color), axis, angle, opacity);
        }

        private static void AddCylinder(Model3DGroup group, Point3D position, Vector3D scale, Color color, Vector3D axis, double angle, double opacity = 1.0)
        {
            group.Children.Add(CreateModel(GetCylinder(), color, 32, opacity, CreateTransform(position, scale, axis, angle)));
        }

        private static GeometryModel3D CreateModel(MeshGeometry3D mesh, Color color, double shine, double opacity, Transform3D transform)
        {
            var diffuseColor = Color.FromArgb((byte)Math.Max(0, Math.Min(255, opacity * 255)), color.R, color.G, color.B);
            var diffuseBrush = new SolidColorBrush(diffuseColor);
            var specularBrush = new SolidColorBrush(Color.FromArgb((byte)(110 * opacity), 255, 255, 255));
            if (diffuseBrush.CanFreeze) diffuseBrush.Freeze();
            if (specularBrush.CanFreeze) specularBrush.Freeze();

            var material = new MaterialGroup();
            material.Children.Add(new DiffuseMaterial(diffuseBrush));
            material.Children.Add(new SpecularMaterial(specularBrush, Math.Max(1, shine)));
            if (material.CanFreeze) material.Freeze();

            return new GeometryModel3D
            {
                Geometry = mesh,
                Material = material,
                BackMaterial = material,
                Transform = transform
            };
        }

        private static Transform3D CreateTransform(Point3D position, Vector3D scale, Vector3D? axis, double angle)
        {
            var transforms = new Transform3DGroup();
            transforms.Children.Add(new ScaleTransform3D(scale.X, scale.Y, scale.Z));
            if (axis.HasValue && Math.Abs(angle) > 0.001)
                transforms.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(axis.Value, angle)));
            transforms.Children.Add(new TranslateTransform3D(position.X, position.Y, position.Z));
            return transforms;
        }

        private static Color ToMedia(System.Drawing.Color color)
        {
            return Color.FromArgb(color.A, color.R, color.G, color.B);
        }

        private static MeshGeometry3D GetSphere()
        {
            lock (MeshLock)
            {
                if (_sphere == null) _sphere = CreateSphereMesh(18, 26);
                return _sphere;
            }
        }

        private static MeshGeometry3D GetBox()
        {
            lock (MeshLock)
            {
                if (_box == null) _box = CreateBoxMesh();
                return _box;
            }
        }

        private static MeshGeometry3D GetCone()
        {
            lock (MeshLock)
            {
                if (_cone == null) _cone = CreateConeMesh(24);
                return _cone;
            }
        }

        private static MeshGeometry3D GetCylinder()
        {
            lock (MeshLock)
            {
                if (_cylinder == null) _cylinder = CreateCylinderMesh(24);
                return _cylinder;
            }
        }

        private static MeshGeometry3D CreateSphereMesh(int latitude, int longitude)
        {
            var mesh = new MeshGeometry3D();
            for (var lat = 0; lat <= latitude; lat++)
            {
                var theta = Math.PI * lat / latitude;
                var sinTheta = Math.Sin(theta);
                var cosTheta = Math.Cos(theta);
                for (var lon = 0; lon <= longitude; lon++)
                {
                    var phi = 2 * Math.PI * lon / longitude;
                    var x = sinTheta * Math.Cos(phi);
                    var y = cosTheta;
                    var z = sinTheta * Math.Sin(phi);
                    mesh.Positions.Add(new Point3D(x, y, z));
                    mesh.Normals.Add(new Vector3D(x, y, z));
                    mesh.TextureCoordinates.Add(new Point((double)lon / longitude, (double)lat / latitude));
                }
            }

            for (var lat = 0; lat < latitude; lat++)
            {
                for (var lon = 0; lon < longitude; lon++)
                {
                    var first = lat * (longitude + 1) + lon;
                    var second = first + longitude + 1;
                    mesh.TriangleIndices.Add(first);
                    mesh.TriangleIndices.Add(second);
                    mesh.TriangleIndices.Add(first + 1);
                    mesh.TriangleIndices.Add(second);
                    mesh.TriangleIndices.Add(second + 1);
                    mesh.TriangleIndices.Add(first + 1);
                }
            }
            if (mesh.CanFreeze) mesh.Freeze();
            return mesh;
        }

        private static MeshGeometry3D CreateBoxMesh()
        {
            var mesh = new MeshGeometry3D();
            AddFace(mesh, new Point3D(-1, -1, 1), new Point3D(1, -1, 1), new Point3D(1, 1, 1), new Point3D(-1, 1, 1), new Vector3D(0, 0, 1));
            AddFace(mesh, new Point3D(1, -1, -1), new Point3D(-1, -1, -1), new Point3D(-1, 1, -1), new Point3D(1, 1, -1), new Vector3D(0, 0, -1));
            AddFace(mesh, new Point3D(-1, -1, -1), new Point3D(-1, -1, 1), new Point3D(-1, 1, 1), new Point3D(-1, 1, -1), new Vector3D(-1, 0, 0));
            AddFace(mesh, new Point3D(1, -1, 1), new Point3D(1, -1, -1), new Point3D(1, 1, -1), new Point3D(1, 1, 1), new Vector3D(1, 0, 0));
            AddFace(mesh, new Point3D(-1, 1, 1), new Point3D(1, 1, 1), new Point3D(1, 1, -1), new Point3D(-1, 1, -1), new Vector3D(0, 1, 0));
            AddFace(mesh, new Point3D(-1, -1, -1), new Point3D(1, -1, -1), new Point3D(1, -1, 1), new Point3D(-1, -1, 1), new Vector3D(0, -1, 0));
            if (mesh.CanFreeze) mesh.Freeze();
            return mesh;
        }

        private static void AddFace(MeshGeometry3D mesh, Point3D a, Point3D b, Point3D c, Point3D d, Vector3D normal)
        {
            var start = mesh.Positions.Count;
            mesh.Positions.Add(a); mesh.Positions.Add(b); mesh.Positions.Add(c); mesh.Positions.Add(d);
            for (var i = 0; i < 4; i++) mesh.Normals.Add(normal);
            mesh.TextureCoordinates.Add(new Point(0, 1));
            mesh.TextureCoordinates.Add(new Point(1, 1));
            mesh.TextureCoordinates.Add(new Point(1, 0));
            mesh.TextureCoordinates.Add(new Point(0, 0));
            mesh.TriangleIndices.Add(start); mesh.TriangleIndices.Add(start + 1); mesh.TriangleIndices.Add(start + 2);
            mesh.TriangleIndices.Add(start); mesh.TriangleIndices.Add(start + 2); mesh.TriangleIndices.Add(start + 3);
        }

        private static MeshGeometry3D CreateConeMesh(int segments)
        {
            var mesh = new MeshGeometry3D();
            var tipIndex = 0;
            mesh.Positions.Add(new Point3D(0, 1, 0));
            mesh.Normals.Add(new Vector3D(0, 1, 0));
            mesh.TextureCoordinates.Add(new Point(0.5, 0));
            for (var i = 0; i <= segments; i++)
            {
                var angle = 2 * Math.PI * i / segments;
                var x = Math.Cos(angle);
                var z = Math.Sin(angle);
                mesh.Positions.Add(new Point3D(x, -1, z));
                var normal = new Vector3D(x, 0.5, z);
                normal.Normalize();
                mesh.Normals.Add(normal);
                mesh.TextureCoordinates.Add(new Point((double)i / segments, 1));
            }
            for (var i = 0; i < segments; i++)
            {
                mesh.TriangleIndices.Add(tipIndex);
                mesh.TriangleIndices.Add(i + 1);
                mesh.TriangleIndices.Add(i + 2);
            }

            var center = mesh.Positions.Count;
            mesh.Positions.Add(new Point3D(0, -1, 0));
            mesh.Normals.Add(new Vector3D(0, -1, 0));
            mesh.TextureCoordinates.Add(new Point(0.5, 0.5));
            var ringStart = mesh.Positions.Count;
            for (var i = 0; i <= segments; i++)
            {
                var angle = 2 * Math.PI * i / segments;
                mesh.Positions.Add(new Point3D(Math.Cos(angle), -1, Math.Sin(angle)));
                mesh.Normals.Add(new Vector3D(0, -1, 0));
                mesh.TextureCoordinates.Add(new Point((Math.Cos(angle) + 1) / 2, (Math.Sin(angle) + 1) / 2));
            }
            for (var i = 0; i < segments; i++)
            {
                mesh.TriangleIndices.Add(center);
                mesh.TriangleIndices.Add(ringStart + i + 1);
                mesh.TriangleIndices.Add(ringStart + i);
            }
            if (mesh.CanFreeze) mesh.Freeze();
            return mesh;
        }

        private static MeshGeometry3D CreateCylinderMesh(int segments)
        {
            var mesh = new MeshGeometry3D();
            for (var i = 0; i <= segments; i++)
            {
                var angle = 2 * Math.PI * i / segments;
                var x = Math.Cos(angle);
                var z = Math.Sin(angle);
                mesh.Positions.Add(new Point3D(x, -1, z));
                mesh.Positions.Add(new Point3D(x, 1, z));
                var normal = new Vector3D(x, 0, z);
                mesh.Normals.Add(normal);
                mesh.Normals.Add(normal);
                mesh.TextureCoordinates.Add(new Point((double)i / segments, 1));
                mesh.TextureCoordinates.Add(new Point((double)i / segments, 0));
            }
            for (var i = 0; i < segments; i++)
            {
                var start = i * 2;
                mesh.TriangleIndices.Add(start);
                mesh.TriangleIndices.Add(start + 1);
                mesh.TriangleIndices.Add(start + 2);
                mesh.TriangleIndices.Add(start + 1);
                mesh.TriangleIndices.Add(start + 3);
                mesh.TriangleIndices.Add(start + 2);
            }
            AddCap(mesh, segments, -1, false);
            AddCap(mesh, segments, 1, true);
            if (mesh.CanFreeze) mesh.Freeze();
            return mesh;
        }

        private static void AddCap(MeshGeometry3D mesh, int segments, double y, bool top)
        {
            var center = mesh.Positions.Count;
            mesh.Positions.Add(new Point3D(0, y, 0));
            mesh.Normals.Add(new Vector3D(0, top ? 1 : -1, 0));
            mesh.TextureCoordinates.Add(new Point(0.5, 0.5));
            var ringStart = mesh.Positions.Count;
            for (var i = 0; i <= segments; i++)
            {
                var angle = 2 * Math.PI * i / segments;
                mesh.Positions.Add(new Point3D(Math.Cos(angle), y, Math.Sin(angle)));
                mesh.Normals.Add(new Vector3D(0, top ? 1 : -1, 0));
                mesh.TextureCoordinates.Add(new Point((Math.Cos(angle) + 1) / 2, (Math.Sin(angle) + 1) / 2));
            }
            for (var i = 0; i < segments; i++)
            {
                mesh.TriangleIndices.Add(center);
                if (top)
                {
                    mesh.TriangleIndices.Add(ringStart + i);
                    mesh.TriangleIndices.Add(ringStart + i + 1);
                }
                else
                {
                    mesh.TriangleIndices.Add(ringStart + i + 1);
                    mesh.TriangleIndices.Add(ringStart + i);
                }
            }
        }
    }
}
