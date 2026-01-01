using Microsoft.AspNetCore.Mvc;
using A3ITranslator.Application.Services;

namespace A3ITranslator.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SessionController : ControllerBase
{
    private readonly ISessionManager _sessionManager;
    private readonly ILogger<SessionController> _logger;

    public SessionController(ISessionManager sessionManager, ILogger<SessionController> logger)
    {
        _sessionManager = sessionManager;
        _logger = logger;
    }

    /// <summary>
    /// Create a new realtime session
    /// </summary>
    [HttpPost("create")]
    public ActionResult<object> CreateSession([FromBody] CreateSessionRequest request)
    {
        try
        {
            var sessionId = Guid.NewGuid().ToString();
            var session = _sessionManager.GetOrCreateSession(sessionId);
            
            _logger.LogInformation("Created new session: {SessionId} for languages {Primary} -> {Secondary}", 
                sessionId, request.PrimaryLanguage, request.SecondaryLanguage);

            return Ok(new
            {
                sessionId = sessionId,
                primaryLanguage = request.PrimaryLanguage ?? "en",
                secondaryLanguage = request.SecondaryLanguage ?? "da",
                status = "active",
                createdAt = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create session");
            return StatusCode(500, new { error = "Failed to create session", message = ex.Message });
        }
    }

    /// <summary>
    /// Get session status
    /// </summary>
    [HttpGet("{sessionId}/status")]
    public ActionResult<object> GetSessionStatus(string sessionId)
    {
        try
        {
            var session = _sessionManager.GetOrCreateSession(sessionId);
            if (session == null)
            {
                return NotFound(new { error = "Session not found", sessionId });
            }

            return Ok(new
            {
                sessionId = sessionId,
                status = "active",
                lastActivity = session.LastActivity,
                connectionId = session.ConnectionId
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get session status for {SessionId}", sessionId);
            return StatusCode(500, new { error = "Failed to get session status", message = ex.Message });
        }
    }
}

public class CreateSessionRequest
{
    public string? PrimaryLanguage { get; set; } = "en";
    public string? SecondaryLanguage { get; set; } = "da";
    public bool IsPremium { get; set; } = false;
}
