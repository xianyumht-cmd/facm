using System;
using System.Collections.Generic;
using FACM.Services;

namespace FACM.AppHost.Modules
{
    internal sealed class GgmanAutoSyncModule : IFacmModule
    {
        private readonly SettingsModule _settings;
        private GgmanAutoSyncService _service;

        internal GgmanAutoSyncModule(SettingsModule settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public string Id { get { return "ggman-auto-sync"; } }
        public IReadOnlyList<string> Dependencies
        {
            get { return new[] { SettingsModule.ModuleId }; }
        }

        public void Initialize()
        {
            if (_settings.Settings == null)
                throw new InvalidOperationException("Application settings must be loaded first.");
            _service = new GgmanAutoSyncService(_settings.Settings);
        }

        public void Dispose()
        {
            if (_service != null) _service.Dispose();
            _service = null;
        }
    }
}
