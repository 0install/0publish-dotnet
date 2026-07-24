// Copyright Bastian Eicher et al.
// Licensed under the GNU Lesser Public License

using System.ComponentModel;
using System.Globalization;
using NanoByte.Common;
using NanoByte.Common.Info;
using NanoByte.Common.Storage;
using NanoByte.Common.Tasks;
using NanoByte.Common.Undo;
using NanoByte.Common.Values;
using NDesk.Options;
using Spectre.Console;
using ZeroInstall.Model;
using ZeroInstall.Publish.Merging;
using ZeroInstall.Publish.Cli.Properties;
using ZeroInstall.Store.Configuration;
using ZeroInstall.Store.Implementations;
using ZeroInstall.Store.Manifests;
using ZeroInstall.Store.Trust;

namespace ZeroInstall.Publish.Cli;

/// <summary>
/// Creates or modified Zero Install feeds.
/// </summary>
public sealed class PublishCommand
{
    private readonly ITaskHandler _handler;

    /// <summary>The paths of the feeds to apply the operation on.</summary>
    private readonly IReadOnlyList<string> _paths;

    /// <summary>
    /// Parses command-line arguments.
    /// </summary>
    /// <param name="args">The command-line arguments to be parsed.</param>
    /// <param name="handler">A callback object used when the the user needs to be asked questions or informed about download and IO tasks.</param>
    /// <exception cref="OperationCanceledException">The user asked to see help information, version information, etc..</exception>
    /// <exception cref="OptionException"><paramref name="args"/> contains unknown options.</exception>
    public PublishCommand(IEnumerable<string> args, ITaskHandler handler)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _paths = BuildOptions().Parse(args ?? throw new ArgumentNullException(nameof(args)));
    }

    #region Options
    /// <summary>The file to store the aggregated <see cref="Catalog"/> data in.</summary>
    private string? _catalogFile;

    /// <summary>Download missing archives, calculate manifest digests, etc..</summary>
    private bool _addMissing;

    /// <summary>Create the feed file if it does not exist yet, without prompting.</summary>
    private bool _create;

    /// <summary>Let the user edit the feed in an external text editor.</summary>
    private bool _edit;

    /// <summary>The feed to add implementations from.</summary>
    private string? _addFrom;

    /// <summary>The manifest format to add additional digests in.</summary>
    private ManifestFormat? _addDigest;

    /// <summary>The manifest formats to calculate digests for newly added archives in.</summary>
    private readonly List<ManifestFormat> _manifestFormats = [];

    /// <summary>The version number of a new implementation to add.</summary>
    private ImplementationVersion? _addVersion;

    /// <summary>The URL of an archive to add to an implementation.</summary>
    private Uri? _archiveUrl;

    /// <summary>A local copy of <see cref="_archiveUrl"/>.</summary>
    private string? _archiveFile;

    /// <summary>The subdirectory of the archive to extract.</summary>
    private string? _archiveExtract;

    /// <summary>The version of the implementation the <c>--set-*</c> options apply to.</summary>
    private ImplementationVersion? _selectVersion;

    private FeedUri? _setInterfaceUri;
    private string? _setID;
    private string? _setMain;
    private ImplementationVersion? _setVersion;
    private string? _setReleased;
    private Stability? _setStability;
    private Architecture? _setArchitecture;

    /// <summary>Mark the latest testing implementation as stable.</summary>
    private bool _stable;

    /// <summary>Add XML signature blocks to the feed.</summary>
    private bool _xmlSign;

    /// <summary>Remove any existing signatures from the feeds.</summary>
    private bool _unsign;

    /// <summary>A key specifier (key ID, fingerprint or any part of a user ID) for the secret key to use to sign the feeds.</summary>
    private string? _key;

    /// <summary>The passphrase used to unlock the <see cref="OpenPgpSecretKey"/>.</summary>
    private string? _openPgpPassphrase;

    private OptionSet BuildOptions()
    {
        var options = new OptionSet
        {
            // Version information
            {
                "V|version", () => Resources.OptionVersion, _ =>
                {
                    Console.WriteLine(@"0publish (.NET version) " + AppInfo.Current.Version + Environment.NewLine + AppInfo.Current.Copyright + Environment.NewLine + Resources.LicenseInfo);
                    throw new OperationCanceledException(); // Don't handle any of the other arguments
                }
            },
            {"v|verbose", () => Resources.OptionVerbose, _ => _handler.Verbosity++},

            // Modes
            {"catalog=", () => Resources.OptionCatalog, path => _catalogFile = Paths.Absolute(path)},
            {"add-missing", () => Resources.OptionAddMissing, _ => _addMissing = true},
            {"c|create", () => Resources.OptionCreate, _ => _create = true},
            {"e|edit", () => Resources.OptionEdit, _ => _edit = true},

            // Adding content
            {"a|add-from=", () => Resources.OptionAddFrom, path => _addFrom = Paths.Absolute(path)},
            {"add-version=", () => Resources.OptionAddVersion, (ImplementationVersion version) => _addVersion = version},
            {"archive-url=", () => Resources.OptionArchiveUrl, url => _archiveUrl = Parse("archive-url", url, x => new Uri(x, UriKind.Absolute))},
            {"archive-file=", () => Resources.OptionArchiveFile, path => _archiveFile = Paths.Absolute(path)},
            {"archive-extract=", () => Resources.OptionArchiveExtract, dir => _archiveExtract = dir},
            {"d|add-digest=", () => Resources.OptionAddDigest, alg => _addDigest = ParseManifestFormat("add-digest", alg)},
            {"manifest-algorithm=", () => Resources.OptionManifestAlgorithm, alg => _manifestFormats.Add(ParseManifestFormat("manifest-algorithm", alg))},

            // Modifying implementations
            {"select-version=", () => Resources.OptionSelectVersion, (ImplementationVersion version) => _selectVersion = version},
            {"set-interface-uri=", () => Resources.OptionSetInterfaceUri, (FeedUri uri) => _setInterfaceUri = uri},
            {"set-id=", () => Resources.OptionSetID, id => _setID = id},
            {"set-version=", () => Resources.OptionSetVersion, (ImplementationVersion version) => _setVersion = version},
            {"set-released=", () => Resources.OptionSetReleased, date => _setReleased = ParseReleaseDate("set-released", date)},
            {"set-stability=", () => Resources.OptionSetStability + Environment.NewLine + SupportedValues<Stability>(), (Stability stability) => _setStability = stability},
            {"set-main=", () => Resources.OptionSetMain, main => _setMain = main},
            {"set-arch=", () => Resources.OptionSetArch, (Architecture arch) => _setArchitecture = arch},
            {"s|stable", () => Resources.OptionStable, _ => _stable = true},

            // Signatures
            {"x|xmlsign", () => Resources.OptionXmlSign, _ => _xmlSign = true},
            {"u|unsign", () => Resources.OptionUnsign, _ => _unsign = true},
            {"k|key=", () => Resources.OptionKey, user => _key = user},
            {"gpg-passphrase=", () => Resources.OptionGnuPGPassphrase, passphrase => _openPgpPassphrase = passphrase}
        };

        options.Add("h|help|?", () => Resources.OptionHelp, _ =>
        {
            Console.WriteLine(Resources.Usage);
            // ReSharper disable once LocalizableElement
            Console.WriteLine("\t0publish [OPTIONS] FEED-FILE");
            Console.WriteLine();
            Console.WriteLine(Resources.Options);
            options.WriteOptionDescriptions(Console.Out);

            // Don't handle any of the other arguments
            throw new OperationCanceledException();
        });

        return options;
    }
    #endregion

    #region Parsing
    /// <summary>The value accepted by <c>--set-released</c> to indicate the current date.</summary>
    [Localizable(false)]
    private const string Today = "today";

    /// <summary>
    /// Generates a localized instruction string listing all values of an enum, e.g. for use in help text.
    /// </summary>
    private static string SupportedValues<T>() where T : struct, Enum
        => string.Format(Resources.SupportedValues, string.Join(", ", Enum.GetValues(typeof(T)).Cast<T>().Select(ConversionUtils.ConvertToString)));

    private static ManifestFormat ParseManifestFormat(string option, string value)
        => Parse(option, value, ManifestFormat.FromPrefix);

    /// <summary>
    /// Parses a release date. Returns <see cref="string.Empty"/> to indicate that the date should be removed.
    /// </summary>
    private static string ParseReleaseDate(string option, string value)
        => Parse(option, value, x => x switch
        {
            "" => "",
            Today => DateTime.Today.ToString(Element.ReleaseDateFormat, CultureInfo.InvariantCulture),
            _ when ModelUtils.ContainsTemplateVariables(x) => x,
            _ when DateTime.TryParseExact(x, Element.ReleaseDateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out _) => x,
            _ => throw new FormatException(string.Format(Resources.InvalidReleaseDate, Element.ReleaseDateFormat.ToUpperInvariant(), Today))
        });

    private static T Parse<T>(string option, string value, Func<string, T> parse)
    {
        try
        {
            return parse(value);
        }
        #region Error handling
        catch (Exception ex) when (ex is FormatException or ArgumentException or UriFormatException or NotSupportedException)
        {
            // Report as an invalid command-line argument
            throw new OptionException(ex.Message, option);
        }
        #endregion
    }
    #endregion

    /// <summary>
    /// Executes the commands specified by the command-line arguments.
    /// </summary>
    public void Execute()
    {
        if (!string.IsNullOrEmpty(_catalogFile))
        {
            GenerateCatalog();
            return;
        }

        if (_archiveUrl == null && (_archiveFile != null || _archiveExtract != null))
            throw new OptionException(string.Format(Resources.OptionRequires, "--archive-url"), "archive-url");
        if (_stable && _selectVersion != null)
            throw new OptionException(string.Format(Resources.ExclusiveOptions, "--stable", "--select-version"), "stable");

        if (_paths.Count == 0)
            throw new OptionException(string.Format(Resources.MissingArguments, "0publish --help"), "");

        foreach (var file in ResolveFeeds())
            HandleFeed(file);
    }

    /// <exception cref="OperationCanceledException">The user chose not to create the feed.</exception>
    private void HandleFeed(FileInfo file)
    {
        bool edit = _edit, forceSave = false;
        string? addFrom = _addFrom;

        FeedEditing feedEditing;
        if (file.Exists) feedEditing = FeedEditing.Load(file.FullName);
        else
        {
            Feed feed;
            if (addFrom != null)
            { // Turn the local feed into the master feed instead of merging it into an empty one
                feed = FeedTemplate.CreateFromLocal(XmlStorage.LoadXml<Feed>(addFrom));
                addFrom = null;
            }
            else if (_create || _handler.Ask(string.Format(Resources.AskCreateFeed, file.FullName), defaultAnswer: false))
            {
                feed = FeedTemplate.Create(Path.GetFileNameWithoutExtension(file.Name));
                // Let the user fill in the placeholders, unless creating non-interactively
                edit |= !_create;
            }
            else throw new OperationCanceledException();

            forceSave = true;
            feedEditing = new FeedEditing(new SignedFeed(feed)) {Path = file.FullName};
        }

        feedEditing.SignedFeed.Feed.ResolveInternalReferences();

        ApplyOperations(feedEditing, addFrom);
        if (edit) EditFeed(feedEditing);

        SaveFeed(feedEditing, forceSave);
    }

    /// <summary>
    /// Resolves the command-line arguments to feed files. Paths that do not exist yet are passed through, to be created later.
    /// </summary>
    private IEnumerable<FileInfo> ResolveFeeds()
        => _paths.SelectMany(path =>
            path.IndexOfAny(['*', '?']) == -1 && !File.Exists(path) && !Directory.Exists(path)
                ? [new FileInfo(Paths.Absolute(path))]
                : Paths.ResolveFiles([path], "*.xml"));

    private void ApplyOperations(FeedEditing feedEditing, string? addFrom)
    {
        var feed = feedEditing.SignedFeed.Feed;

        if (_setInterfaceUri != null)
            ((ICommandExecutor)feedEditing).Execute(SetValueCommand.ForNullable(() => feed.Uri, newValue: _setInterfaceUri));

        if (_addVersion != null) feed.AddVersion(_addVersion, feedEditing);

        if (_setID != null || _setVersion != null || _setReleased != null || _setStability != null || _setMain != null || _setArchitecture != null)
        {
            foreach (var implementation in feed.SelectImplementations(_selectVersion))
                SetAttributes(implementation, feedEditing);
        }

        if (_stable) feed.MarkStable(feedEditing);

        if (_archiveUrl != null)
            feed.AddArchive(_archiveUrl, _archiveFile, _archiveExtract, _manifestFormats, feedEditing, _handler);

        if (addFrom != null)
            feed.AddFrom(XmlStorage.LoadXml<Feed>(addFrom), feedEditing);

        if (_addDigest != null)
            feed.AddDigests(_addDigest, ImplementationStores.Default(_handler), feedEditing, _handler);

        if (_addMissing) AddMissing(feed.Implementations, feedEditing);
    }

    private void SetAttributes(Implementation implementation, ICommandExecutor executor)
    {
        if (_setID != null)
            executor.Execute(SetValueCommand.For(() => implementation.ID, newValue: _setID));

        if (_setVersion != null)
        {
            executor.Execute(SetValueCommand.For(() => implementation.Version, newValue: _setVersion));
            if (!string.IsNullOrEmpty(implementation.VersionModifier))
                executor.Execute(SetValueCommand.ForNullable(() => implementation.VersionModifier, newValue: null));
        }

        if (_setReleased != null)
            executor.Execute(SetValueCommand.ForNullable(() => implementation.ReleasedString, newValue: _setReleased.EmptyAsNull()));

        if (_setStability != null)
            executor.Execute(SetValueCommand.For(() => implementation.Stability, newValue: _setStability.Value));

        if (_setMain != null)
            executor.Execute(SetValueCommand.ForNullable(() => implementation.Main, newValue: _setMain));

        if (_setArchitecture != null)
            executor.Execute(SetValueCommand.For(() => implementation.Architecture, newValue: _setArchitecture.Value));
    }

    private static void EditFeed(FeedEditing feedEditing)
    {
        var edited = Editor.Edit(feedEditing.SignedFeed.Feed);

        // Avoid a needless rewrite (and resign) if the user did not change anything
        if (!edited.Equals(feedEditing.SignedFeed.Feed))
            feedEditing.Execute(SetValueCommand.ForNullable(() => feedEditing.Target, newValue: edited));
    }

    private void GenerateCatalog()
    {
        IList<FileInfo> feedFiles;
        try
        {
            // Default to using all XML files in the current directory
            feedFiles = Paths.ResolveFiles(_paths.Count == 0 ? [Environment.CurrentDirectory] : _paths, "*.xml");
        }
        #region Error handling
        catch (FileNotFoundException ex)
        {
            // Report as an invalid command-line argument
            throw new OptionException(ex.Message, ex.FileName);
        }
        #endregion

        var catalog = new Catalog();
        foreach (var feed in feedFiles.Select(feedFile => XmlStorage.LoadXml<Feed>(feedFile.FullName)))
        {
            feed.Strip();
            catalog.Feeds.Add(feed);
        }

        if (catalog.Feeds.Count == 0) throw new FileNotFoundException(Resources.NoFeedFilesFound);

        if (_xmlSign)
        {
            var openPgp = OpenPgp.Signing();
            var signedCatalog = new SignedCatalog(catalog, openPgp.GetSecretKey(_key));

            PromptPassphrase(
                () => signedCatalog.Save(_catalogFile!, _openPgpPassphrase),
                signedCatalog.SecretKey);
        }
        else catalog.SaveXml(_catalogFile!);
    }

    private void AddMissing(IEnumerable<Implementation> implementations, ICommandExecutor executor)
    {
        executor = new ConcurrentCommandExecutor(executor);

        try
        {
            implementations.AsParallel()
                           .WithDegreeOfParallelism(Config.LoadSafe().MaxParallelDownloads)
                           .ForAll(implementation => implementation.SetMissing(executor, _handler));
        }
        catch (AggregateException ex)
        {
            throw ex.RethrowFirstInner();
        }
    }

    private void SaveFeed(FeedEditing feedEditing, bool forceSave)
    {
        if (!feedEditing.Path!.EndsWith(".xml.template")
         && !feedEditing.IsValid(out string problem))
            Log.Warn(problem);

        if (_unsign)
        {
            // Remove any existing signatures
            feedEditing.SignedFeed.SecretKey = null;
        }
        else
        {
            var openPgp = OpenPgp.Signing();
            if (_xmlSign)
            { // Signing explicitly requested
                if (feedEditing.SignedFeed.SecretKey == null)
                { // No previous signature
                    // Use user-specified key or default key
                    feedEditing.SignedFeed.SecretKey = openPgp.GetSecretKey(_key);
                }
                else
                { // Existing signature
                    if (!string.IsNullOrEmpty(_key)) // Use new user-specified key
                        feedEditing.SignedFeed.SecretKey = openPgp.GetSecretKey(_key);
                    //else resign implied
                }
            }
            //else resign implied
        }

        // If no signing or unsigning was explicitly requested and the content did not change
        // there is no need to overwrite (and potential resign) the file
        if (!_xmlSign && !_unsign && !forceSave && !feedEditing.UnsavedChanges)
        {
            Log.Info(Resources.FeedUnchanged);
            return;
        }

        PromptPassphrase(
            () => feedEditing.SignedFeed.Save(feedEditing.Path!, _openPgpPassphrase),
            feedEditing.SignedFeed.SecretKey);
    }

    /// <summary>
    /// Runs the specified <paramref name="action"/> and prompts for the <paramref name="secretKey"/> if <see cref="WrongPassphraseException"/> is thrown.
    /// </summary>
    /// <exception cref="OperationCanceledException">The user canceled the passphrase entry.</exception>
    private void PromptPassphrase(Action action, OpenPgpSecretKey? secretKey)
    {
        while (true)
        {
            try
            {
                action();
                return; // Exit loop if passphrase is correct
            }
            catch (WrongPassphraseException ex) when (secretKey != null)
            {
                // Only print error if a passphrase was actually entered
                if (_openPgpPassphrase != null) Log.Error(ex);

                // Ask for passphrase to unlock secret key if we were unable to save without it
                _openPgpPassphrase = AnsiCli.Prompt(new TextPrompt<string>(string.Format(Resources.AskForPassphrase, secretKey.UserID)).Secret(), _handler.CancellationToken);
            }
        }
    }
}
