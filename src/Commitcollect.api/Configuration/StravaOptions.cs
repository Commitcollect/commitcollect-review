namespace Commitcollect.api.Configuration;

public class StravaOptions
{
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string RedirectUri { get; set; } = "";
    public string WebhookVerifyToken { get; set; } = string.Empty;


}
