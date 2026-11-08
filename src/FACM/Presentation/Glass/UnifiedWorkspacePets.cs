namespace FACM.Presentation.Glass
{
    internal sealed partial class UnifiedWorkspaceForm
    {
        private UnifiedWorkspacePetAdapter _petAdapter;

        private UnifiedWorkspacePetAdapter PetAdapter
        {
            get
            {
                if (_petAdapter == null)
                    _petAdapter = new UnifiedWorkspacePetAdapter(
                        delegate { return CanPublishOnRoute("appearance"); },
                        delegate(string json) { _browser.CoreWebView2.PostWebMessageAsJson(json); },
                        delegate(string id) { return _ownerBall.ApplyUnifiedPetSelection(id); },
                        delegate(string action) { return _ownerBall.ApplyUnifiedPetAction(action); },
                        _settings);
                return _petAdapter;
            }
        }

        internal void SyncUnifiedPetOptions() { PetAdapter.Sync(); }
        internal void ApplyUnifiedPetChoice(string id) { PetAdapter.Select(id); }
        internal void ApplyUnifiedPetAction(string action) { PetAdapter.Act(action); }
    }
}
