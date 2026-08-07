using System;
using System.Windows;
using System.Windows.Input;
using WpfCursor = System.Windows.Forms.Cursor;

namespace FACM.Pets
{
    internal sealed class Pet3DWindow : Window, IDisposable
    {
        private readonly Pet3DScene _scene;
        private bool _dragging;
        private bool _moved;
        private System.Drawing.Point _dragCursor;
        private Point _dragWindow;
        private bool _disposed;

        public Pet3DWindow(PetDefinition pet)
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = System.Windows.Media.Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;
            ShowActivated = false;
            Focusable = false;
            SizeToContent = SizeToContent.Manual;
            Width = pet.Size.Width;
            Height = pet.Size.Height;
            MinWidth = MaxWidth = Width;
            MinHeight = MaxHeight = Height;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;

            _scene = new Pet3DScene(pet);
            Content = _scene;

            PreviewMouseLeftButtonDown += BeginDrag;
            PreviewMouseMove += ContinueDrag;
            PreviewMouseLeftButtonUp += EndDrag;
            PreviewMouseRightButtonUp += ShowContextMenu;
            MouseEnter += delegate { _scene.SetHover(true); };
            MouseLeave += delegate { _scene.SetHover(false); };
            MouseMove += UpdatePointer;
            Closed += delegate { Dispose(); };
        }

        public event EventHandler Clicked;
        public event EventHandler DragStarted;
        public event EventHandler DragFinished;
        public event EventHandler ContextMenuRequested;

        public void SetPet(PetDefinition pet)
        {
            var centerX = Left + Width / 2.0;
            var centerY = Top + Height / 2.0;
            Width = pet.Size.Width;
            Height = pet.Size.Height;
            MinWidth = MaxWidth = Width;
            MinHeight = MaxHeight = Height;
            Left = centerX - Width / 2.0;
            Top = centerY - Height / 2.0;
            _scene.SetPet(pet);
        }

        public void SetScreenPosition(double left, double top)
        {
            Left = left;
            Top = top;
        }

        private void BeginDrag(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;
            _dragging = true;
            _moved = false;
            _dragCursor = WpfCursor.Position;
            _dragWindow = new Point(Left, Top);
            CaptureMouse();
            var handler = DragStarted;
            if (handler != null) handler(this, EventArgs.Empty);
            e.Handled = true;
        }

        private void ContinueDrag(object sender, MouseEventArgs e)
        {
            if (!_dragging || e.LeftButton != MouseButtonState.Pressed) return;
            var cursor = WpfCursor.Position;
            var deltaX = cursor.X - _dragCursor.X;
            var deltaY = cursor.Y - _dragCursor.Y;
            if (Math.Abs(deltaX) + Math.Abs(deltaY) > 4) _moved = true;
            Left = _dragWindow.X + deltaX;
            Top = _dragWindow.Y + deltaY;
        }

        private void EndDrag(object sender, MouseButtonEventArgs e)
        {
            if (!_dragging || e.ChangedButton != MouseButton.Left) return;
            _dragging = false;
            ReleaseMouseCapture();

            if (_moved)
            {
                var handler = DragFinished;
                if (handler != null) handler(this, EventArgs.Empty);
            }
            else
            {
                var handler = Clicked;
                if (handler != null) handler(this, EventArgs.Empty);
            }
            e.Handled = true;
        }

        private void ShowContextMenu(object sender, MouseButtonEventArgs e)
        {
            var handler = ContextMenuRequested;
            if (handler != null) handler(this, EventArgs.Empty);
            e.Handled = true;
        }

        private void UpdatePointer(object sender, MouseEventArgs e)
        {
            var position = e.GetPosition(this);
            var x = ActualWidth <= 1 ? 0 : position.X / ActualWidth * 2.0 - 1.0;
            var y = ActualHeight <= 1 ? 0 : position.Y / ActualHeight * 2.0 - 1.0;
            _scene.SetPointer(x, y);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _scene.Dispose();
        }
    }
}
