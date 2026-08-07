using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

namespace FACM.Pets
{
    internal sealed class Pet3DScene : Grid, IDisposable
    {
        private readonly Viewport3D _viewport;
        private readonly PerspectiveCamera _camera;
        private readonly ModelVisual3D _petVisual;
        private readonly DispatcherTimer _timer;
        private readonly Transform3DGroup _rootTransforms;
        private readonly ScaleTransform3D _scale;
        private readonly AxisAngleRotation3D _yaw;
        private readonly AxisAngleRotation3D _pitch;
        private readonly TranslateTransform3D _translation;
        private Pet3DModel _petModel;
        private PetDefinition _pet;
        private double _phase;
        private double _hover;
        private double _pointerX;
        private double _pointerY;
        private bool _disposed;

        public Pet3DScene(PetDefinition pet)
        {
            Background = Brushes.Transparent;
            ClipToBounds = false;
            SnapsToDevicePixels = true;

            _camera = new PerspectiveCamera
            {
                Position = new Point3D(0, 0.18, 6.6),
                LookDirection = new Vector3D(0, -0.05, -6.6),
                UpDirection = new Vector3D(0, 1, 0),
                FieldOfView = 34
            };

            _viewport = new Viewport3D
            {
                Camera = _camera,
                ClipToBounds = false,
                IsHitTestVisible = false
            };

            var lightGroup = new Model3DGroup();
            lightGroup.Children.Add(new AmbientLight(Color.FromRgb(96, 100, 116)));
            lightGroup.Children.Add(new DirectionalLight(Color.FromRgb(255, 252, 244), new Vector3D(-0.55, -0.70, -1.20)));
            lightGroup.Children.Add(new DirectionalLight(Color.FromRgb(130, 170, 255), new Vector3D(0.80, 0.25, -0.65)));
            _viewport.Children.Add(new ModelVisual3D { Content = lightGroup });

            _scale = new ScaleTransform3D(1, 1, 1);
            _yaw = new AxisAngleRotation3D(new Vector3D(0, 1, 0), 0);
            _pitch = new AxisAngleRotation3D(new Vector3D(1, 0, 0), 0);
            _translation = new TranslateTransform3D(0, 0, 0);
            _rootTransforms = new Transform3DGroup();
            _rootTransforms.Children.Add(_scale);
            _rootTransforms.Children.Add(new RotateTransform3D(_pitch));
            _rootTransforms.Children.Add(new RotateTransform3D(_yaw));
            _rootTransforms.Children.Add(_translation);

            _petVisual = new ModelVisual3D();
            _viewport.Children.Add(_petVisual);
            Children.Add(_viewport);

            SetPet(pet);

            _timer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(33)
            };
            _timer.Tick += Animate;
            _timer.Start();
            Unloaded += delegate { if (!_disposed) _timer.Stop(); };
            Loaded += delegate { if (!_disposed && !_timer.IsEnabled) _timer.Start(); };
        }

        public void SetPet(PetDefinition pet)
        {
            _pet = pet ?? PetCatalog.Get(PetCatalog.DefaultPetId);
            _petModel = Pet3DModelFactory.Create(_pet);
            _petModel.Model.Transform = _rootTransforms;
            _petVisual.Content = _petModel.Model;
            _phase = 0;
            _pointerX = 0;
            _pointerY = 0;
        }

        public void SetHover(bool hovered)
        {
            _hover = hovered ? 1.0 : 0.0;
        }

        public void SetPointer(double normalizedX, double normalizedY)
        {
            _pointerX = Clamp(normalizedX, -1, 1);
            _pointerY = Clamp(normalizedY, -1, 1);
        }

        private void Animate(object sender, EventArgs e)
        {
            if (_disposed || _petModel == null) return;

            _phase += 0.055;
            var bob = Math.Sin(_phase * 1.85) * 0.075;
            var breathe = 1.0 + Math.Sin(_phase * 1.35) * 0.018 + _hover * 0.035;
            var idleYaw = Math.Sin(_phase * 0.72) * 7.0;
            var idlePitch = Math.Sin(_phase * 0.53) * 2.2;

            _translation.OffsetY = bob;
            _scale.ScaleX = breathe;
            _scale.ScaleY = breathe;
            _scale.ScaleZ = breathe;
            _yaw.Angle = idleYaw + _pointerX * 12.0;
            _pitch.Angle = idlePitch - _pointerY * 7.0;

            var blink = Math.Sin(_phase * 0.43) > 0.985;
            foreach (var eye in _petModel.BlinkTargets)
                eye.ScaleY = blink ? 0.10 : 1.0;

            _camera.Position = new Point3D(_pointerX * 0.14, 0.18 - _pointerY * 0.08, 6.6);
            _camera.LookDirection = new Vector3D(-_pointerX * 0.10, -0.05 + _pointerY * 0.05, -6.6);
        }

        private static double Clamp(double value, double min, double max)
        {
            return Math.Max(min, Math.Min(max, value));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _timer.Stop();
            _timer.Tick -= Animate;
        }
    }
}
