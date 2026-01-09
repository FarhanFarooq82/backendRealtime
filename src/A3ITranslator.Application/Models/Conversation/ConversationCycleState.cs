using A3ITranslator.Application.Enums;

namespace A3ITranslator.Application.Models.Conversation;

/// <summary>
/// Represents the state of a conversation cycle with proper phase management
/// Domain model for tracking conversation processing phases
/// </summary>
public class ConversationCycleState
{
    /// <summary>
    /// Current phase of the conversation cycle
    /// </summary>
    public ConversationPhase CurrentPhase { get; private set; } = ConversationPhase.Ready;
    
    /// <summary>
    /// Unique identifier for this conversation cycle
    /// </summary>
    public string CycleId { get; private set; } = Guid.NewGuid().ToString();
    
    /// <summary>
    /// Timestamp when the cycle started
    /// </summary>
    public DateTime CycleStartedAt { get; private set; }
    
    /// <summary>
    /// Timestamp when audio reception began
    /// </summary>
    public DateTime? AudioReceptionStartedAt { get; private set; }
    
    /// <summary>
    /// Timestamp when processing began (VAD timeout)
    /// </summary>
    public DateTime? ProcessingStartedAt { get; private set; }
    
    /// <summary>
    /// Whether the frontend should stop sending audio chunks
    /// </summary>
    public bool ShouldStopAudioReception => CurrentPhase >= ConversationPhase.ProcessingUtterance;
    
    /// <summary>
    /// Whether new audio chunks are being accepted
    /// </summary>
    public bool IsAcceptingAudio => CurrentPhase <= ConversationPhase.ReceivingAudio;
    
    /// <summary>
    /// Transition to ReceivingAudio phase
    /// </summary>
    public void StartReceivingAudio()
    {
        if (CurrentPhase == ConversationPhase.Ready)
        {
            CurrentPhase = ConversationPhase.ReceivingAudio;
            AudioReceptionStartedAt = DateTime.UtcNow;
            if (CycleStartedAt == default)
            {
                CycleStartedAt = DateTime.UtcNow;
            }
        }
    }
    
    /// <summary>
    /// Transition to ProcessingUtterance phase (VAD timeout detected)
    /// </summary>
    public void StartProcessingUtterance()
    {
        if (CurrentPhase == ConversationPhase.ReceivingAudio)
        {
            CurrentPhase = ConversationPhase.ProcessingUtterance;
            ProcessingStartedAt = DateTime.UtcNow;
        }
    }
    
    /// <summary>
    /// Transition to SendingResponse phase
    /// </summary>
    public void StartSendingResponse()
    {
        if (CurrentPhase == ConversationPhase.ProcessingUtterance)
        {
            CurrentPhase = ConversationPhase.SendingResponse;
        }
    }
    
    /// <summary>
    /// Reset to Ready phase for next cycle
    /// </summary>
    public void ResetForNextCycle()
    {
        CurrentPhase = ConversationPhase.Ready;
        CycleId = Guid.NewGuid().ToString();
        CycleStartedAt = default;
        AudioReceptionStartedAt = null;
        ProcessingStartedAt = null;
    }
    
    /// <summary>
    /// Get phase duration for current phase
    /// </summary>
    public TimeSpan? GetCurrentPhaseDuration()
    {
        var now = DateTime.UtcNow;
        return CurrentPhase switch
        {
            ConversationPhase.Ready => null,
            ConversationPhase.ReceivingAudio when AudioReceptionStartedAt.HasValue => now - AudioReceptionStartedAt.Value,
            ConversationPhase.ProcessingUtterance when ProcessingStartedAt.HasValue => now - ProcessingStartedAt.Value,
            ConversationPhase.SendingResponse when ProcessingStartedAt.HasValue => now - ProcessingStartedAt.Value,
            _ => null
        };
    }
}
