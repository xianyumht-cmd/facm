using System;
using System.Threading;
using System.Windows.Forms;
using FACM.Services;

namespace FACM.Pets
{
    internal static class AnimalPetManager
    {
        private static SpritePetWindow _window;
        private static VPetHostClient _host;
        private static Action _clicked;
        private static Action _rightClicked;
        private static Action _ready;
        private static int _uiThreadId;

        public static bool IsActive
        {
            get
            {
                if (_host != null && _host.IsActive) return true;
                return _window != null && !_window.IsDisposed;
            }
        }

        public static bool IsVisible
        {
            get
            {
                if (_host != null && _host.IsActive) return _host.IsVisible;
                return _window != null && !_window.IsDisposed && _window.Visible;
            }
        }

        public static string ActivePetId
        {
            get
            {
                if (_host != null && _host.IsActive) return _host.ActivePetId;
                return _window == null || _window.IsDisposed ? string.Empty : _window.PetId;
            }
        }

        public static void Activate(string petId, Action clicked, Action rightClicked, Action ready = null)
        {
            EnsureUiThread();
            _clicked = clicked;
            _rightClicked = rightClicked;
            _ready = ready;
            var definition = AnimalPetCatalog.Get(petId);

            if (definition.Runtime == AnimalPetRuntime.VPetCore)
            {
                StopSpriteWindow();
                if (_host == null) _host = new VPetHostClient();
                if (!_host.Activate(definition.Id, HandleHostClicked, HandleHostRightClicked, HandleHostReady))
                {
                    StopHost();
                    throw new InvalidOperationException("FACM.PetHost 不可用，已拒绝退回低清 Sprite 冒充高精度桌宠。");
                }
                AppLog.Info("VPet Core PetHost started: " + definition.Id);
                return;
            }

            StopHost();
            if (_window == null || _window.IsDisposed)
            {
                _window = new SpritePetWindow(definition);
                _window.PetClicked += HandleClicked;
                _window.PetRightClicked += HandleRightClicked;
                _window.FormClosed += HandleClosed;
                _window.Show();
                _window.ResetToPrimaryScreen();
                AppLog.Info(SpriteRuntimeName(definition) + " pet started: " + definition.Id);
                HandleReady();
                return;
            }

            _window.SetPet(definition);
            if (!_window.Visible) _window.Show();
            _window.TopMost = true;
            AppLog.Info(SpriteRuntimeName(definition) + " pet changed: " + definition.Id);
            HandleReady();
        }

        public static void SetVisible(bool visible)
        {
            EnsureUiThread();
            if (_host != null)
            {
                _host.SetVisible(visible);
                return;
            }

            if (_window == null || _window.IsDisposed) return;
            if (visible)
            {
                if (!_window.Visible) _window.Show();
                _window.TopMost = true;
            }
            else if (_window.Visible)
            {
                _window.Hide();
            }
        }

        public static void ResetToPrimaryScreen()
        {
            EnsureUiThread();
            if (_host != null && _host.IsActive)
            {
                _host.ResetToPrimaryScreen();
                AppLog.Info("VPet Core PetHost reset to primary screen.");
                return;
            }
            if (_window == null || _window.IsDisposed) return;
            var definition = AnimalPetCatalog.Get(_window.PetId);
            _window.ResetToPrimaryScreen();
            AppLog.Info(SpriteRuntimeName(definition) + " pet reset to primary screen.");
        }

        public static void Stop()
        {
            // Host disposal also runs after Application.Run returns. If no pet state was ever created,
            // stopping is a true no-op and must not manufacture a false UI-thread warning.
            if (_host == null && (_window == null || _window.IsDisposed) &&
                _clicked == null && _rightClicked == null && _ready == null)
                return;

            EnsureUiThread();
            StopHost();
            StopSpriteWindow();
            _clicked = null;
            _rightClicked = null;
            _ready = null;
        }

        private static string SpriteRuntimeName(AnimalPetDefinition definition)
        {
            return FlyingPetProfiles.IsManaged(definition) ? "Flying Runtime" : "Compatibility Sprite";
        }

        private static void StopHost()
        {
            if (_host == null) return;
            var host = _host;
            _host = null;
            try { host.Dispose(); }
            catch (Exception exception) { AppLog.Info("VPet PetHost stop skipped: " + exception.Message); }
        }

        private static void StopSpriteWindow()
        {
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
                AppLog.Info("Animated sprite pet stop skipped: " + exception.Message);
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

        private static void HandleHostClicked()
        {
            var callback = _clicked;
            if (callback != null) callback();
        }

        private static void HandleHostRightClicked()
        {
            var callback = _rightClicked;
            if (callback != null) callback();
        }

        private static void HandleHostReady()
        {
            HandleReady();
        }

        private static void HandleReady()
        {
            var callback = _ready;
            if (callback != null) callback();
        }

        private static void HandleClosed(object sender, FormClosedEventArgs e)
        {
            if (!ReferenceEquals(sender, _window)) return;
            _window = null;
        }

        private static void EnsureUiThread()
        {
            var currentThreadId = Thread.CurrentThread.ManagedThreadId;
            if (Application.MessageLoop)
            {
                Interlocked.CompareExchange(ref _uiThreadId, currentThreadId, 0);
                if (_uiThreadId == currentThreadId) return;
            }

            // Application.MessageLoop becomes false after the main loop exits even though module
            // disposal is still running on the same STA/UI thread. Remember that owner so shutdown
            // and self-update can close an active pet cleanly instead of logging a false skip.
            if (_uiThreadId != 0 && _uiThreadId == currentThreadId) return;
            throw new InvalidOperationException("Animal pet operations must run on the FACM UI thread.");
        }
    }
}
