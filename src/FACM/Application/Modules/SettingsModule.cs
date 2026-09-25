using System;
using System.Collections.Generic;
using System.IO;
using FACM.AppHost;
using FACM.Services;
using FACM.Theming;

namespace FACM.AppHost.Modules
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

        internal bool WasSettingsCreatedThisRun { get; private set; }

        public void Initialize()
        {
            RuntimePaths.Initialize();
            WasSettingsCreatedThisRun = !File.Exists(RuntimePaths.SettingsPath);
            Settings = AppSettings.Load();
            FacmThemeRuntime.Initialize(Settings.ThemeId);
            UiText = UiTextCatalog.Load();
        }

        public void Dispose()
        {
        }
    }
}