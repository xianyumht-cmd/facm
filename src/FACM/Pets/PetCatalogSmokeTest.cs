using System;
using System.Collections.Generic;

namespace FACM.Pets
{
    internal static class PetCatalogSmokeTest
    {
        public static void Validate()
        {
            if (PetCatalog.All == null || PetCatalog.All.Count != 10)
                throw new InvalidOperationException("Expected exactly 10 VRM desktop pets.");

            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var personas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var modelUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var pet in PetCatalog.All)
            {
                if (pet == null) throw new InvalidOperationException("Pet catalog contains a null entry.");
                if (string.IsNullOrWhiteSpace(pet.Id) || !ids.Add(pet.Id))
                    throw new InvalidOperationException("Duplicate or empty pet id: " + pet.Id);
                if (string.IsNullOrWhiteSpace(pet.Name))
                    throw new InvalidOperationException("Pet has no display name: " + pet.Id);
                if (string.IsNullOrWhiteSpace(pet.PersonaId) || !personas.Add(pet.PersonaId))
                    throw new InvalidOperationException("Duplicate or empty persona id: " + pet.Id);
                if (string.IsNullOrWhiteSpace(pet.AssetId) || !pet.AssetId.StartsWith("vrm:facm:", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Invalid VRM asset id: " + pet.Id);
                if (!IsHttps(pet.ModelUrl) || !modelUrls.Add(pet.ModelUrl))
                    throw new InvalidOperationException("Invalid or duplicate VRM URL: " + pet.Id);
                if (!IsHttps(pet.ThumbnailUrl))
                    throw new InvalidOperationException("Invalid thumbnail URL: " + pet.Id);
                if (string.IsNullOrWhiteSpace(pet.License) || pet.License.IndexOf("CC0", StringComparison.OrdinalIgnoreCase) < 0)
                    throw new InvalidOperationException("Pet model is missing the expected CC0 license marker: " + pet.Id);
            }

            var defaultPet = PetCatalog.Get(PetCatalog.DefaultPetId);
            if (defaultPet == null || !string.Equals(defaultPet.Id, PetCatalog.DefaultPetId, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Default pet id does not resolve.");
        }

        private static bool IsHttps(string value)
        {
            Uri uri;
            return Uri.TryCreate(value, UriKind.Absolute, out uri) &&
                   string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        }
    }
}
