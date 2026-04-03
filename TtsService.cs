using Dalamud.Plugin.Services;
using System;
using System.Reflection;
using System.Runtime.InteropServices;

namespace TargetBarkNotifier;

public sealed class TtsService : IDisposable
{
    private readonly IPluginLog log;
    private readonly object? sapiVoice;
    private readonly object ttsLock = new();

    public TtsService(IPluginLog log)
    {
        this.log = log;
        sapiVoice = CreateSapiVoice();
    }

    public void TrySpeak(string content)
    {
        if (sapiVoice is null)
            return;

        var speakText = string.IsNullOrWhiteSpace(content) ? "收到匹配消息" : content;

        try
        {
            lock (ttsLock)
            {
                _ = sapiVoice.GetType().InvokeMember(
                    "Volume",
                    BindingFlags.SetProperty,
                    null,
                    sapiVoice,
                    [100]);

                _ = sapiVoice.GetType().InvokeMember(
                    "Rate",
                    BindingFlags.SetProperty,
                    null,
                    sapiVoice,
                    [0]);

                _ = sapiVoice.GetType().InvokeMember(
                    "Speak",
                    BindingFlags.InvokeMethod,
                    null,
                    sapiVoice,
                    [speakText, 1]);
            }
        }
        catch (Exception ex)
        {
            log.Warning(ex, "TTS failed.");
        }
    }

    public void Dispose()
    {
        if (sapiVoice is not null)
        {
            try
            {
                Marshal.FinalReleaseComObject(sapiVoice);
            }
            catch
            {
            }
        }
    }

    private object? CreateSapiVoice()
    {
        try
        {
            var type = Type.GetTypeFromProgID("SAPI.SpVoice");
            if (type is null)
            {
                log.Warning("SAPI.SpVoice not found, TTS disabled.");
                return null;
            }

            return Activator.CreateInstance(type);
        }
        catch (Exception ex)
        {
            log.Warning(ex, "Failed to initialize SAPI TTS.");
            return null;
        }
    }
}
