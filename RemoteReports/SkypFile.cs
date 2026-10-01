namespace RemoteReports;

/// <summary>
/// A Skyline document pointer (.skyp): the WebDAV URL of a .sky.zip on a Panorama server, then optional
/// "key:value" lines such as FileSize:475831 and DownloadingUser:someone@example.org. Mirrors
/// pwiz.Skyline.Model.SkypFile, which likewise ignores values it cannot parse.
/// </summary>
public sealed class SkypFile
{
    public const string Extension = ".skyp";

    public required Uri SkyZipUri { get; init; }
    public long? FileSize { get; init; }
    /// <summary>The Panorama account the pointer was made for, if any.</summary>
    public string? DownloadingUser { get; init; }

    public static bool IsSkypPath(string path) => path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase);

    public static SkypFile Load(string path)
    {
        Uri? uri = null;
        long? fileSize = null;
        string? downloadingUser = null;
        foreach (var line in File.ReadLines(path).Where(l => !string.IsNullOrWhiteSpace(l)))
        {
            if (uri == null)
            {
                string url = line.Trim();
                if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                    throw new InvalidDataException($"{path}: the first line should be the http(s) URL of a .sky.zip file, but is '{url}'");
                continue;
            }
            var parts = line.Split(':');
            if (parts.Length != 2)
                continue;
            string key = parts[0].Trim(), value = parts[1].Trim();
            if (key == "FileSize" && long.TryParse(value, out long size) && size > 0)
                fileSize = size;
            else if (key == "DownloadingUser" && value.Length > 0)
                downloadingUser = value;
        }
        if (uri == null)
            throw new InvalidDataException($"{path} does not contain the URL of a .sky.zip file");
        return new SkypFile { SkyZipUri = uri, FileSize = fileSize, DownloadingUser = downloadingUser };
    }
}
