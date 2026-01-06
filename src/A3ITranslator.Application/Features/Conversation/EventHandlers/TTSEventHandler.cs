using MediatR;
using A3ITranslator.Application.Domain.Events;
using A3ITranslator.Application.Services;
using Microsoft.Extensions.Logging;

namespace A3ITranslator.Application.Features.Conversation.EventHandlers;

public class TTSEventHandler : INotificationHandler<TranslationCompleted>
{
    private readonly IStreamingTTSService _ttsService;
    private readonly IRealtimeNotificationService _notificationService;
    private readonly ILogger<TTSEventHandler> _logger;

    public TTSEventHandler(
        IStreamingTTSService ttsService,
        IRealtimeNotificationService notificationService,
        ILogger<TTSEventHandler> logger)
    {
        _ttsService = ttsService;
        _notificationService = notificationService;
        _logger = logger;
    }

    public async Task Handle(TranslationCompleted notification, CancellationToken cancellationToken)
    {
        var connectionId = notification.Session.ConnectionId;
        var text = notification.TranslatedText;
        var language = notification.TargetLanguage; // Or "en-US" hardcoded if that's the model constraint

        try
        {
            _logger.LogInformation("🗣️ Starting TTS for Connection {ConnectionId} - Text: {Text}", connectionId, text);

            var sentences = SplitIntoSentences(text);

            foreach (var sentence in sentences)
            {
                // Notify frontend of the text currently being spoken (subtitle)
                await _notificationService.NotifyTranscriptionAsync(connectionId, sentence, "en-US", true); // Hardcoded 'en-US' for now matching old logic

                try
                {
                    await foreach (var chunk in _ttsService.SynthesizeStreamAsync(sentence, "en-US", "en-US-JennyNeural"))
                    {
                        string base64Audio = Convert.ToBase64String(chunk.AudioData);
                        await _notificationService.NotifyAudioChunkAsync(connectionId, base64Audio);
                    }
                }
                catch (Exception ex)
                {
                     _logger.LogError(ex, "TTS synthesis failed for sentence: {Sentence}", sentence);
                }
            }
            
            // Notify transaction complete (Old logic did this at end of orchestrator)
            // Ideally, we should have a TransactionCompleted event? 
            // Or just do it here as it is the "End of the Line".
            await _notificationService.NotifyTransactionCompleteAsync(connectionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling TTS for Connection {ConnectionId}", connectionId);
             await _notificationService.NotifyErrorAsync(connectionId, "TTS processing failed");
        }
    }

    private IEnumerable<string> SplitIntoSentences(string text)
    {
        if (string.IsNullOrEmpty(text)) return Enumerable.Empty<string>();
        return text.Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries)
                   .Select(s => s.Trim())
                   .Where(s => !string.IsNullOrEmpty(s));
    }
}
