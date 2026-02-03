using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

using MediatR;
using A3ITranslator.Application.Features.Conversation.Commands.StartSession;
using A3ITranslator.Application.Features.AudioProcessing.Commands.ProcessAudioChunk;
using A3ITranslator.Application.Orchestration;
using A3ITranslator.Application.DTOs.Audio;

namespace A3ITranslator.API.Hubs;

/// <summary>
/// Clean SignalR Hub - Pure Domain Architecture
/// Responsibility: Connection management and message routing only
/// </summary>
public class HubClient : Hub<IHubClient>, IDisposable
{
    private readonly ILogger<HubClient> _logger;
    private readonly IMediator _mediator;
    private readonly IConversationOrchestrator _conversationOrchestrator;
    private readonly CancellationTokenSource _hubCancellationTokenSource = new();
    private bool _disposed = false;

    public HubClient(
        ILogger<HubClient> logger,
        IMediator mediator,
        IConversationOrchestrator conversationOrchestrator)
    {
        _logger = logger;
        _mediator = mediator;
        _conversationOrchestrator = conversationOrchestrator;
    }

    public override async Task OnConnectedAsync()
    {
        var connectionId = Context.ConnectionId;
        
        try
        {
            _logger.LogInformation("🔌 New SignalR connection: {ConnectionId}", connectionId);
            
            var httpContext = Context.GetHttpContext();
            string sessionId = httpContext?.Request.Query["sessionId"].ToString() ?? string.Empty;
            string primaryLang = httpContext?.Request.Query["primaryLang"].ToString() ?? string.Empty;
            string secondaryLang = httpContext?.Request.Query["secondaryLang"].ToString() ?? string.Empty;

            // ✅ CLEAN ARCHITECTURE: Create session via Domain Command
            await _mediator.Send(new StartSessionCommand(connectionId, sessionId, primaryLang, secondaryLang));
            
            _logger.LogInformation("✅ Session creation initiated for {ConnectionId}", connectionId);

            // Send connection confirmation
            await Clients.Caller.ReceiveTranscription("Connected successfully", "system", true);

            // ✅ ORCHESTRATOR RESPONSIBILITY: Initialize pipeline when session is ready
            // The orchestrator will fetch the session and get language candidates internally
            _ = Task.Run(async () =>
            {
                try
                {
                    // Let the orchestrator handle session retrieval and language setup
                    await _conversationOrchestrator.InitializeConnectionPipeline(
                        connectionId, 
                        new[] { primaryLang, secondaryLang ?? "en-US" }, 
                        _hubCancellationTokenSource.Token);
                    
                    _logger.LogInformation("🎯 Conversation pipeline initialized for {ConnectionId}", connectionId);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("🛑 Pipeline initialization cancelled for {ConnectionId}", connectionId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Pipeline initialization failed for {ConnectionId}", connectionId);
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Critical error in OnConnectedAsync for {ConnectionId}", connectionId);
            await Clients.Caller.ReceiveError($"Connection failed: {ex.Message}");
        }
        
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (exception != null)
        {
            _logger.LogError(exception, "❌ Client {ConnectionId} disconnected with error", Context.ConnectionId);
        }
        else
        {
            _logger.LogInformation("👋 Client {ConnectionId} disconnected gracefully", Context.ConnectionId);
        }
        
        // Cancel all pending operations for this hub
        if (!_hubCancellationTokenSource.Token.IsCancellationRequested)
        {
            _hubCancellationTokenSource.Cancel();
        }
        
        try
        {
            // ✅ UNIFIED CLEANUP: ConversationOrchestrator handles all pipeline cleanup
            // This includes STT, Speaker, VAD, and all other resources
            await _conversationOrchestrator.CleanupConnection(Context.ConnectionId);
            
            _logger.LogInformation("🧹 Complete cleanup performed for {ConnectionId}", Context.ConnectionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error during cleanup for {ConnectionId}", Context.ConnectionId);
        }
        
        await base.OnDisconnectedAsync(exception);
    }

    protected new virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                try
                {
                    if (!_hubCancellationTokenSource.Token.IsCancellationRequested)
                    {
                        _hubCancellationTokenSource.Cancel();
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error cancelling token during dispose");
                }
                finally
                {
                    _hubCancellationTokenSource?.Dispose();
                }
            }
            _disposed = true;
        }
        
        // Call base dispose
        base.Dispose(disposing);
    }

    public new void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }


    /// <summary>
    /// Process audio chunk from frontend - simplified for clean architecture
    /// Accepts: { audioData: uint8Array/Base64, timestamp: number }
    /// </summary>
    public async Task SendAudioChunk(AudioChunkPayload payload)
    {
        try
        {
            _logger.LogInformation("🎤 RECEIVED SendAudioChunk call for {ConnectionId}", Context.ConnectionId);
            
            if (payload == null)
            {
                _logger.LogWarning("⚠️ Payload is NULL for {ConnectionId}", Context.ConnectionId);
                return;
            }
            
            _logger.LogInformation("📦 Payload received - AudioData: {AudioDataType}, Length: {Length}, Timestamp: {Timestamp}", 
                payload.AudioData?.GetType().Name ?? "null", 
                payload.AudioData?.Length ?? 0, 
                payload.Timestamp);

            if (_hubCancellationTokenSource.Token.IsCancellationRequested)
                return;

            if (payload.AudioData == null || payload.AudioData.Length == 0)
            {
                _logger.LogWarning("⚠️ Empty audio data for {ConnectionId}", Context.ConnectionId);
                return;
            }
            
            _logger.LogDebug("✅ Sending {Bytes} bytes to ProcessAudioChunkCommand", payload.AudioData.Length);
            
            // ✅ CLEAN ARCHITECTURE: Delegate to Domain via Command
            await _mediator.Send(new ProcessAudioChunkCommand(
                Context.ConnectionId, 
                payload.AudioData, 
                payload.Timestamp));
                
            _logger.LogDebug("✅ Audio chunk processed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Audio processing error for {ConnectionId}: {Message}", Context.ConnectionId, ex.Message);
            await Clients.Caller.ReceiveError($"Audio processing failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Signal utterance completion from frontend VAD
    /// </summary>
    public async Task CompleteUtterance()
    {
        try
        {
            _logger.LogDebug("🔇 Frontend VAD completion signal for {ConnectionId}", Context.ConnectionId);

            if (_hubCancellationTokenSource.Token.IsCancellationRequested)
                return;

            // ✅ CLEAN ARCHITECTURE: Signal completion to orchestrator
            await _conversationOrchestrator.CompleteUtteranceAsync(Context.ConnectionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Utterance completion error for {ConnectionId}", Context.ConnectionId);
            await Clients.Caller.ReceiveError("Utterance completion failed");
        }
    }

    /// <summary>
    /// Signal utterance cancellation from frontend
    /// </summary>
    public async Task CancelUtterance()
    {
        try
        {
            _logger.LogInformation("🛑 Frontend CANCEL signal for {ConnectionId}", Context.ConnectionId);

            if (_hubCancellationTokenSource.Token.IsCancellationRequested)
                return;

            // ✅ CLEAN ARCHITECTURE: Delegate cancellation to orchestrator
            await _conversationOrchestrator.CancelUtteranceAsync(Context.ConnectionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Utterance cancellation error for {ConnectionId}", Context.ConnectionId);
            await Clients.Caller.ReceiveError("Utterance cancellation failed");
        }
    }

    /// <summary>
    /// Request an AI summary of the current session
    /// </summary>
    public async Task RequestSummary()
    {
        try
        {
            _logger.LogInformation("📝 Requesting summary for {ConnectionId}", Context.ConnectionId);
            await _conversationOrchestrator.RequestSummaryAsync(Context.ConnectionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ RequestSummary error for {ConnectionId}", Context.ConnectionId);
            await Clients.Caller.ReceiveError("Summary request failed");
        }
    }

    /// <summary>
    /// Finalize session and mail PDF to provided addresses
    /// </summary>
    public async Task FinalizeAndMail(List<string> emailAddresses)
    {
        try
        {
            _logger.LogInformation("📧 Finalizing and mailing for {ConnectionId} to {Count} addresses", 
                Context.ConnectionId, emailAddresses?.Count ?? 0);
            
            if (emailAddresses == null || !emailAddresses.Any())
            {
                await Clients.Caller.ReceiveError("No email addresses provided");
                return;
            }

            await _conversationOrchestrator.FinalizeAndMailAsync(Context.ConnectionId, emailAddresses);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ FinalizeAndMail error for {ConnectionId}", Context.ConnectionId);
            await Clients.Caller.ReceiveError("Finalization failed");
        }
    }
}
