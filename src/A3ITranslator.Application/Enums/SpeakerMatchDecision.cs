/// <summary>
/// Decision about whether two speakers are the same person
/// Based on similarity score thresholds
/// Used to determine if new speaker matches existing speaker or is new
/// </summary>
public enum SpeakerMatchDecision
{
    /// <summary>
    /// Score > 0.90: Very high confidence, same speaker
    /// Action: Update existing speaker profile
    /// </summary>
    DefinitelySameSpeaker = 0,

    /// <summary>
    /// Score 0.75-0.90: High confidence, likely same speaker
    /// Action: Update with note about uncertainty
    /// </summary>
    LikelySameSpeaker = 1,

    /// <summary>
    /// Score 0.60-0.75: Medium confidence, uncertain
    /// Action: Create new speaker with reference to similar profile
    /// </summary>
    Uncertain = 2,

    /// <summary>
    /// Score < 0.60: Different speakers
    /// Action: Create completely new speaker profile
    /// </summary>
    DifferentSpeaker = 3
}