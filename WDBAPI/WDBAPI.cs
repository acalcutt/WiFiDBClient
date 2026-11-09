using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using WDBTraceLog;

namespace WDBAPI
{
    /// <summary>What WifiDB did with an upload.</summary>
    /// <param name="ConnectionFailed">WifiDB couldn't be reached or failed, rather than refusing the file.</param>
    public sealed record UploadResult(bool Success, string Message, string? ImportId = null, string? Title = null,
        string? User = null, bool ConnectionFailed = false)
    {
        /// <summary>WifiDB already has this file, imported or waiting.</summary>
        public bool AlreadyKnown => !Success && (Message.Contains("already exists", StringComparison.OrdinalIgnoreCase) ||
                                                 Message.Contains("already waiting", StringComparison.OrdinalIgnoreCase));
    }

    public enum RemoteFileState { Unknown, Waiting, Importing, Finished, Failed }

    /// <summary>Where a file is in WifiDB, by its hash.</summary>
    /// <param name="Error">The check itself failed; <see cref="State"/> means nothing then.</param>
    public sealed record RemoteFileStatus(RemoteFileState State, string Message, int ImportId = 0, string? Title = null, string? Error = null);

    public sealed record DaemonInfo(string NodeName, string PidFile, string Pid, string RunTime, string Memory, string Command, string Date);

    /// <summary>The daemons, or the reason there's no list.</summary>
    public sealed record DaemonList(IReadOnlyList<DaemonInfo> Daemons, string? Error = null);

    /// <summary>The queue and recent imports from api/v2/profile.php, keyed by upper-case hash. Null <see cref="Files"/> means no answer.</summary>
    public sealed record ProfileResult(IReadOnlyDictionary<string, RemoteFileStatus>? Files, string? Error = null);

    /// <summary>WifiDB's API (api/v2): JSON answers, and the username and key in the POST body so they stay out of URLs and access logs.</summary>
    public sealed class WDBAPI : IDisposable
    {
        private const int MaxRedirects = 5;
        private static readonly TimeSpan UploadTimeout = TimeSpan.FromMinutes(30);
        private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(30);

        private readonly HttpClient _http;
        private readonly TraceLog _log;
        private string _apiBase = "";

        public string Username { get; set; } = "";
        public string ApiKey { get; set; } = "";

        /// <param name="handler">Replaces the network, for tests. It must not follow redirects itself.</param>
        public WDBAPI(TraceLog log, HttpMessageHandler? handler = null)
        {
            _log = log;
            // Redirects are followed by SendAsync: HttpClient would turn the POST into a GET and lose the file.
            handler ??= new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.All,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            };
            _http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            string version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "2.0";
            _http.DefaultRequestHeaders.UserAgent.ParseAdd($"WDB_Uploader/{version}");
        }

        /// <summary>The API folder's URL, always ending in a slash, e.g. https://wifidb.net/api/v2/.</summary>
        public string ApiBase
        {
            get => _apiBase;
            set => _apiBase = value ?? "";
        }

        public static string CombineApiUrl(string? serverAddress, string? apiPath)
        {
            string server = (serverAddress ?? "").Trim().TrimEnd('/');
            if (server.Length == 0) return "";
            if (!server.Contains("://", StringComparison.Ordinal)) server = "https://" + server;
            string path = (apiPath ?? "").Trim().Trim('/');
            return path.Length == 0 ? server + "/" : $"{server}/{path}/";
        }

