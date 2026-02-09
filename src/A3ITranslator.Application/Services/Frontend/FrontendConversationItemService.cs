using A3ITranslator.Application.DTOs.Frontend;
using A3ITranslator.Application.Models;
using A3ITranslator.Application.Models.Conversation;
using A3ITranslator.Application.Models.Speaker;

namespace A3ITranslator.Application.Services.Frontend;

/// <summary>
/// Service for creating frontend conversation items
/// Reusable for translation responses, AI responses, and system messages
/// </summary>
public interface IFrontendConversationItemService
{
    /// <summary>
    /// Create frontend conversation item from utterance and translation response
    /// </summary>
    FrontendConversationItem CreateFromTranslation(
        UtteranceWithContext utterance,
        string translationText,
        string targetLanguage,
        float translationConfidence,
        SpeakerProfile speaker,
        float speakerConfidence,
        string? id = null,
        bool isPartial = false);

    /// <summary>
    /// Create frontend conversation item from AI response
    /// </summary>
    FrontendConversationItem CreateFromAIResponse(
        string sourceLanguage,
        string aiResponseText,
        string aiResponseTranslation,
        string responseLanguage,
        float aiConfidence,
        string? id = null);

    /// <summary>
    /// Create frontend speaker list update from domain models
    /// </summary>
    FrontendSpeakerListUpdate CreateSpeakerListUpdate(
        List<SpeakerProfile> speakers,
        bool hasChanges = true);

    /// <summary>
    /// Create TTS chunk for frontend
    /// </summary>
    FrontendTTSChunk CreateTTSChunk(
        string conversationItemId,
        byte[] audioData,
        string text,
        int chunkIndex,
        int totalChunks,
        double durationMs,
        string audioFormat = "audio/mp3");
}

/// <summary>
/// Implementation of frontend conversation item service
/// </summary>
public class FrontendConversationItemService : IFrontendConversationItemService
{
    public FrontendConversationItem CreateFromTranslation(
        UtteranceWithContext utterance,
        string translationText,
        string targetLanguage,
        float translationConfidence,
        SpeakerProfile speaker,
        float speakerConfidence,
        string? id = null,
        bool isPartial = false)
    {
        return new FrontendConversationItem
        {
            Id = id ?? Guid.NewGuid().ToString(),
            Timestamp = DateTime.UtcNow,
            SpeakerName = speaker.DisplayName,
            SpeakerConfidence = speakerConfidence,
            TranscriptionText = utterance.Text,
            SourceLanguageName = FrontendConversationItem.GetLanguageName(utterance.DominantLanguage),
            TranscriptionConfidence = utterance.TranscriptionConfidence,
            TranslationText = translationText,
            TargetLanguageName = FrontendConversationItem.GetLanguageName(targetLanguage),
            TranslationConfidence = translationConfidence,
            ResponseType = "Translation"
        };
    }

    public FrontendConversationItem CreateFromAIResponse(
        string sourceLanguage,
        string aiResponseText,
        string aiResponseTranslation,
        string responseLanguage,
        float aiConfidence,
        string? id = null)
    {
        return new FrontendConversationItem
        {
            Id = id ?? Guid.NewGuid().ToString(),
            Timestamp = DateTime.UtcNow,
            SpeakerName = "Assistant",
            SpeakerConfidence = 1,
            TranscriptionText = aiResponseText,
            SourceLanguageName = FrontendConversationItem.GetLanguageName(sourceLanguage),
            TranscriptionConfidence = aiConfidence,
            TranslationText = aiResponseTranslation,
            TargetLanguageName = FrontendConversationItem.GetLanguageName(responseLanguage),
            TranslationConfidence = 1,
            ResponseType = "AIResponse"
        };
    }

    public FrontendSpeakerListUpdate CreateSpeakerListUpdate(
        List<SpeakerProfile> speakers,
        bool hasChanges = true)
    {
        return new FrontendSpeakerListUpdate
        {
            Speakers = speakers.Select((s, i) => FrontendSpeakerInfo.FromDomainModel(s, i)).ToList(),
            HasChanges = hasChanges,
            UpdatedAt = DateTime.UtcNow
        };
    }

    public FrontendTTSChunk CreateTTSChunk(
        string conversationItemId,
        byte[] audioData,
        string text,
        int chunkIndex,
        int totalChunks,
        double durationMs,
        string audioFormat = "audio/mp3")
    {
        return new FrontendTTSChunk
        {
            ChunkId = Guid.NewGuid().ToString(),
            ConversationItemId = conversationItemId,
            AudioData = audioData,
            Text = text,
            ChunkIndex = chunkIndex,
            TotalChunks = totalChunks,
            IsFirstChunk = chunkIndex == 0,
            IsLastChunk = chunkIndex == totalChunks - 1,
            AudioFormat = audioFormat,
            DurationMs = durationMs,
            CreatedAt = DateTime.UtcNow
        };
    }
}
