using Dalamud.Plugin.Services;
using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TargetBarkNotifier;

public sealed class MonitorClient : IDisposable
{
    private const int HeartbeatIntervalMs = 15000;
    private const int ConnectTimeoutMs = 5000;
    private const int HeartbeatResponseTimeoutMs = 3000;
    private const int MaxReconnectAttempts = 3;
    private const int ReconnectDelayMs = 5000;

    private readonly Configuration config;
    private TcpClient? tcpClient;
    private NetworkStream? stream;
    private Timer? heartbeatTimer;
    private CancellationTokenSource? cts;
    private string clientId = string.Empty;
    private bool isConnected;
    private bool isConnecting;

    public bool IsConnected => isConnected;
    public bool IsConnecting => isConnecting;
    public bool ConnectionFailed => cts == null && !isConnected;
    public event Action<string>? OnLog;
    public event Action? OnConnectionLost;

    private void Log(string message)
    {
        OnLog?.Invoke(message);
    }

    public MonitorClient(Configuration config)
    {
        this.config = config;
    }

    public async Task StartAsync()
    {
        if (!config.EnableMonitor)
        {
            return;
        }

        if (isConnected)
        {
            return;
        }

        if (cts != null)
        {
            return;
        }

        if (string.IsNullOrEmpty(config.MonitorHost))
        {
            return;
        }

        cts = new CancellationTokenSource();
        isConnecting = true;
        Log("启动连接");

        try
        {
            await ConnectAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            Log("已取消");
        }
        finally
        {
            isConnecting = false;
        }

        if (!isConnected)
        {
            Cleanup();
            OnConnectionLost?.Invoke();
        }
    }

    private async Task ConnectAsync(CancellationToken token)
    {
        clientId = string.IsNullOrEmpty(config.MonitorToken) 
            ? $"TBN_{Environment.MachineName}_{new Random().Next(1000, 9999)}" 
            : config.MonitorToken;

        Log("连接 " + config.MonitorHost + ":" + config.MonitorPort);

        var authPayload = $"{clientId}|{config.BarkToken ?? ""}|{config.NotifyMeUuid ?? ""}|{config.ServerChan3Key ?? ""}";

        for (int attempt = 0; attempt < MaxReconnectAttempts; attempt++)
        {
            token.ThrowIfCancellationRequested();

            try
            {
                using var linkCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                linkCts.CancelAfter(ConnectTimeoutMs);

                tcpClient = new TcpClient();
                await tcpClient.ConnectAsync(config.MonitorHost, config.MonitorPort, linkCts.Token).ConfigureAwait(false);
                
                token.ThrowIfCancellationRequested();

                stream = tcpClient.GetStream();
                stream.ReadTimeout = ConnectTimeoutMs;
                stream.WriteTimeout = ConnectTimeoutMs;

                var authData = Encoding.UTF8.GetBytes($"auth:{authPayload}");
                await stream.WriteAsync(authData, token).ConfigureAwait(false);
                await stream.FlushAsync(token).ConfigureAwait(false);

                token.ThrowIfCancellationRequested();

                var buffer = new byte[256];
                var bytesRead = await stream.ReadAsync(buffer, token).ConfigureAwait(false);
                var response = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                if (response.StartsWith("OK"))
                {
                    isConnected = true;
                    Log("已连接，准备启动心跳");
                    StartHeartbeat();
                    Log("StartHeartbeat 已调用");
                    return;
                }
            }
            catch (Exception ex)
            {
                try { tcpClient?.Close(); } catch { }
                tcpClient = null;
                Log("失败 " + (attempt + 1) + "/3: " + ex.Message);
            }

            if (attempt < MaxReconnectAttempts - 1)
            {
                token.ThrowIfCancellationRequested();
                await Task.Delay(ReconnectDelayMs, token).ConfigureAwait(false);
            }
        }

        Log("连接失败");
    }

    private void StartHeartbeat()
    {
        heartbeatTimer = new Timer(async _ => 
        { 
            try { await SendHeartbeatAsync(); } 
            catch (Exception ex) { Log("心跳异常: " + ex.Message); } 
        }, null, HeartbeatIntervalMs, HeartbeatIntervalMs);
        
        Log("心跳已启动");
    }

    private async Task SendHeartbeatAsync()
    {
        if (!isConnected || stream == null || cts == null)
        {
            return;
        }

        Log("发送心跳");

        try
        {
            var data = Encoding.UTF8.GetBytes("heartbeat");
            await stream.WriteAsync(data, cts.Token).ConfigureAwait(false);
            await stream.FlushAsync(cts.Token).ConfigureAwait(false);

            var buffer = new byte[64];
            stream.ReadTimeout = HeartbeatResponseTimeoutMs;
            var bytesRead = await stream.ReadAsync(buffer, cts.Token).ConfigureAwait(false);
            var response = Encoding.UTF8.GetString(buffer, 0, bytesRead);

            if (response.StartsWith("OK"))
            {
                Log("心跳 OK");
            }
        }
        catch (Exception ex)
        {
            Log("心跳失败: " + ex.Message);
            Log("设置 isConnected = false");
            isConnected = false;
            Log("释放 heartbeatTimer");
            heartbeatTimer?.Dispose();
            Log("调用 Cleanup");
            Cleanup();
            Log("调用 OnConnectionLost");
            OnConnectionLost?.Invoke();
        }
    }

    public async Task StopAsync()
    {
        Log("停止");

        if (cts != null)
        {
            try { cts.Cancel(); } catch { }
            cts = null;
        }

        heartbeatTimer?.Dispose();

        if (!isConnected || stream == null)
        {
            Log("未连接");
            Cleanup();
            return;
        }

        try
        {
            var data = Encoding.UTF8.GetBytes("stop");
            await stream.WriteAsync(data).ConfigureAwait(false);
            await stream.FlushAsync().ConfigureAwait(false);
            Log("已停止");
        }
        catch (Exception ex)
        {
            Log("停止异常: " + ex.Message);
        }
        finally
        {
            Cleanup();
        }
    }

    private void Cleanup()
    {
        Log("Cleanup: cts=" + (cts != null));
        
        try { stream?.Close(); } catch { }
        stream = null;

        try { tcpClient?.Close(); } catch { }
        tcpClient = null;

        if (cts != null)
        {
            try { cts.Cancel(); } catch { }
            try { cts.Dispose(); } catch { }
            cts = null;
        }

        isConnected = false;

        Log("已清理");
    }

    public void Dispose()
    {
        Log("Dispose: cts=" + (cts != null));
        
        if (cts != null)
        {
            try { cts.Cancel(); } catch { }
            try { cts.Dispose(); } catch { }
            cts = null;
        }

        heartbeatTimer?.Dispose();
        heartbeatTimer = null;

        try { stream?.Close(); } catch { }
        stream = null;

        try { tcpClient?.Close(); } catch { }
        tcpClient = null;

        isConnected = false;

        Log("Dispose 完成");
    }
}