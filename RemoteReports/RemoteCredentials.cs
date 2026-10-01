using System.Net.Http.Headers;
using System.Text;

namespace RemoteReports;

/// <summary>
/// Credentials for a LabKey/Panorama server, from the command line or the environment
/// (LABKEY_API_KEY, LABKEY_USERNAME, LABKEY_PASSWORD). Sent with HTTP Basic authentication, which
/// LabKey accepts for an email + password, or for an API key as the password of the user "apikey".
/// </summary>
public sealed class RemoteCredentials
{
    public string? ApiKey { get; init; }
    public string? Username { get; init; }
    public string? Password { get; init; }

    public static RemoteCredentials FromOptions(string? apiKey, string? username, string? password) => new()
    {
        ApiKey = NullIfEmpty(apiKey ?? Environment.GetEnvironmentVariable("LABKEY_API_KEY")),
        Username = NullIfEmpty(username ?? Environment.GetEnvironmentVariable("LABKEY_USERNAME")),
        Password = NullIfEmpty(password ?? Environment.GetEnvironmentVariable("LABKEY_PASSWORD")),
    };

    /// <summary>
    /// The Authorization header to send, or null to try anonymously. A username without a password is
    /// completed by prompting; a password without a username uses <paramref name="defaultUsername"/>
    /// (the .skyp file's DownloadingUser).
    /// </summary>
    public AuthenticationHeaderValue? GetHeader(string? defaultUsername)
    {
        if (ApiKey != null)
            return Basic("apikey", ApiKey);
        string? username = Username ?? (Password != null ? defaultUsername : null);
        if (username == null)
        {
            if (Password != null)
                throw new InvalidDataException("A password was given without a username; use --username");
            return null;
        }
        string password = Password ?? PromptForPassword(username);
        return Basic(username, password);
    }

    /// <summary>After an anonymous request was refused, asks for a username and password if there is a console.</summary>
    public static AuthenticationHeaderValue? PromptForLogin(string server, string? defaultUsername)
    {
        if (Console.IsInputRedirected)
            return null;
        Console.Error.WriteLine($"{server} requires signing in.");
        Console.Error.Write(defaultUsername != null ? $"Username [{defaultUsername}]: " : "Username: ");
        string? username = NullIfEmpty(Console.ReadLine()?.Trim()) ?? defaultUsername;
        if (username == null)
            return null;
        return Basic(username, PromptForPassword(username));
    }

    private static string PromptForPassword(string username)
    {
        if (Console.IsInputRedirected)
            throw new InvalidDataException($"No password for {username}; use --password or set LABKEY_PASSWORD");
        Console.Error.Write($"Password for {username}: ");
        var password = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
                break;
            if (key.Key == ConsoleKey.Backspace)
            {
                if (password.Length > 0)
                    password.Length--;
            }
            else if (!char.IsControl(key.KeyChar))
            {
                password.Append(key.KeyChar);
            }
        }
        Console.Error.WriteLine();
        return password.ToString();
    }

    private static AuthenticationHeaderValue Basic(string username, string password) =>
        new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(username + ":" + password)));

    private static string? NullIfEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;
}
