using System;
using System.Collections.Generic;
using FACM.AppHost;

namespace FACM.AppHost.Modules
{
    internal sealed class ShellModule : IFacmModule
    {
        private static readonly IReadOnlyList<string> ModuleDependencies = new[]
        {
            CompactMenuEnhancerModule.ModuleId,
            SettingsModule.ModuleId,
            ToolsModule.ModuleId,
            OnlineModule.ModuleId,
            PetsModule.ModuleId,
            MayhemModule.ModuleId
        };
        private readonly bool _startCleanup;
        private readonly SettingsModule _settings;
        private readonly ToolsModule _tools;
        private readonly OnlineModule _online;
        private readonly PetsModule _pets;
        private readonly MayhemModule _mayhem;

        public ShellModule(
            bool startCleanup,
            SettingsModule settings,
            ToolsModule tools,
            OnlineModule online,
            PetsModule pets,
            MayhemModule mayhem)
        {
            _startCleanup = startCleanup;
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _tools = tools ?? throw new ArgumentNullException(nameof(tools));
            _online = online ?? throw new ArgumentNullException(nameof(online));
            _pets = pets ?? throw new ArgumentNullException(nameof(pets));
            _mayhem = mayhem ?? throw new ArgumentNullException(nameof(mayhem));
        }

        public const string ModuleId = "shell";

        public string Id
        {
            get { return ModuleId; }
        }

        public IReadOnlyList<string> Dependencies
        {
            get { return ModuleDependencies; }
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
                _online,
                _pets,
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
