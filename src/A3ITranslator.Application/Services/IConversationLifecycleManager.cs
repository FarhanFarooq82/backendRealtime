namespace A3ITranslator.Application.Services;

/// <summary>
/// Service responsible for managing the lifecycle of conversations and their states
/// </summary>
public interface IConversationLifecycleManager
{
    /// <summary>
    /// Initializes a new session with language configuration
    /// </summary>
    Task InitializeSessionAsync(string connectionId, string sessionId, string primaryLanguage, string secondaryLanguage);

    /// <summary>
    /// Cleans up resources for a disconnected connection
    /// </summary>
    Task CleanupConnectionAsync(string connectionId);

    /// <summary>
    /// Requests a summary of the current conversation
    /// </summary>
    Task RequestSummaryAsync(string connectionId);

    /// <summary>
    /// Finalizes the session and sends transcript via email
    /// </summary>
    Task FinalizeAndMailAsync(string connectionId, List<string> emailAddresses);
}
