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

        // Optional bridge metadata. Legacy clients ignore the field; the 3.5.17
        // bridge consumes it to hand the installation over to the FACM 4.0
        // native bootstrapper without changing the legacy single-file protocol.
        [DataMember(Name = "migration")]
        public Facm4MigrationTarget Migration { get; set; }

        [IgnoreDataMember]
        public UpdateMirrorSource[] ResolvedSources { get; set; }
    }

    [DataContract]
    internal sealed class Facm4MigrationTarget
    {
        [DataMember(Name = "enabled")]
        public bool Enabled { get; set; }

        [DataMember(Name = "version")]
        public string Version { get; set; }

        [DataMember(Name = "bootstrapper_url")]
        public string BootstrapperUrl { get; set; }

        [DataMember(Name = "bootstrapper_sha256")]
        public string BootstrapperSha256 { get; set; }

        [DataMember(Name = "manifest_url")]
        public string ManifestUrl { get; set; }

        [DataMember(Name = "release_notes")]
        public string ReleaseNotes { get; set; }
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

    [DataContract]
    internal sealed class UpdateMirrorCatalog
    {
        [DataMember(Name = "schema")]
        public string Schema { get; set; }

        [DataMember(Name = "updated_at")]
        public string UpdatedAt { get; set; }

        [DataMember(Name = "sources")]
        public UpdateMirrorSource[] Sources { get; set; }
    }

    [DataContract]
    internal sealed class UpdateMirrorSource
    {
        [DataMember(Name = "name")]
        public string Name { get; set; }

        [DataMember(Name = "prefix")]
        public string Prefix { get; set; }

        [DataMember(Name = "enabled")]
        public bool Enabled { get; set; }

        [DataMember(Name = "priority")]
        public int Priority { get; set; }
    }

    internal sealed class UpdateDownloadCandidate
    {
        public string SourceName { get; set; }
        public string Url { get; set; }
    }

    internal sealed class OnlineSnapshot
    {
        public UpdateManifest Update { get; set; }
        public AnnouncementManifest Announcement { get; set; }
        public Version CurrentVersion { get; set; }
        public Version LatestVersion { get; set; }
        public bool UpdateAvailable { get; set; }
        public bool ForceUpdateRequired { get; set; }
        public string MetadataSourceName { get; set; }
        public string ErrorMessage { get; set; }
    }
}
