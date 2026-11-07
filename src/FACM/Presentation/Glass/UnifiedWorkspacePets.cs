using System;
using System.Linq;
using System.Web.Script.Serialization;
using FACM.Pets;

namespace FACM.Presentation.Glass
{
    internal sealed partial class UnifiedWorkspaceForm
    {
        internal void SyncUnifiedPetOptions()
        {
            if (!_ready || _closing || IsDisposed || _navigation.Current != "appearance" ||
                _browser.CoreWebView2 == null) return;
            var current = AnimalPetCatalog.Get(_settings.PetStyleId);
            _browser.CoreWebView2.PostWebMessageAsJson(new JavaScriptSerializer().Serialize(new
            {
                type = "pets",
                selected = current.Id,
                name = current.Name,
                enabled = _settings.AnimalPetEnabled,
                items = AnimalPetCatalog.Visible.Select(item => new
                {
                    id = item.Id,
                    name = item.Name,
                    description = item.Description
                }).ToArray()
            }));
        }

        internal void ApplyUnifiedPetChoice(string id)
        {
            if (_navigation.Current != "appearance" || id == null || id.Length > 48) return;
            var status = _ownerBall.ApplyUnifiedPetSelection(id);
            PostUnifiedPetResult(status);
            SyncUnifiedPetOptions();
        }

        internal void ApplyUnifiedPetAction(string action)
        {
            if (_navigation.Current != "appearance") return;
            var status = _ownerBall.ApplyUnifiedPetAction(action);
            PostUnifiedPetResult(status);
            SyncUnifiedPetOptions();
        }

        private void PostUnifiedPetResult(string status)
        {
            if (!_ready || IsDisposed || _closing || _navigation.Current != "appearance" ||
                _browser.CoreWebView2 == null) return;
            _browser.CoreWebView2.PostWebMessageAsJson(new JavaScriptSerializer().Serialize(new
            {
                type = "pet-result", status
            }));
        }
    }
}
