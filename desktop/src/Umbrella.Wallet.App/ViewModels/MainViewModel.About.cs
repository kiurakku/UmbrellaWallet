using System.Reflection;

namespace Umbrella.Wallet.App.ViewModels;

/// <summary>
/// Who made this wallet, under what licence, and where the authoritative copies live.
///
/// A self-custody wallet is downloaded as a binary from the internet by people who are being told,
/// correctly, not to trust binaries from the internet. The one thing that makes the download
/// checkable is knowing exactly whose build it is meant to be: the publisher, the repository, the
/// one official channel. A look-alike build that keeps the name and changes the money path is the
/// attack this screen exists to make visible — so the publisher is stated in the app, not only in
/// the installer's properties dialog, and it is read from the assembly rather than typed here, so a
/// rebranded fork cannot leave it saying "the fear" while being something else.
///
/// These strings are also the attribution the licence requires (LICENSE §2(e), LEGAL/LICENSE_SUMMARY)
/// and the in-app notices THIRD_PARTY_NOTICES points at.
/// </summary>
public partial class MainViewModel
{
    /// <summary>Publisher of this build, read from the assembly's own company metadata.</summary>
    public string PublisherName => Publisher;

    /// <summary>Copyright line, read from the assembly rather than restated in the UI.</summary>
    public string CopyrightLine =>
        typeof(MainViewModel).Assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright
        ?? string.Empty;

    /// <summary>The publisher name as the build declares it. Static so tests can assert on it
    /// without constructing a view model (which touches the vault and the network layer).</summary>
    public static string Publisher =>
        typeof(MainViewModel).Assembly.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company
        ?? "the fear";

    /// <summary>"Umbrella Wallet 4.8.1" — the product name and version as the assembly declares them.</summary>
    public string AboutProductLine =>
        $"{typeof(MainViewModel).Assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? "Umbrella Wallet"} {CurrentVersion}";

    /// <summary>"A product by the fear" — the maker's mark in words, beside the gold ghost.</summary>
    public string AboutPublisherLine =>
        string.Format(Loc.Instance["about.byPublisher"], Publisher);

    /// <summary>The canonical repository. Every other copy of this source is somebody else's.</summary>
    public string RepoUrl => "https://github.com/thefear078/UmbrellaWallet";

    public string ReleasesUrl => "https://github.com/thefear078/UmbrellaWallet/releases";
    public string LicenseUrl => "https://github.com/thefear078/UmbrellaWallet/blob/main/LICENSE";
    public string TrademarkUrl => "https://github.com/thefear078/UmbrellaWallet/blob/main/TRADEMARK_POLICY.md";
    public string NoticesUrl => "https://github.com/thefear078/UmbrellaWallet/blob/main/THIRD_PARTY_NOTICES.md";
}
