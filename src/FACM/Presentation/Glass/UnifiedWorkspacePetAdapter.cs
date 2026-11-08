using System;
using System.Linq;
using System.Web.Script.Serialization;
using FACM.Pets;
using FACM.Services;

namespace FACM.Presentation.Glass
{
    internal sealed class UnifiedWorkspacePetAdapter
    {
        private readonly Func<bool> _canPublish;
        private readonly Action<string> _publish;
        private readonly Func<string, string> _select;
        private readonly Func<string, string> _act;
        private readonly AppSettings _settings;

        internal UnifiedWorkspacePetAdapter(Func<bool> canPublish, Action<string> publish,
            Func<string, string> select, Func<string, string> act, AppSettings settings)
        {
            _canPublish = canPublish ?? throw new ArgumentNullException(nameof(canPublish));
            _publish = publish ?? throw new ArgumentNullException(nameof(publish));
            _select = select ?? throw new ArgumentNullException(nameof(select));
            _act = act ?? throw new ArgumentNullException(nameof(act));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        internal void Sync()
        {
            if (_canPublish()) _publish(Serialize(_settings.PetStyleId, _settings.AnimalPetEnabled));
        }

        internal void Select(string id)
        {
            if (!_canPublish() || !IsSelectable(id)) return;
            PostResult(_select(id));
            Sync();
        }

        internal void Act(string action)
        {
            if (!_canPublish() || (action != "disable" && action != "reset")) return;
            PostResult(_act(action));
            Sync();
        }

        private void PostResult(string status)
        {
            if (_canPublish()) _publish(new JavaScriptSerializer().Serialize(new
            { type = "pet-result", status }));
        }

        private static bool IsSelectable(string id)
        {
            return id != null && id.Length <= 48 && AnimalPetCatalog.Visible.Any(item =>
                string.Equals(item.Id, id, StringComparison.Ordinal));
        }

        private static string Serialize(string selected, bool enabled)
        {
            var current = AnimalPetCatalog.Get(selected);
            return new JavaScriptSerializer().Serialize(new
            {
                type = "pets", selected = current.Id, name = current.Name, enabled,
                items = AnimalPetCatalog.Visible.Select(item => new
                {
                    id = item.Id, name = item.Name, description = item.Description
                }).ToArray()
            });
        }

        internal static void ValidateForSmokeTest()
        {
            if (!IsSelectable(AnimalPetCatalog.DefaultPetId) || IsSelectable("invalid") ||
                IsSelectable(null) || IsSelectable(new string('x', 49)))
                throw new InvalidOperationException("Desktop pet choice validation changed.");
            var payload = Serialize(AnimalPetCatalog.DefaultPetId, false);
            if (!payload.Contains("\"type\":\"pets\"") ||
                !payload.Contains("\"enabled\":false") ||
                !payload.Contains("\"selected\":\"" + AnimalPetCatalog.DefaultPetId + "\""))
                throw new InvalidOperationException("Desktop pet catalog projection changed.");
            var calls = 0;
            var hidden = new UnifiedWorkspacePetAdapter(() => false,
                delegate { throw new InvalidOperationException("Hidden pet status published."); },
                delegate { calls++; return "enabled"; }, delegate { calls++; return "disabled"; },
                new AppSettings());
            hidden.Select(AnimalPetCatalog.DefaultPetId);
            hidden.Act("disable");
            hidden.Sync();
            if (calls != 0) throw new InvalidOperationException("Hidden desktop pet reached the native owner.");
            var events = 0;
            var visible = new UnifiedWorkspacePetAdapter(() => true,
                delegate(string json)
                {
                    if (json.Contains("\"type\":\"pet-result\"")) events++;
                }, delegate { calls++; return "enabled"; },
                delegate { calls++; return "disabled"; }, new AppSettings());
            visible.Select("invalid");
            visible.Act("start");
            visible.Select(AnimalPetCatalog.DefaultPetId);
            visible.Act("disable");
            if (calls != 2 || events != 2)
                throw new InvalidOperationException("Desktop pet command allowlist or status contract changed.");
        }
    }
}
