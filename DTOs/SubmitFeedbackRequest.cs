using System.ComponentModel.DataAnnotations;

namespace PKeetDashboard.API.DTOs;

public sealed class SubmitFeedbackRequest
{
    [Required]
    [MinLength(3)]
    [MaxLength(8000)]
    public string Message { get; set; } = string.Empty;
}
