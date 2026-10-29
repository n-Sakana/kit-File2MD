using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace File2MD
{
    internal sealed class LocalServer : IDisposable
    {
        private const long MaximumRequestBytes = 64 * 1024;

        private readonly SemaphoreSlim _requestSlots = new SemaphoreSlim(16, 16);
        private readonly SemaphoreSlim _operationGate = new SemaphoreSlim(1, 1);
        private readonly HttpListener _listener = new HttpListener();
        private readonly AppState _state;
        private readonly int _port;
        private readonly string _initialMode;
        private readonly string _outputRoot;
        private readonly Action _requestShutdown;

        public LocalServer(
            int port,
            AppState state,
            string initialMode,
            string outputRoot,
            Action requestShutdown)
        {
            _port = port;
            _state = state;
            _initialMode = initialMode;
            _outputRoot = Path.GetFullPath(outputRoot);
            _requestShutdown = requestShutdown;
        }

        public Uri Start()
        {
            _listener.IgnoreWriteExceptions = true;
            _listener.Prefixes.Add(string.Format("http://127.0.0.1:{0}/", _port));
            _listener.Prefixes.Add(string.Format("http://localhost:{0}/", _port));
            _listener.Start();
            return new Uri(string.Format("http://localhost:{0}/", _port));
        }

        public async Task RunAsync(CancellationToken cancellationToken)
        {
            using (cancellationToken.Register(delegate
            {
                try
                {
                    _listener.Close();
                }
                catch (ObjectDisposedException)
                {
                }
            }))
            {
                var pending = new List<Task>();
                while (!cancellationToken.IsCancellationRequested && _listener.IsListening)
                {
                    HttpListenerContext context;
                    try
                    {
                        context = await _listener.GetContextAsync();
                    }
                    catch (HttpListenerException)
                    {
                        if (cancellationToken.IsCancellationRequested || !_listener.IsListening)
                        {
                            break;
                        }

                        throw;
                    }
                    catch (ObjectDisposedException)
                    {
                        if (cancellationToken.IsCancellationRequested || !_listener.IsListening)
                        {
                            break;
                        }

                        throw;
                    }

                    try
                    {
                        await _requestSlots.WaitAsync(cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        context.Response.Abort();
                        break;
                    }
                    var accepted = context;
                    pending.RemoveAll(delegate(Task task) { return task.IsCompleted; });
                    pending.Add(Task.Run(async delegate
                    {
                        try { await HandleSafelyAsync(accepted); }
                        catch (Exception exception) { Console.Error.WriteLine(exception); }
                        finally { _requestSlots.Release(); }
                    }));
                }
                await Task.WhenAny(Task.WhenAll(pending), Task.Delay(2000));
            }
        }

        public void Dispose()
        {
            if (_listener.IsListening)
            {
                _listener.Stop();
            }

            _listener.Close();
        }

        private async Task HandleSafelyAsync(HttpListenerContext context)
        {
            AddSecurityHeaders(context.Response);
            ErrorBody error = null;
            var errorStatus = HttpStatusCode.InternalServerError;

            try
            {
                var path = context.Request.Url == null ? "/" : context.Request.Url.AbsolutePath;
                var exclusive = path == "/api/pick-folder" || path == "/api/pick-files"
                    || path == "/api/markdown";
                if (exclusive) await _operationGate.WaitAsync();
                try { await HandleAsync(context); }
                finally { if (exclusive) _operationGate.Release(); }
            }
            catch (DirectoryNotFoundException exception)
            {
                errorStatus = HttpStatusCode.BadRequest;
                error = new ErrorBody(exception.Message);
            }
            catch (FileNotFoundException exception)
            {
                errorStatus = HttpStatusCode.BadRequest;
                error = new ErrorBody(exception.Message);
            }
            catch (ArgumentException exception)
            {
                errorStatus = HttpStatusCode.BadRequest;
                error = new ErrorBody(exception.Message);
            }
            catch (InvalidDataException exception)
            {
                errorStatus = HttpStatusCode.BadRequest;
                error = new ErrorBody(exception.Message);
            }
            catch (UnauthorizedAccessException exception)
            {
                errorStatus = HttpStatusCode.Forbidden;
                error = new ErrorBody(string.Format(
                    "Cannot open this folder: {0}",
                    exception.Message));
            }
            catch (IOException exception)
            {
                errorStatus = HttpStatusCode.BadRequest;
                error = new ErrorBody(string.Format(
                    "Cannot read this folder: {0}",
                    exception.Message));
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(
                    "{0:O} {1} {2}: {3}",
                    DateTimeOffset.Now,
                    context.Request.HttpMethod,
                    context.Request.RawUrl,
                    exception);
                error = new ErrorBody("File2MD encountered an error.");
            }

            if (error != null)
            {
                try
                {
                    if (context.Response.OutputStream.CanWrite)
                    {
                        await WriteJsonAsync(context, errorStatus, error);
                    }
                }
                catch (ObjectDisposedException)
                {
                    // The client disconnected while File2MD was reporting the error.
                }
                catch (HttpListenerException)
                {
                    // The client disconnected while a native picker was open.
                }
                catch (InvalidOperationException)
                {
                    // The response had already started before the disconnect.
                }
            }

            try
            {
                context.Response.OutputStream.Close();
            }
            catch (ObjectDisposedException)
            {
                // The client disconnected after the response was written.
            }
            catch (HttpListenerException)
            {
                // The client disconnected after the response was written.
            }
            catch (InvalidOperationException)
            {
                // The response was already closed.
            }
        }

        private async Task HandleAsync(HttpListenerContext context)
        {
            var request = context.Request;
            var path = request.Url == null ? "/" : request.Url.AbsolutePath;
            var role = GetRequestRole(request);

            if (request.HttpMethod == "GET" && path == "/api/status")
            {
                await WriteJsonAsync(context, HttpStatusCode.OK, BuildStatus(role));
                return;
            }

            if (request.HttpMethod == "POST" && path == "/api/shutdown")
            {
                if (role == "remote")
                {
                    await WriteJsonAsync(
                        context,
                        HttpStatusCode.Forbidden,
                        new ErrorBody("File2MD can only be stopped from this PC."));
                    return;
                }

                int expectedProcessId;
                var currentProcessId = GetCurrentProcessId();
                if (!int.TryParse(
                        request.Headers["X-File2MD-Process-Id"],
                        out expectedProcessId)
                    || expectedProcessId != currentProcessId)
                {
                    await WriteJsonAsync(
                        context,
                        HttpStatusCode.Conflict,
                        new ErrorBody("The running File2MD process changed before it could be stopped."));
                    return;
                }

                await WriteJsonAsync(
                    context,
                    HttpStatusCode.OK,
                    new { Stopping = true, ProcessId = currentProcessId });
                context.Response.OutputStream.Close();
                _requestShutdown();
                return;
            }

            if (request.HttpMethod == "POST" && path == "/api/activate")
            {
                RequireLocal(role, "Open items from the context menu on this computer.");

                int expectedProcessId;
                var currentProcessId = GetCurrentProcessId();
                if (!int.TryParse(
                        request.Headers["X-File2MD-Process-Id"],
                        out expectedProcessId)
                    || expectedProcessId != currentProcessId)
                {
                    await WriteJsonAsync(
                        context,
                        HttpStatusCode.Conflict,
                        new ErrorBody("The running File2MD process changed before it could be activated."));
                    return;
                }

                var body = JsonCodec.ParseObject(await ReadBodyAsync(request));
                string mode = null;
                string selectedPath = null;
                if (body.ContainsKey("mode") || body.ContainsKey("path"))
                {
                    if (!body.ContainsKey("mode") || !body.ContainsKey("path"))
                    {
                        throw new ArgumentException(
                            "Specify both the context menu target and mode.");
                    }
                    mode = ReadMode(ReadString(body, "mode"));
                    selectedPath = ReadString(body, "path");
                }

                var folder = mode == null
                    ? null
                    : _state.SelectPath(mode, selectedPath);
                if (!DesktopActivation.Request(mode))
                {
                    await WriteJsonAsync(
                        context,
                        HttpStatusCode.ServiceUnavailable,
                        new ErrorBody("The File2MD desktop window is not ready."));
                    return;
                }

                await WriteJsonAsync(
                    context,
                    HttpStatusCode.OK,
                    new { Activated = true, Folder = folder });
                return;
            }

            if (request.HttpMethod == "GET" && path == "/api/folder")
            {
                var mode = ReadMode(request.QueryString["mode"]);
                if (role == "remote")
                {
                    await WriteJsonAsync(
                        context,
                        HttpStatusCode.Forbidden,
                        new ErrorBody("Use File2MD on this computer."));
                    return;
                }
                await WriteJsonAsync(context, HttpStatusCode.OK, _state.ReadFolder(mode));
                return;
            }

            if (request.HttpMethod == "POST" && path == "/api/folder")
            {
                if (role == "remote")
                {
                    await WriteJsonAsync(
                        context,
                        HttpStatusCode.Forbidden,
                        new ErrorBody("Select folders on this computer."));
                    return;
                }

                var body = JsonCodec.ParseObject(await ReadBodyAsync(request));
                var mode = ReadMode(body);
                object pathValue;
                var selectedPath = body.TryGetValue("path", out pathValue) ? pathValue as string : null;
                if (string.IsNullOrWhiteSpace(selectedPath))
                {
                    throw new ArgumentException("Select a folder.");
                }

                await WriteJsonAsync(
                    context,
                    HttpStatusCode.OK,
                    _state.SelectFolder(mode, selectedPath));
                return;
            }

            if (request.HttpMethod == "POST" && path == "/api/pick-folder")
            {
                if (role == "remote")
                {
                    await WriteJsonAsync(
                        context,
                        HttpStatusCode.Forbidden,
                        new ErrorBody("Select folders on this computer."));
                    return;
                }

                if (!NativePicker.IsAvailable)
                {
                    await WriteJsonAsync(
                        context,
                        HttpStatusCode.NotImplemented,
                        new ErrorBody("The system folder picker is unavailable in this environment."));
                    return;
                }

                var mode = ReadMode(request.QueryString["mode"]);
                var selectedPath = await NativePicker.PickFolderAsync(_state.FolderPath(mode));
                if (selectedPath == null)
                {
                    await WriteJsonAsync(
                        context,
                        HttpStatusCode.OK,
                        new { Cancelled = true, Folder = (FolderSnapshot)null });
                    return;
                }

                await WriteJsonAsync(
                    context,
                    HttpStatusCode.OK,
                    new
                    {
                        Cancelled = false,
                        Folder = _state.SelectFolder(mode, selectedPath)
                    });
                return;
            }

            if (request.HttpMethod == "POST" && path == "/api/pick-files")
            {
                if (role == "remote")
                {
                    await WriteJsonAsync(
                        context,
                        HttpStatusCode.Forbidden,
                        new ErrorBody("Select files on this computer."));
                    return;
                }

                if (!NativePicker.IsAvailable)
                {
                    await WriteJsonAsync(
                        context,
                        HttpStatusCode.NotImplemented,
                        new ErrorBody("The system file picker is unavailable in this environment."));
                    return;
                }

                var body = JsonCodec.ParseObject(await ReadBodyAsync(request));
                var mode = ReadMode(body);
                var selectedPaths = await NativePicker.PickFilesAsync(_state.FolderPath(mode), mode);
                if (selectedPaths == null || selectedPaths.Count == 0)
                {
                    await WriteJsonAsync(
                        context,
                        HttpStatusCode.OK,
                        new { Cancelled = true, Folder = (FolderSnapshot)null });
                    return;
                }

                await WriteJsonAsync(
                    context,
                    HttpStatusCode.OK,
                    new
                    {
                        Cancelled = false,
                        Folder = _state.SelectFiles(mode, selectedPaths)
                    });
                return;
            }

            if (request.HttpMethod == "POST" && path == "/api/markdown")
            {
                if (role == "remote")
                {
                    await WriteJsonAsync(
                        context,
                        HttpStatusCode.Forbidden,
                        new ErrorBody("Convert files to Markdown on this computer."));
                    return;
                }

                var body = JsonCodec.ParseObject(await ReadBodyAsync(request));
                var selectedNames = ReadStringList(body, "files");
                object combineValue;
                var combine = !body.TryGetValue("combine", out combineValue)
                    || !(combineValue is bool)
                    || (bool)combineValue;
                var snapshot = _state.ReadFolder("markdown");
                var result = await Task.Run(delegate
                {
                    return MarkdownService.Convert(
                        snapshot,
                        selectedNames,
                        combine,
                        _outputRoot,
                        null,
                        ReadOptionalBool(body, "excludeLargeFiles", true));
                });
                _state.RememberOutput("markdown", result.OutputPath);
                _state.ClearSelection("markdown");
                await WriteJsonAsync(context, HttpStatusCode.OK, result);
                return;
            }

            if (request.HttpMethod == "POST" && path == "/api/open-output")
            {
                RequireLocal(role, "Open the output folder on this computer.");
                var body = JsonCodec.ParseObject(await ReadBodyAsync(request));
                var kind = ReadString(body, "kind");
                var outputPath = _state.ReadOutput(kind);
                if (string.IsNullOrWhiteSpace(outputPath))
                {
                    throw new ArgumentException("Convert files before opening the output folder.");
                }
                var openedPath = await Task.Run(delegate
                {
                    return OutputLauncher.OpenFolder(outputPath);
                });
                await WriteJsonAsync(
                    context,
                    HttpStatusCode.OK,
                    new { Opened = true, Path = openedPath });
                return;
            }

            Asset asset;
            if ((request.HttpMethod == "GET" || request.HttpMethod == "HEAD")
                && StaticAssets.TryGet(path, out asset))
            {
                await WriteAssetAsync(context, asset, request.HttpMethod == "HEAD");
                return;
            }

            await WriteJsonAsync(
                context,
                HttpStatusCode.NotFound,
                new ErrorBody("Not found"));
        }

        private object BuildStatus(string role)
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            return new
            {
                Application = "File2MD",
                Device = Environment.MachineName,
                ProcessId = GetCurrentProcessId(),
                MarkdownLargeFileBytes = MarkdownService.LargeFileBytes,
                Platform = PlatformInfo.Name,
                Role = role,
                Version = version == null ? "0.0.0" : version.ToString(3),
                InitialMode = _initialMode,
                Capabilities = new
                {
                    Word = PlatformInfo.IsWindows && WordIsRegistered(),
                    WindowsOcr = PlatformInfo.IsWindows && Environment.OSVersion.Version.Major >= 10,
                    NativePicker = NativePicker.IsAvailable,
                    Camera = false
                }
            };
        }

        private static string ReadMode(Dictionary<string, object> body)
        {
            object value;
            var mode = body.TryGetValue("mode", out value) ? value as string : "markdown";
            return ReadMode(mode);
        }

        private static string ReadMode(string mode)
        {
            if (string.IsNullOrWhiteSpace(mode))
            {
                mode = "markdown";
            }
            if (mode != "markdown")
            {
                throw new ArgumentException("Invalid file selection mode.");
            }
            return mode;
        }

        private static List<string> ReadStringList(
            Dictionary<string, object> body,
            string propertyName)
        {
            object value;
            var raw = body.TryGetValue(propertyName, out value)
                ? value as List<object>
                : null;
            if (raw == null)
            {
                throw new ArgumentException("A file list is required.");
            }

            var result = new List<string>();
            foreach (var item in raw)
            {
                var text = item as string;
                if (string.IsNullOrWhiteSpace(text))
                {
                    throw new ArgumentException("Invalid file name.");
                }
                result.Add(text);
            }
            return result;
        }

        private static string ReadString(
            Dictionary<string, object> body,
            string propertyName)
        {
            object value;
            var result = body.TryGetValue(propertyName, out value) ? value as string : null;
            if (string.IsNullOrWhiteSpace(result))
            {
                throw new ArgumentException("Required value is missing: " + propertyName);
            }
            return result;
        }

        private static int ReadInt(
            Dictionary<string, object> body,
            string propertyName)
        {
            object value;
            if (!body.TryGetValue(propertyName, out value) || value == null)
            {
                throw new ArgumentException("Required value is missing: " + propertyName);
            }

            try
            {
                return Convert.ToInt32(
                    value,
                    System.Globalization.CultureInfo.InvariantCulture);
            }
            catch (Exception exception)
            {
                throw new ArgumentException(
                    "Invalid numeric value: " + propertyName,
                    exception);
            }
        }

        private static bool ReadOptionalBool(
            Dictionary<string, object> body,
            string propertyName,
            bool fallback)
        {
            object value;
            return body.TryGetValue(propertyName, out value) && value is bool
                ? (bool)value
                : fallback;
        }

        private static void RequireLocal(string role, string message)
        {
            if (role == "remote")
            {
                throw new UnauthorizedAccessException(message);
            }
        }

        private static void RequireRemote(string role, string message)
        {
            if (role != "remote")
            {
                throw new UnauthorizedAccessException(message);
            }
        }

        private static int GetCurrentProcessId()
        {
            using (var process = Process.GetCurrentProcess())
            {
                return process.Id;
            }
        }

        private static string GetRequestRole(HttpListenerRequest request)
        {
            var host = request.Headers["Host"];
            if (string.IsNullOrWhiteSpace(host))
            {
                return "remote";
            }

            host = host.Trim();
            if (host.Length > 0 && host[0] == '[')
            {
                var closingBracket = host.IndexOf(']');
                if (closingBracket > 0)
                {
                    host = host.Substring(1, closingBracket - 1);
                }
            }
            else
            {
                var colon = host.LastIndexOf(':');
                if (colon > 0 && host.IndexOf(':') == colon)
                {
                    host = host.Substring(0, colon);
                }
            }

            return string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
                || string.Equals(host, "127.0.0.1", StringComparison.Ordinal)
                ? "local"
                : "remote";
        }

        private static bool WordIsRegistered()
        {
            if (!PlatformInfo.IsWindows)
            {
                return false;
            }

            try
            {
                return Type.GetTypeFromProgID("Word.Application", false) != null;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static async Task<byte[]> ReadBodyAsync(HttpListenerRequest request)
        {
            if (request.ContentLength64 > MaximumRequestBytes)
            {
                throw new ArgumentException("Request body is too large.");
            }

            using (var buffer = new MemoryStream())
            {
                var chunk = new byte[4096];
                while (true)
                {
                    var read = await request.InputStream.ReadAsync(chunk, 0, chunk.Length);
                    if (read == 0)
                    {
                        return buffer.ToArray();
                    }

                    buffer.Write(chunk, 0, read);
                    if (buffer.Length > MaximumRequestBytes)
                    {
                        throw new ArgumentException("Request body is too large.");
                    }
                }
            }
        }

        private static async Task WriteAssetAsync(
            HttpListenerContext context,
            Asset asset,
            bool headOnly)
        {
            var response = context.Response;
            response.StatusCode = (int)HttpStatusCode.OK;
            response.ContentType = asset.ContentType;
            response.ContentLength64 = asset.Bytes.LongLength;
            response.Headers[HttpResponseHeader.CacheControl] = asset.Cacheable
                ? "public, max-age=3600"
                : "no-cache";

            if (!headOnly)
            {
                await response.OutputStream.WriteAsync(asset.Bytes, 0, asset.Bytes.Length);
            }
        }

        private static async Task WriteJsonAsync(
            HttpListenerContext context,
            HttpStatusCode status,
            object body)
        {
            var bytes = JsonCodec.Serialize(body);
            var response = context.Response;
            response.StatusCode = (int)status;
            response.ContentType = "application/json; charset=utf-8";
            response.ContentEncoding = Encoding.UTF8;
            response.ContentLength64 = bytes.LongLength;
            response.Headers[HttpResponseHeader.CacheControl] = "no-store";
            await response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
        }

        private static async Task WriteBytesAsync(
            HttpListenerContext context,
            string contentType,
            byte[] bytes)
        {
            var response = context.Response;
            response.StatusCode = (int)HttpStatusCode.OK;
            response.ContentType = contentType;
            response.ContentLength64 = bytes.LongLength;
            response.Headers[HttpResponseHeader.CacheControl] = "no-store";
            await response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
        }

        private static void AddSecurityHeaders(HttpListenerResponse response)
        {
            response.Headers["Content-Security-Policy"] =
                "default-src 'self'; script-src 'self' 'wasm-unsafe-eval'; worker-src 'self'; " +
                "style-src 'self'; img-src 'self' data:; media-src 'self' blob:; " +
                "connect-src 'self'; frame-ancestors 'none'; " +
                "base-uri 'none'; form-action 'self'";
            response.Headers["Permissions-Policy"] =
                "camera=(self), microphone=(), geolocation=()";
            response.Headers["Referrer-Policy"] = "no-referrer";
            response.Headers["X-Content-Type-Options"] = "nosniff";
            response.Headers["X-Frame-Options"] = "DENY";
        }

        private sealed class ErrorBody
        {
            public ErrorBody(string error)
            {
                Error = error;
            }

            public string Error { get; private set; }
        }
    }
}
