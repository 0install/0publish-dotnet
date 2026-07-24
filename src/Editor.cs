// Copyright Bastian Eicher et al.
// Licensed under the GNU Lesser Public License

using NanoByte.Common;
using NanoByte.Common.Native;
using NanoByte.Common.Storage;
using ZeroInstall.Model;
using ZeroInstall.Publish.Cli.Properties;

namespace ZeroInstall.Publish.Cli;

/// <summary>
/// Lets the user edit a <see cref="Feed"/> in an external text editor.
/// </summary>
public static class Editor
{
    /// <summary>
    /// Writes a feed to a temporary file, opens it in the user's text editor and reads it back in once the editor is closed.
    /// </summary>
    /// <param name="feed">The feed to edit.</param>
    /// <returns>The edited feed.</returns>
    /// <exception cref="IOException">The editor could not be launched or exited with an error.</exception>
    /// <exception cref="InvalidDataException">The edited file is not a valid feed.</exception>
    public static Feed Edit(Feed feed)
    {
        #region Sanity checks
        if (feed == null) throw new ArgumentNullException(nameof(feed));
        #endregion

        using var tempFile = new TemporaryFile("0publish");
        feed.SaveXml(tempFile);

        string editor = GetEditor();
        Log.Info($"Editing feed with '{editor}'");
        try
        {
            ProcessUtils.Start(editor, tempFile).WaitForSuccess();
        }
        #region Error handling
        catch (ExitCodeException ex)
        {
            // Wrap exception since only certain exception types are allowed
            throw new IOException(ex.Message, ex);
        }
        #endregion

        return XmlStorage.LoadXml<Feed>(tempFile);
    }

    /// <summary>
    /// Determines the text editor to use.
    /// </summary>
    /// <exception cref="IOException">No text editor could be found.</exception>
    private static string GetEditor()
        => Environment.GetEnvironmentVariable("VISUAL").EmptyAsNull()
        ?? Environment.GetEnvironmentVariable("EDITOR").EmptyAsNull()
        ?? (WindowsUtils.IsWindows
               ? "notepad.exe"
               : new[] {"sensible-editor", "nano", "vi"}.FirstOrDefault(ExistsInPath)
              ?? throw new IOException(string.Format(Resources.NoEditorFound, "EDITOR")));

    private static bool ExistsInPath(string fileName)
        => (Environment.GetEnvironmentVariable("PATH") ?? "")
          .Split(Path.PathSeparator)
          .Any(directory => File.Exists(Path.Combine(directory, fileName)));
}
