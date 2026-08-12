using System;
using System.Collections.Generic;
using FACM.Services;

namespace FACM.Application.Modules
{
    internal sealed class SettingsModule : IFacmModule
    {
        private static readonly IReadOnlyList<string> NoDependencies = Array.Empty<string>();

        public const string ModuleId = "settings";

        public string Id
        {
            get { return ModuleId; }
        }

        public IReadOnlyList<string> Dependencies
        {
            get { return NoDependencies; }
        }

        public AppSettings Settings { get; private set; }

        public UiTextCatalog UiText { get; private set; }

        public void Initialize()
        {
            Settings = AppSettings.Load();
            UiText = UiTextCatalog.Load();
        }

        public void Dispose()
        {
        }
    }
}
