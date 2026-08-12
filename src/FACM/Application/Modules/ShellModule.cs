using System;
using System.Collections.Generic;

namespace FACM.Application.Modules
{
    internal sealed class ShellModule : IFacmModule
    {
        private readonly bool _startCleanup;
        private readonly SettingsModule _settings;
        private readonly ToolsModule _tools;
        private readonly PetsModule _pets;
        private readonly OnlineModule _online;
        private readonly MayhemModule _mayhem;
        private readonly IReadOnlyList<string> _dependencies;

        public ShellModule(
            bool startCleanup,
            SettingsModule settings,
            ToolsModule tools,
            PetsModule pets,
            OnlineModule online,
            MayhemModule mayhem)
        {
            _startCleanup = startCleanup;
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _tools = tools ?? throw new ArgumentNullException(nameof(tools));
            _pets = pets ?? throw new ArgumentNullException(nameof(pets));
            _online = online ?? throw new ArgumentNullException(nameof(online));
            _mayhem = mayhem ?? throw new ArgumentNullException(nameof(mayhem));
            _dependencies = new[]
            {
                CompactMenuEnhancerModule.ModuleId,
                SettingsModule.ModuleId,
                ToolsModule.ModuleId,
                PetsModule.ModuleId,
                OnlineModule.ModuleId,
                MayhemModule.ModuleId
            };
        }

        public const string ModuleId = "shell";

        public string Id
        {
            get { return ModuleId; }
        }

        public IReadOnlyList<string> Dependencies
        {
            get { return _dependencies; }
        }

        public MainForm MainForm { get; private set; }

        public void Initialize()
        {
            if (_settings.Settings == null || _settings.UiText == null)
                throw new InvalidOperationException("Settings module must initialize before shell.");

            MainForm = new MainForm(
                _settings.Settings,
                _settings.UiText,
                _tools,
                _pets,
                _online,
                _mayhem,
                _startCleanup);
        }

        public void Dispose()
        {
            var form = MainForm;
            MainForm = null;
            if (form == null || form.IsDisposed) return;
            form.Dispose();
        }
    }
}
