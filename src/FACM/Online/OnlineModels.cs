using System;
using System.Runtime.Serialization;

namespace FACM.Online
{
    [DataContract]
    internal sealed class UpdateManifest
    {
        [DataMember(Name = "enabled")]
        public bool Enabled { get; set; }

        [DataMember(Name = "version")]
        public string Version { get; set; }

        [DataMember(Name = "minimum_version")]
        public string MinimumVersion { get; set; }

        [DataMember(Name = "force_update")]
        public bool ForceUpdate { get; set; }

        [DataMember(Name = "download_url")]
        public string DownloadUrl { get; set; }

        [DataMember(Name = "sha256")]
        public string Sha256 { get; set; }

        [DataMember(Name = "release_notes")]
        public string ReleaseNotes { get; set; }

        [DataMember(Name = "published_at")]
        public string PublishedAt { get; set; }
    }

    [DataContract]
    internal sealed class AnnouncementManifest
    {
        [DataMember(Name = "enabled")]
        public bool Enabled { get; set; }

        [DataMember(Name = "id")]
        public string Id { get; set; }

        [DataMember(Name = "title")]
        public string Title { get; set; }

        [DataMember(Name = "body")]
        public string Body { get; set; }

        [DataMember(Name = "level")]
        public string Level { get; set; }

        [DataMember(Name = "popup")]
        public bool Popup { get; set; }

        [DataMember(Name = "updated_at")]
        public string UpdatedAt { get; set; }

        [DataMember(Name = "link_url")]
        public string LinkUrl { get; set; }
    }

    internal sealed class OnlineSnapshot
    {
        public UpdateManifest Update { get; set; }
        public AnnouncementManifest Announcement { get; set; }
        public Version CurrentVersion { get; set; }
        public Version LatestVersion { get; set; }
        public bool UpdateAvailable { get; set; }
        public bool ForceUpdateRequired { get; set; }
        public string ErrorMessage { get; set; }
    }

    internal static class OnlineDisplayText
    {
        public const string UnknownVersion = "未知";
        public const string LatestVersionUnavailable = "未获取";
    }
}
