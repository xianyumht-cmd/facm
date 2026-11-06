using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using FACM.AppHost;
using FACM.Mayhem;
using FACM.League;
using FACM.Theming;

namespace FACM.AppHost.Modules
{
    internal sealed class MayhemModule : IFacmModule
    {
        private static readonly IReadOnlyList<string> ModuleDependencies = new[]
        {
            LeagueClientModule.ModuleId
        };
        private readonly LeagueClientModule _leagueClient;

        public MayhemModule(LeagueClientModule leagueClient)
        {
            _leagueClient = leagueClient ?? throw new ArgumentNullException(nameof(leagueClient));
        }

        public const string ModuleId = "mayhem";

        public string Id
        {
            get { return ModuleId; }
        }

        public IReadOnlyList<string> Dependencies
        {
            get { return ModuleDependencies; }
        }

        public void Initialize()
        {
        }

        internal LeagueClientSession UnifiedLookupSession { get { return _leagueClient.CurrentSession; } }

        // Reuse the same lookup and enrichment chain as the native Mayhem form.
        internal async Task<MayhemChampionResult> ReadUnifiedGuideAsync(string champion, CancellationToken token)
        {
            var result = await OpggMayhemService.QueryAsync(champion, token);
            if (result == null || !string.IsNullOrWhiteSpace(result.ErrorMessage)) return result;
            await RiotGameDataService.EnrichAsync(result, _leagueClient, token);
            await MayhemRankedAugmentService.EnrichAsync(result, token);
            await MayhemDecisionLocalizationService.EnrichAsync(result, _leagueClient, token);
            return result;
        }

        // Send only bounded, locally encoded thumbnails to the offline WebView2 page.
        // URLs are consumed by the same native Riot/Mayhem image cache used by the existing card renderer.
        internal async Task<string> ReadUnifiedIconAsync(string reference, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(reference) || reference.Length > 512) return null;
            try
            {
                using (var original = await MayhemImageCache.GetAsync(reference, _leagueClient, token))
                {
                    if (original == null) return null;
                    using (var thumb = new Bitmap(48, 48))
                    {
                        using (var graphics = Graphics.FromImage(thumb))
                        {
                            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            graphics.DrawImage(original, new Rectangle(0, 0, 48, 48));
                        }
                        using (var bytes = new MemoryStream())
                        {
                            thumb.Save(bytes, ImageFormat.Png);
                            if (bytes.Length > 24576) return null;
                            return "data:image/png;base64," + Convert.ToBase64String(bytes.ToArray());
                        }
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { return null; }
        }

        public Form CreateLookupForm()
        {
            var form = new MayhemLookupForm(_leagueClient);
            FacmWindowChrome.Prepare(form);
            return form;
        }

        public void Dispose()
        {
        }
    }
}