        public async Task<UploadResult> UploadAsync(string filePath, string title, string notes, string otherUsers = "", CancellationToken ct = default)
        {
            try
            {
                var (status, body) = await SendAsync("import.php", () => BuildUpload(filePath, title, notes, otherUsers), UploadTimeout, ct);
                _log.Write("WDBAPI", $"Upload {Path.GetFileName(filePath)}: HTTP {(int)status} {Trim(body)}");
                if (!IsSuccess(status))
                {
                    return new UploadResult(false, status == HttpStatusCode.RequestEntityTooLarge
                        ? "The file is larger than this WifiDB accepts."
                        : $"WifiDB returned HTTP {(int)status}: {Trim(body)}", ConnectionFailed: (int)status >= 500);
                }

                using JsonDocument? json = ParseJson(body);
                if (json is null) return new UploadResult(false, $"WifiDB's answer couldn't be read: {Trim(body)}");
                JsonElement root = json.RootElement;
                string? error = ErrorText(root);
                if (error is not null) return new UploadResult(false, error);
                if (Child(root, "import") is { } import)
                {
                    error = ErrorText(import);
                    if (error is not null) return new UploadResult(false, error);
                    return new UploadResult(true, Str(import, "message") ?? "File uploaded to WifiDB.",
                        Str(import, "importnum"), Str(import, "title"), Str(import, "user"));
                }
                return new UploadResult(false, $"Unexpected answer from WifiDB: {Trim(body)}");
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return new UploadResult(false, "The upload timed out.", ConnectionFailed: true);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or UriFormatException or UnauthorizedAccessException)
            {
                _log.Write("WDBAPI", "Upload failed: " + ex);
                return new UploadResult(false, $"Upload failed: {ex.Message}", ConnectionFailed: true);
            }
        }

        public async Task<RemoteFileStatus> CheckHashAsync(string fileHash, CancellationToken ct = default)
        {
            try
            {
                var (status, body) = await SendAsync("import.php", () => Form(("func", "check_hash"), ("hash", fileHash)), QueryTimeout, ct);
                if (!IsSuccess(status)) return Failed($"WifiDB returned HTTP {(int)status}");
                using JsonDocument? json = ParseJson(body);
                if (json is null) return Failed($"WifiDB's answer couldn't be read: {Trim(body)}");
                JsonElement root = json.RootElement;
                string? error = ErrorText(root);
                if (error is not null) return Failed(error);
                if (Child(root, "scheduling") is not { } scheduling) return Failed($"Unexpected answer from WifiDB: {Trim(body)}");

                if (Child(scheduling, "waiting") is { } waiting)
                    return new RemoteFileStatus(RemoteFileState.Waiting, "Waiting in WifiDB's import queue.", Int(waiting, "id"), Str(waiting, "title"));
                if (Child(scheduling, "importing") is { } importing)
                    return new RemoteFileStatus(RemoteFileState.Importing, ImportingMessage(Str(importing, "ap"), Str(importing, "tot")), Int(importing, "id"), Str(importing, "title"));
                if (Child(scheduling, "finished") is { } finished)
                    return new RemoteFileStatus(RemoteFileState.Finished, FinishedMessage(Str(finished, "aps"), Str(finished, "gps")), Int(finished, "id"), Str(finished, "title"));
                return new RemoteFileStatus(RemoteFileState.Unknown, "WifiDB doesn't know this file.");
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return Failed("WifiDB didn't answer in time.");
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or UriFormatException)
            {
                return Failed($"Couldn't reach WifiDB: {ex.Message}");
            }

            static RemoteFileStatus Failed(string error) => new(RemoteFileState.Unknown, error, Error: error);
        }

