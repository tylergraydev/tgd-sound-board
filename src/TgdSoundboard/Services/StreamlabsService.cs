using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TgdSoundboard.Services;

public class StreamlabsService : IDisposable
{
    private ClientWebSocket? _webSocket;
    private string _token = string.Empty;
    private int _requestId;
    private bool _isConnected;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public bool IsConnected => _isConnected;
    public string? LastError { get; private set; }

    public event EventHandler<bool>? ConnectionChanged;
    public event EventHandler<string>? ErrorOccurred;
    public event EventHandler? ReplaySaved;

    public void SetToken(string token)
    {
        _token = token;
    }

    public async Task<bool> ConnectAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_token))
        {
            LastError = "No token configured";
            ErrorOccurred?.Invoke(this, LastError);
            return false;
        }

        try
        {
            await DisconnectAsync();

            _webSocket = new ClientWebSocket();
            _webSocket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);

            await _webSocket.ConnectAsync(new Uri("ws://127.0.0.1:59650/api/websocket"), ct);

            // Authenticate with token
            var authResult = await SendRequestAsync("auth", "TcpServerService", [_token], ct);

            if (authResult.Success)
            {
                _isConnected = true;
                LastError = null;
                ConnectionChanged?.Invoke(this, true);
                return true;
            }
            else
            {
                LastError = authResult.ErrorMessage ?? "Authentication failed";
                ErrorOccurred?.Invoke(this, LastError);
                await DisconnectAsync();
                return false;
            }
        }
        catch (WebSocketException ex)
        {
            LastError = $"Could not connect to Streamlabs Desktop. Is it running? ({ex.Message})";
            ErrorOccurred?.Invoke(this, LastError);
            return false;
        }
        catch (Exception ex)
        {
            LastError = $"Connection failed: {ex.Message}";
            ErrorOccurred?.Invoke(this, LastError);
            return false;
        }
    }

    public async Task<bool> SaveReplayAsync(CancellationToken ct = default)
    {
        if (!_isConnected)
        {
            LastError = "Not connected to Streamlabs";
            return false;
        }

        var result = await SendRequestAsync("saveReplay", "StreamingService", null, ct);

        if (result.Success)
        {
            ReplaySaved?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            LastError = result.ErrorMessage ?? "Failed to save replay";
            ErrorOccurred?.Invoke(this, LastError);
        }

        return result.Success;
    }

    public async Task<bool> StartReplayBufferAsync(CancellationToken ct = default)
    {
        if (!_isConnected) return false;
        var result = await SendRequestAsync("startReplayBuffer", "StreamingService", null, ct);
        return result.Success;
    }

    public async Task<bool> StopReplayBufferAsync(CancellationToken ct = default)
    {
        if (!_isConnected) return false;
        var result = await SendRequestAsync("stopReplayBuffer", "StreamingService", null, ct);
        return result.Success;
    }

    public async Task<StreamingState?> GetStreamingStateAsync(CancellationToken ct = default)
    {
        if (!_isConnected) return null;

        var result = await SendRequestAsync("getModel", "StreamingService", null, ct);
        if (result.Success && result.Data != null)
        {
            try
            {
                return JsonSerializer.Deserialize<StreamingState>(result.Data.Value.GetRawText(), JsonOptions);
            }
            catch
            {
                return null;
            }
        }
        return null;
    }

    public async Task<List<string>> GetScenesAsync(CancellationToken ct = default)
    {
        if (!_isConnected) return [];

        var result = await SendRequestAsync("getScenes", "ScenesService", null, ct);
        if (result.Success && result.Data != null)
        {
            try
            {
                var scenes = JsonSerializer.Deserialize<List<SceneInfo>>(result.Data.Value.GetRawText(), JsonOptions);
                return scenes?.Select(s => s.Name).ToList() ?? [];
            }
            catch
            {
                return [];
            }
        }
        return [];
    }

    public async Task<bool> ChangeSceneAsync(string sceneName, CancellationToken ct = default)
    {
        if (!_isConnected) return false;

        // Get scenes to find the scene ID
        var result = await SendRequestAsync("getScenes", "ScenesService", null, ct);
        if (!result.Success || result.Data == null) return false;

        try
        {
            var scenes = JsonSerializer.Deserialize<List<SceneInfo>>(result.Data.Value.GetRawText(), JsonOptions);
            var scene = scenes?.FirstOrDefault(s => s.Name.Equals(sceneName, StringComparison.OrdinalIgnoreCase));

            if (scene == null) return false;

            var switchResult = await SendRequestAsync("makeSceneActive", "ScenesService", [scene.Id], ct);
            return switchResult.Success;
        }
        catch
        {
            return false;
        }
    }

    #region Audio Source Management

    public async Task<string?> GetActiveSceneIdAsync(CancellationToken ct = default)
    {
        if (!_isConnected) return null;

        var result = await SendRequestAsync("activeSceneId", "ScenesService", null, ct);
        if (result.Success && result.Data != null)
        {
            return result.Data.Value.GetString();
        }
        return null;
    }

    public async Task<bool> AddApplicationAudioCaptureAsync(string processName, string? sourceName = null, CancellationToken ct = default)
    {
        if (!_isConnected)
        {
            LastError = "Not connected to Streamlabs";
            return false;
        }

        try
        {
            // Get active scene
            var sceneId = await GetActiveSceneIdAsync(ct);
            if (sceneId == null)
            {
                LastError = "Could not get active scene";
                return false;
            }

            // Create the source name
            var name = sourceName ?? $"Audio: {processName}";

            // Create application audio capture source
            // Using wasapi_process_output_capture type for OBS/Streamlabs
            // Window format is "title:class:executable" - use empty title and class to match by exe only
            // Priority: 0=Title, 1=Class, 2=Exe
            var windowString = $"::{processName}.exe";
            var settings = new Dictionary<string, object>
            {
                ["window"] = windowString,
                ["priority"] = 2  // Match by executable
            };

            Console.WriteLine($"[Streamlabs] Creating source: name={name}, type=wasapi_process_output_capture, window={windowString}, priority=2");

            // First create the source using SourcesService
            var createResult = await SendRequestAsync("createSource", "SourcesService",
                [name, "wasapi_process_output_capture", settings], ct);

            if (!createResult.Success)
            {
                LastError = createResult.ErrorMessage ?? "Unknown error creating source";
                Console.WriteLine($"[Streamlabs] Failed to create source: {LastError}");
                return false;
            }

            // Get the source ID from the result and add it to the scene
            if (createResult.Data != null)
            {
                try
                {
                    var sourceId = createResult.Data.Value.GetProperty("id").GetString();
                    if (!string.IsNullOrEmpty(sourceId))
                    {
                        // Use the Scene resource format to add source
                        var sceneResource = $"Scene[\"{sceneId}\"]";
                        Console.WriteLine($"[Streamlabs] Source created with id={sourceId}, adding to scene resource={sceneResource}");
                        var addResult = await SendRequestAsync("addSource", sceneResource, [sourceId], ct);
                        if (!addResult.Success)
                        {
                            Console.WriteLine($"[Streamlabs] Failed to add source to scene: {addResult.ErrorMessage}");
                            // Source was created, that's partial success
                            LastError = $"Source created but could not add to scene: {addResult.ErrorMessage}";
                        }
                        return addResult.Success;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Streamlabs] Error parsing source result: {ex.Message}");
                }
            }

            // Source was created even if we couldn't add to scene
            return true;
        }
        catch (Exception ex)
        {
            LastError = $"Failed to add audio source: {ex.Message}";
            ErrorOccurred?.Invoke(this, LastError);
            return false;
        }
    }

    public async Task<bool> AddSoundboardAudioSourceAsync(CancellationToken ct = default)
    {
        // Add this application (TgdSoundboard) as an audio source
        return await AddApplicationAudioCaptureAsync("TgdSoundboard", "TGD Soundboard Audio", ct);
    }

    public async Task<List<SourceInfo>> GetSourcesAsync(CancellationToken ct = default)
    {
        if (!_isConnected) return [];

        try
        {
            var sceneId = await GetActiveSceneIdAsync(ct);
            if (sceneId == null) return [];

            // Get the scene's items
            var result = await SendRequestAsync("getItems", "ScenesService", [sceneId], ct);
            if (result.Success && result.Data != null)
            {
                var items = JsonSerializer.Deserialize<List<SourceInfo>>(result.Data.Value.GetRawText(), JsonOptions);
                return items ?? [];
            }
        }
        catch { }

        return [];
    }

    public async Task<bool> RemoveSourceAsync(string sourceId, CancellationToken ct = default)
    {
        if (!_isConnected) return false;

        try
        {
            var result = await SendRequestAsync("remove", "Source", [sourceId], ct);
            return result.Success;
        }
        catch
        {
            return false;
        }
    }

    #endregion

    private async Task<RpcResult> SendRequestAsync(string method, string resource, object[]? args, CancellationToken ct)
    {
        if (_webSocket?.State != WebSocketState.Open)
        {
            _isConnected = false;
            ConnectionChanged?.Invoke(this, false);
            return new RpcResult { Success = false, ErrorMessage = "Not connected" };
        }

        await _sendLock.WaitAsync(ct);
        try
        {
            var request = new RpcRequest
            {
                JsonRpc = "2.0",
                Id = Interlocked.Increment(ref _requestId),
                Method = method,
                Params = new RpcParams
                {
                    Resource = resource,
                    Args = args
                }
            };

            var json = JsonSerializer.Serialize(request, JsonOptions);
            var bytes = Encoding.UTF8.GetBytes(json);

            await _webSocket.SendAsync(bytes, WebSocketMessageType.Text, true, ct);

            // Read response
            var buffer = new byte[16384];
            var response = new StringBuilder();
            WebSocketReceiveResult result;

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));

            do
            {
                result = await _webSocket.ReceiveAsync(buffer, timeoutCts.Token);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _isConnected = false;
                    ConnectionChanged?.Invoke(this, false);
                    return new RpcResult { Success = false, ErrorMessage = "Connection closed" };
                }

                response.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            } while (!result.EndOfMessage);

            var rpcResponse = JsonSerializer.Deserialize<RpcResponse>(response.ToString(), JsonOptions);
            return new RpcResult
            {
                Success = rpcResponse?.Error == null,
                Data = rpcResponse?.Result,
                ErrorMessage = rpcResponse?.Error?.Message
            };
        }
        catch (OperationCanceledException)
        {
            return new RpcResult { Success = false, ErrorMessage = "Request timed out" };
        }
        catch (WebSocketException ex)
        {
            _isConnected = false;
            ConnectionChanged?.Invoke(this, false);
            return new RpcResult { Success = false, ErrorMessage = ex.Message };
        }
        catch (Exception ex)
        {
            return new RpcResult { Success = false, ErrorMessage = ex.Message };
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async Task DisconnectAsync()
    {
        if (_webSocket?.State == WebSocketState.Open)
        {
            try
            {
                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
            }
            catch { }
        }

        _webSocket?.Dispose();
        _webSocket = null;
        _isConnected = false;
        ConnectionChanged?.Invoke(this, false);
    }

    public void Dispose()
    {
        _webSocket?.Dispose();
        _sendLock.Dispose();
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

// Request/Response models
internal class RpcRequest
{
    public string JsonRpc { get; set; } = "2.0";
    public int Id { get; set; }
    public string Method { get; set; } = "";
    public RpcParams? Params { get; set; }
}

internal class RpcParams
{
    public string Resource { get; set; } = "";
    public object[]? Args { get; set; }
}

internal class RpcResponse
{
    public string? JsonRpc { get; set; }
    public int Id { get; set; }
    public JsonElement? Result { get; set; }
    public RpcError? Error { get; set; }
}

internal class RpcError
{
    public int Code { get; set; }
    public string? Message { get; set; }
}

internal class RpcResult
{
    public bool Success { get; set; }
    public JsonElement? Data { get; set; }
    public string? ErrorMessage { get; set; }
}

public class StreamingState
{
    public string? StreamingStatus { get; set; }
    public string? RecordingStatus { get; set; }
    public string? ReplayBufferStatus { get; set; }
    public bool ReplayBufferStatusTime { get; set; }
}

internal class SceneInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}

public class SourceInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string SourceId { get; set; } = "";
}
