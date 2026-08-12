namespace Act.Core.Model;

// How far ACT is allowed to go on its own towards a newer version. Three values rather than a
// switch, because "check but leave the download to me" is a position people actually hold — a
// metered connection makes an unasked hundred megabytes a real cost — and it is not the same
// position as not wanting updates.
//
// **No value here ever reaches a pre-release.** That is not a policy choice, it is a property of
// the feed: pre-release GitHub releases carry no update metadata, and the app forces
// `AllowPrerelease = false` on top of it.
public enum UpdatePolicy
{
    // Check, fetch it quietly, and apply it the next time the app exits anyway. The default,
    // because the alternative is a user who is out of date and does not know it.
    NotifyAndDownload,

    // Check and say so, download nothing until asked.
    NotifyOnly,

    // Never check. Nothing reaches the network.
    Off,
}
