using System.Net;
using System.Text;
using System.Text.Json;
using HttpServer.Shared;
using System.Net.Mime;

class MiniHttpServer
{
    public static async Task Main()
    {
        HttpListener? server = null;
        CancellationTokenSource cts = new CancellationTokenSource();

        try
        {
            string settings = File.ReadAllText("settings.json");
            SettingsModel settingsModel = JsonSerializer.Deserialize<SettingsModel>(settings)
                ?? throw new InvalidOperationException("Failed to deserialize settings");

            string staticDir = settingsModel.StaticDirectoryPath.TrimEnd('/') + "/";

            server = new HttpListener();
            server.Prefixes.Add($"http://{settingsModel.Domain}:{settingsModel.Port}/");
            server.Start();

            Console.WriteLine($"Server started at http://{settingsModel.Domain}:{settingsModel.Port}/");
            Console.WriteLine("Введите /stop для остановки сервера");

            Task requestHandlingTask = HandleRequestsAsync(server, staticDir, cts.Token);

            while (true)
            {
                string? input = Console.ReadLine();
                if (input?.Trim() == "/stop")
                {
                    Console.WriteLine("сервер остановлен");
                    cts.Cancel();
                    break;
                }
            }

            try
            {
                await requestHandlingTask;
            }
            catch (OperationCanceledException)
            {

            }
        }
        catch (Exception e) when (e is DirectoryNotFoundException or FileNotFoundException)
        {
            Console.WriteLine("settings.json or static folder not found");
        }
        catch (JsonException e)
        {
            Console.WriteLine("settings.json is incorrect: " + e.Message);
        }
        catch (Exception e)
        {
            Console.WriteLine("Unexpected error: " + e.Message);
        }
        finally
        {
            server?.Stop();
            server?.Close();
            Console.WriteLine("Сервер остановлен.");
        }
    }

    private static async Task HandleRequestsAsync(HttpListener server, string staticDir, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var contextTask = server.GetContextAsync();
                
                var completedTask = await Task.WhenAny(contextTask, Task.Delay(-1, cancellationToken));

                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                HttpListenerContext context = await contextTask;
                var request = context.Request;
                var response = context.Response;

                try
                {
                    string rawUrl = request.Url?.AbsolutePath ?? "/";

                    string localPath = Uri.UnescapeDataString(rawUrl).TrimStart('/');

                    if (localPath.Contains("..") || localPath.StartsWith("/") || localPath.Contains(":"))
                    {
                        SendErrorResponse(response, "403 Forbidden", HttpStatusCode.Forbidden);
                        continue;
                    }

                    if (string.IsNullOrEmpty(localPath) || localPath == "/")
                    {
                        localPath = "index.html";
                    }

                    string fullPath = Path.Combine(staticDir, localPath);

                    if (!File.Exists(fullPath))
                    {
                        SendErrorResponse(response, "404 Not Found", HttpStatusCode.NotFound);
                        continue;
                    }

                    string contentType = GetMimeType(Path.GetExtension(fullPath));

                    byte[] buffer = await File.ReadAllBytesAsync(fullPath);

                    response.ContentType = contentType;
                    response.ContentLength64 = buffer.Length;
                    response.StatusCode = (int)HttpStatusCode.OK;

                    await response.OutputStream.WriteAsync(buffer);
                    response.OutputStream.Close();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error handling request: {ex.Message}");
                    SendErrorResponse(response, "500 Internal Server Error", HttpStatusCode.InternalServerError);
                }
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (HttpListenerException ex) when (ex.ErrorCode == 995)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unexpected error in request loop: {ex.Message}");
            }
        }
    }

    static void SendErrorResponse(HttpListenerResponse response, string message, HttpStatusCode code)
    {
        try
        {
            string html = $"<h1>{message}</h1>";
            byte[] buffer = Encoding.UTF8.GetBytes(html);
            response.ContentType = "text/html; charset=utf-8";
            response.StatusCode = (int)code;
            response.ContentLength64 = buffer.Length;
            response.OutputStream.Write(buffer);
        }
        finally
        {
            response.OutputStream.Close();
        }
    }

    static string GetMimeType(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".html" => "text/html",
            ".htm"  => "text/html",
            ".css"  => "text/css",
            ".js"   => "application/javascript",
            ".json" => "application/json",
            ".png"  => "image/png",
            ".jpg"  => "image/jpeg",
            ".jpeg" => "image/jpeg",
            ".gif"  => "image/gif",
            ".svg"  => "image/svg+xml",
            ".ico"  => "image/x-icon",
            ".woff" => "font/woff",
            ".woff2" => "font/woff2",
            ".txt"  => "text/plain",
        };
    }
}