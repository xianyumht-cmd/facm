using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using FACM.Services;

namespace FACM.League
{
    internal sealed class LeagueFriendActivity
    {
        public string DisplayName { get; set; }
        public string Availability { get; set; }
        public string GameStatus { get; set; }
        public string GameMode { get; set; }
        public string QueueType { get; set; }
        public int ChampionId { get; set; }
        public int Level { get; set; }
        public bool JoinableParty { get; set; }

        public bool IsPlaying
        {
            get { return string.Equals(GameStatus, "inGame", StringComparison.OrdinalIgnoreCase); }
        }
    }

    internal sealed class LeagueFriendActivitySnapshot
    {
        public static readonly LeagueFriendActivitySnapshot Unavailable =
            new LeagueFriendActivitySnapshot { Available = false };

        public bool Available { get; set; }
        public int OnlineCount { get; set; }
        public int PlayingCount { get; set; }
        public int JoinableCount { get; set; }
        public IReadOnlyList<LeagueFriendActivity> Friends { get; set; }

        public string Summary
        {
            get
            {
                if (!Available) return "好友状态不可用";
                return "在线 " + OnlineCount + " · 游戏中 " + PlayingCount + " · 可加入 " + JoinableCount;
            }
        }
    }

    /// <summary>
    /// Surfaces the presence fields the stock social panel hides: the exact game status, queue,
    /// champion being played, and whether the party is open to joins.
    /// </summary>
    internal sealed class LeagueFriendActivityService
    {
        internal const string FriendsPath = "/lol-chat/v1/friends";

        private readonly ILeagueClientApi _reader;

        public LeagueFriendActivityService(ILeagueClientApi reader)
        {
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        }

        public async Task<LeagueFriendActivitySnapshot> ReadAsync(CancellationToken cancellationToken)
        {
            var bytes = await _reader.TryGetBytesAsync(FriendsPath, cancellationToken).ConfigureAwait(false);
            if (bytes == null || bytes.Length == 0) return LeagueFriendActivitySnapshot.Unavailable;

            object[] items;
            try
            {
                items = new JavaScriptSerializer().DeserializeObject(Encoding.UTF8.GetString(bytes)) as object[];
            }
            catch (ArgumentException) { return LeagueFriendActivitySnapshot.Unavailable; }
            catch (InvalidOperationException) { return LeagueFriendActivitySnapshot.Unavailable; }
            if (items == null) return LeagueFriendActivitySnapshot.Unavailable;

            var friends = new List<LeagueFriendActivity>();
            int online = 0, playing = 0, joinable = 0;
            foreach (var item in items)
            {
                var entry = item as Dictionary<string, object>;
                if (entry == null) continue;
                var availability = ReadString(entry, "availability");
                var offline = string.Equals(availability, "offline", StringComparison.OrdinalIgnoreCase);

                object lolRaw;
                var lol = entry.TryGetValue("lol", out lolRaw) ? lolRaw as Dictionary<string, object> : null;

                var activity = new LeagueFriendActivity
                {
                    DisplayName = ReadString(entry, "gameName") ?? ReadString(entry, "name"),
                    Availability = availability,
                    GameStatus = ReadString(lol, "gameStatus"),
                    GameMode = ReadString(lol, "gameMode"),
                    QueueType = ReadString(lol, "gameQueueType"),
                    ChampionId = (int)ReadDouble(lol, "championId"),
                    Level = (int)ReadDouble(lol, "level"),
                    JoinableParty = string.Equals(ReadString(lol, "ptyType"), "open", StringComparison.OrdinalIgnoreCase)
                };

                if (!offline) online++;
                if (activity.IsPlaying) playing++;
                if (activity.JoinableParty && !offline) joinable++;
                friends.Add(activity);
            }

            var snapshot = new LeagueFriendActivitySnapshot
            {
                Available = true,
                OnlineCount = online,
                PlayingCount = playing,
                JoinableCount = joinable,
                Friends = friends
            };
            AppLog.Info("Friend activity: online=" + online + "; playing=" + playing + "; joinable=" + joinable +
                        "; total=" + friends.Count);
            return snapshot;
        }

        private static string ReadString(Dictionary<string, object> value, string key)
        {
            object entry;
            return value != null && value.TryGetValue(key, out entry) && entry != null
                ? Convert.ToString(entry, CultureInfo.InvariantCulture) : null;
        }

        private static double ReadDouble(Dictionary<string, object> value, string key)
        {
            object entry;
            if (value == null || !value.TryGetValue(key, out entry) || entry == null) return 0;
            double parsed;
            return double.TryParse(Convert.ToString(entry, CultureInfo.InvariantCulture),
                NumberStyles.Any, CultureInfo.InvariantCulture, out parsed) ? parsed : 0;
        }
    }
}