        public async Task<ProfileResult> GetProfileAsync(CancellationToken ct = default)
        {
            try
            {
                var (status, body) = await SendAsync("profile.php", () => Form(("inc", "100")), QueryTimeout, ct);
                if (!IsSuccess(status)) return new ProfileResult(null, $"WifiDB returned HTTP {(int)status}");
                using JsonDocument? json = ParseJson(body);
                if (json is null) return new ProfileResult(null, "This WifiDB doesn't have the profile API.");
                JsonElement root = json.RootElement;
                string? error = ErrorText(root);
                if (error is not null) return new ProfileResult(null, error);
                if (Child(root, "profile") is not { } profile) return new ProfileResult(null, "WifiDB's answer had no profile.");

                var files = new Dictionary<string, RemoteFileStatus>(StringComparer.OrdinalIgnoreCase);
                if (Child(profile, "imports") is { } imports)
                {
                    AddRows(files, Child(imports, "rows"), f => new RemoteFileStatus(RemoteFileState.Finished,
                        FinishedMessage(Str(f, "aps"), Str(f, "gps")), Int(f, "id"), Str(f, "title")));
                }
                if (Child(profile, "queue") is { } queue)
                {
                    AddRows(files, Child(queue, "waiting"), f => new RemoteFileStatus(RemoteFileState.Waiting,
                        "Waiting in WifiDB's import queue.", Int(f, "id"), Str(f, "title")));
                    AddRows(files, Child(queue, "importing"), f => new RemoteFileStatus(RemoteFileState.Importing,
                        ImportingMessage(Str(f, "ap"), Str(f, "tot")), Int(f, "id"), Str(f, "title")));
                    AddRows(files, Child(queue, "bad"), f => new RemoteFileStatus(RemoteFileState.Failed,
                        Str(f, "error_msg") ?? "WifiDB couldn't import this file.", Int(f, "id"), Str(f, "title")));
                }
                return new ProfileResult(files);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return new ProfileResult(null, "WifiDB didn't answer in time.");
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or UriFormatException)
            {
                return new ProfileResult(null, $"Couldn't reach WifiDB: {ex.Message}");
            }
        }

        public async Task<DaemonList> GetDaemonsAsync(CancellationToken ct = default)
        {
            try
            {
                var (status, body) = await SendAsync("schedule.php", () => Form(("func", "daemonstatuses")), QueryTimeout, ct);
                if (!IsSuccess(status)) return new DaemonList([], $"WifiDB returned HTTP {(int)status}");
                using JsonDocument? json = ParseJson(body);
                if (json is null) return new DaemonList([], "WifiDB's answer couldn't be read.");
                JsonElement root = json.RootElement;
                string? error = ErrorText(root);
                if (error is not null) return new DaemonList([], error);

                // {"daemons":{"daemon1":{...}}}, or {"daemons":"No Daemons running."}
                var list = new List<DaemonInfo>();
                if (Child(root, "daemons") is { } daemons)
                {
                    foreach (JsonProperty p in daemons.EnumerateObject())
                    {
                        JsonElement d = p.Value;
                        list.Add(new DaemonInfo(Str(d, "nodename") ?? "", Str(d, "pidfile") ?? "", Str(d, "pid") ?? "",
                            Str(d, "pidtime") ?? "", Str(d, "pidmem") ?? "", Str(d, "pidcmd") ?? "", Str(d, "date") ?? ""));
                    }
                }
                return new DaemonList(list);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return new DaemonList([], "WifiDB didn't answer in time.");
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or UriFormatException)
            {
                return new DaemonList([], $"Couldn't reach WifiDB: {ex.Message}");
            }
        }

        public void Dispose() => _http.Dispose();

        private async Task<(HttpStatusCode Status, string Body)> SendAsync(string endpoint, Func<HttpContent> content, TimeSpan timeout, CancellationToken ct)
        {
            if (_apiBase.Length == 0) throw new HttpRequestException("No WifiDB server is selected.");
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);

