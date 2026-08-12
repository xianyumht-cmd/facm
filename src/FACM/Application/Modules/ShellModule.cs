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
            SettingsModule.ModuleId
        };
        private readonly bool _startCleanup;
        private readonly SettingsModule _settings;

        public ShellModule(bool startCleanup, SettingsModule settings)
        {
            _startCleanup = startCleanup;
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
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

            MainForm = new MainForm(_settings.Settings, _settings.UiText, _startCleanup);
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
