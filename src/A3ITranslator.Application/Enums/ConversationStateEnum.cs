namespace A3ITranslator.Application.Enums;

/// <summary>
/// Represents the current state of a conversation cycle
/// Used to manage audio reception, processing phases, and frontend communication
/// </summary>
public enum ConversationPhase
{
    /// <summary>
    /// Ready to receive new audio chunks - initial state
    /// Frontend can send audio chunks
    /// </summary>
    Ready = 0,
    
    /// <summary>
    /// Actively receiving and processing audio chunks
    /// STT and Speaker ID running in parallel
    /// Frontend continues sending audio chunks
    /// </summary>
    ReceivingAudio = 1,
    
    /// <summary>
    /// VAD detected silence - processing utterance with GenAI
    /// Frontend should STOP sending audio chunks
    /// GenAI, translation, speaker decision in progress
    /// </summary>
    ProcessingUtterance = 2,
    
    /// <summary>
    /// Sending TTS response and completing cycle
    /// Final response being prepared and sent
    /// </summary>
    SendingResponse = 3
}