            var uri = new Uri(_apiBase + endpoint);
            for (int hop = 0; hop <= MaxRedirects; hop++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = content() };
                using HttpResponseMessage response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308 && response.Headers.Location is { } location)
                {
                    uri = NextHop(uri, location);
                    continue;
                }
                return (response.StatusCode, await response.Content.ReadAsStringAsync(cts.Token));
            }
            throw new HttpRequestException("WifiDB redirected too many times. Check the server address in Settings.");
        }

        /// <summary>The next address in a redirect. The request carries the API key, so it never leaves the host or drops to plain HTTP.</summary>
        internal static Uri NextHop(Uri current, Uri location)
        {
            Uri next = location.IsAbsoluteUri ? location : new Uri(current, location);
            static string Host(Uri u) => u.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? u.Host[4..] : u.Host;
            if (!string.Equals(Host(current), Host(next), StringComparison.OrdinalIgnoreCase))
                throw new HttpRequestException($"WifiDB redirected to {next.Host}. Update the server address in Settings.");
            if (current.Scheme == Uri.UriSchemeHttps && next.Scheme == Uri.UriSchemeHttp)
                next = new UriBuilder(next) { Scheme = Uri.UriSchemeHttps, Port = -1 }.Uri;
            return next;
        }

        private MultipartFormDataContent BuildUpload(string filePath, string title, string notes, string otherUsers)
        {
            var content = new MultipartFormDataContent();
            void Add(string name, string value)
            {
                if (!string.IsNullOrWhiteSpace(value)) content.Add(new StringContent(value.Trim()), name);
            }
            Add("username", Username);
            Add("apikey", ApiKey);
            Add("otherusers", otherUsers);
            Add("title", title);
            Add("notes", notes);

            var file = new StreamContent(new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
            file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            file.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
            {
                Name = "\"file\"",
                FileName = $"\"{UploadFileName(filePath)}\"",
            };
            content.Add(file);
            return content;
        }

        /// <summary>The name to send: PHP can't read the encoded form .NET uses for non-ASCII names, and WifiDB picks its importer from the extension.</summary>
        internal static string UploadFileName(string filePath)
        {
            var name = new StringBuilder();
            foreach (char c in Path.GetFileName(filePath))
                name.Append(c is >= ' ' and < (char)127 and not '"' and not '\\' ? c : '_');
            return name.ToString();
        }

        private FormUrlEncodedContent Form(params (string Name, string Value)[] fields)
        {
            var values = new List<KeyValuePair<string, string>>
            {
                new("username", Username),
                new("apikey", ApiKey),
            };
            foreach (var (name, value) in fields) values.Add(new(name, value));
            return new FormUrlEncodedContent(values.Where(v => v.Value.Length > 0));
        }

        private static void AddRows(Dictionary<string, RemoteFileStatus> files, JsonElement? rows, Func<JsonElement, RemoteFileStatus> read)
        {
            if (rows is not { ValueKind: JsonValueKind.Array } list) return;
            foreach (JsonElement row in list.EnumerateArray())
            {
                string? hash = Str(row, "hash");
                if (!string.IsNullOrEmpty(hash)) files[hash] = read(row);
            }
        }

        private static string ImportingMessage(string? ap, string? total) =>
            ap is not null && total is not null ? $"Importing now ({ap} of {total} APs)." : "Importing now.";

        private static string FinishedMessage(string? aps, string? gps) =>
            aps is not null ? $"Imported: {aps} APs, {gps ?? "0"} GPS points." : "Imported.";

        private static bool IsSuccess(HttpStatusCode status) => (int)status is >= 200 and < 300;

        /// <summary>The JSON in an answer. A server showing PHP warnings prints them before it, so a second try starts at the first bracket.</summary>
        private static JsonDocument? ParseJson(string body)
        {
            foreach (string text in new[] { body, body[Math.Max(0, body.IndexOfAny(['{', '[']))..] })
            {
                try
                {
                    return JsonDocument.Parse(text);
                }
                catch (JsonException)
                {
                }
            }
            return null;
        }

        /// <summary>WifiDB's refusal: {"error":"..."}, {"error":{"desc":"..."}}, or a bare ["message"] when it stops early.</summary>
        private static string? ErrorText(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Array)
                return element.GetArrayLength() > 0 && element[0].ValueKind == JsonValueKind.String ? element[0].GetString() : null;
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty("error", out JsonElement error)) return null;
            if (error.ValueKind == JsonValueKind.Object && error.TryGetProperty("desc", out JsonElement desc)) return desc.ToString();
            return error.ToString();
        }

        private static JsonElement? Child(JsonElement element, string name) =>
            element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) &&
            value.ValueKind is JsonValueKind.Object or JsonValueKind.Array ? value : null;

        private static string? Str(JsonElement element, string name) =>
            element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind != JsonValueKind.Null
                ? value.ToString() : null;

        private static int Int(JsonElement element, string name) => int.TryParse(Str(element, name), out int value) ? value : 0;

        private static string Trim(string text) => text.Length > 200 ? text[..200] + "…" : text;
    }
}
