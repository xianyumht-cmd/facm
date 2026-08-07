using System;
using System.Windows.Forms;
using FACM.Services;

namespace FACM.Pets
{
    internal static class AnimalPetManager
    {
        private static AnimalPetWindow _window;
        private static Action _clicked;
        private static Action _rightClicked;

        public static bool IsActive
        {
            get { return _window != null && !_window.IsDisposed && _window.Visible; }
        }

        public static string ActivePetId
        {
            get { return _window == null || _window.IsDisposed ? string.Empty : _window.PetId; }
        }

        public static void Activate(string petId, Action clicked, Action rightClicked)
        {
            EnsureUiThread();
            _clicked = clicked;
            _rightClicked = rightClicked;
            var definition = AnimalPetCatalog.Get(petId);

            if (_window == null || _window.IsDisposed)
            {
                _window = new AnimalPetWindow(definition);
                _window.PetClicked += HandleClicked;
                _window.PetRightClicked += HandleRightClicked;
                _window.FormClosed += HandleClosed;
                _window.Show();
                _window.ResetToPrimaryScreen();
                AppLog.Info("Built-in animal pet started: " + definition.Id);
                return;
            }

            _window.SetPet(definition);
            if (!_window.Visible) _window.Show();
            _window.TopMost = true;
            AppLog.Info("Built-in animal pet changed: " + definition.Id);
        }

        public static void ResetToPrimaryScreen()
        {
            EnsureUiThread();
            if (_window == null || _window.IsDisposed) return;
            _window.ResetToPrimaryScreen();
            AppLog.Info("Built-in animal pet reset to primary screen.");
        }

        public static void Stop()
        {
            EnsureUiThread();
            if (_window == null) return;
            var window = _window;
            _window = null;
            try
            {
                window.PetClicked -= HandleClicked;
                window.PetRightClicked -= HandleRightClicked;
                window.FormClosed -= HandleClosed;
                if (!window.IsDisposed) window.Close();
                window.Dispose();
            }
            catch (Exception exception)
            {
                AppLog.Info("Built-in animal pet stop skipped: " + exception.Message);
            }
            finally
            {
                _clicked = null;
                _rightClicked = null;
            }
        }

        private static void HandleClicked(object sender, EventArgs e)
        {
            var callback = _clicked;
            if (callback != null) callback();
        }

        private static void HandleRightClicked(object sender, EventArgs e)
        {
            var callback = _rightClicked;
            if (callback != null) callback();
        }

        private static void HandleClosed(object sender, FormClosedEventArgs e)
        {
            if (!ReferenceEquals(sender, _window)) return;
            _window = null;
        }

        private static void EnsureUiThread()
        {
            if (Application.MessageLoop) return;
            throw new InvalidOperationException("Animal pet operations must run on the FACM UI thread.");
        }
    }
}
