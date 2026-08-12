using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FACM.AppHost;
using FACM.Pets;
using FACM.Services;

namespace FACM.AppHost.Modules
{
    internal sealed class PetsModule : IFacmModule
    {
        private static readonly IReadOnlyList<string> NoDependencies = Array.Empty<string>();

        public const string ModuleId = "pets";

        public string Id
        {
            get { return ModuleId; }
        }

        public IReadOnlyList<string> Dependencies
        {
            get { return NoDependencies; }
        }

        public bool IsActive
        {
            get { return AnimalPetManager.IsActive; }
        }

        public void Initialize()
        {
        }

        public Task WarmupAsync()
        {
            return PetHostBundleLoader.BeginWarmup();
        }

        public void Activate(string petId, Action clicked, Action rightClicked, Action ready = null)
        {
            AnimalPetManager.Activate(petId, clicked, rightClicked, ready);
        }

        public void ResetToPrimaryScreen()
        {
            AnimalPetManager.ResetToPrimaryScreen();
        }

        public void Stop()
        {
            AnimalPetManager.Stop();
        }

        public void Dispose()
        {
            try
            {
                Stop();
            }
            catch (Exception exception)
            {
                AppLog.Info("Pets module stop skipped: " + exception.Message);
            }
        }
    }
}
