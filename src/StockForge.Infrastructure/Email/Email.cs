using System.Net;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using StockForge.Application.Abstractions;

namespace StockForge.Infrastructure.Email;

public sealed class SmtpOptions
{
    public const string Section = "Smtp";

    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 1025;
    public bool UseStartTls { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string FromAddress { get; set; } = "no-reply@stockforge.local";
    public string FromName { get; set; } = "StockForge";
}

public sealed class AppUrlOptions
{
    public const string Section = "App";

    /// <summary>Public URL of the web frontend, used in email links.</summary>
    public string FrontendBaseUrl { get; set; } = "http://localhost:3000";
}

internal sealed class SmtpEmailSender(IOptions<SmtpOptions> options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var smtp = options.Value;
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(smtp.FromName, smtp.FromAddress));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder { HtmlBody = message.HtmlBody, TextBody = message.TextBody }.ToMessageBody();

        using var client = new SmtpClient();
        await client.ConnectAsync(smtp.Host, smtp.Port, smtp.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None, cancellationToken);
        if (!string.IsNullOrEmpty(smtp.Username))
        {
            await client.AuthenticateAsync(smtp.Username, smtp.Password ?? string.Empty, cancellationToken);
        }

        await client.SendAsync(new SendRequest(mime), cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
        // Never log bodies: they can contain password-reset or invitation links.
        logger.LogInformation("Email '{Subject}' sent", message.Subject);
    }
}

/// <summary>Minimal, dependency-free HTML email templates. All interpolated values are HTML-encoded.</summary>
public static class EmailTemplates
{
    public static (string Subject, string Html, string Text) PasswordReset(string name, string link) => Build(
        "Reset your StockForge password",
        $"Hi {name},",
        "We received a request to reset your password. The link below is valid for 2 hours. If you did not request this, you can ignore this email.",
        "Reset password", link);

    public static (string Subject, string Html, string Text) Invitation(string name, string organization, string link) => Build(
        $"You've been invited to {organization} on StockForge",
        $"Hi {name},",
        $"You have been invited to join {organization} on StockForge. Use the link below to set your password and sign in. The invitation expires in 3 days.",
        "Accept invitation", link);

    public static (string Subject, string Html, string Text) AddedToOrganization(string name, string organization, string link) => Build(
        $"You now have access to {organization}",
        $"Hi {name},",
        $"Your StockForge account was added to {organization}. Sign in and switch organization to start working.",
        "Open StockForge", link);

    public static (string Subject, string Html, string Text) LowStock(string product, string sku, string warehouse, decimal available, decimal reorderLevel, string link) => Build(
        $"Low stock: {sku} at {warehouse}",
        "Low-stock alert",
        $"{product} ({sku}) at {warehouse} has {available:0.###} units available, at or below its reorder level of {reorderLevel:0.###}.",
        "View inventory", link);

    public static (string Subject, string Html, string Text) DailySummary(string organization, DateOnly date, IReadOnlyList<(string Label, string Value)> rows, string link)
    {
        var table = string.Join(string.Empty, rows.Select(r =>
            $"<tr><td style=\"padding:6px 12px;color:#475569\">{WebUtility.HtmlEncode(r.Label)}</td><td style=\"padding:6px 12px;font-weight:600;color:#0f172a\">{WebUtility.HtmlEncode(r.Value)}</td></tr>"));
        var subject = $"{organization} inventory summary — {date:yyyy-MM-dd}";
        var html = Layout($"<h1 style=\"font-size:18px;color:#0f172a\">{WebUtility.HtmlEncode(subject)}</h1><table>{table}</table>{Button("Open dashboard", link)}");
        var text = subject + "\n" + string.Join("\n", rows.Select(r => $"{r.Label}: {r.Value}")) + $"\n{link}";
        return (subject, html, text);
    }

    private static (string Subject, string Html, string Text) Build(string subject, string greeting, string body, string action, string link)
    {
        var html = Layout(
            $"<p style=\"color:#0f172a\">{WebUtility.HtmlEncode(greeting)}</p>" +
            $"<p style=\"color:#334155;line-height:1.5\">{WebUtility.HtmlEncode(body)}</p>" +
            Button(action, link));
        return (subject, html, $"{greeting}\n\n{body}\n\n{action}: {link}");
    }

    private static string Button(string label, string link) =>
        $"<p><a href=\"{WebUtility.HtmlEncode(link)}\" style=\"display:inline-block;background:#1d4ed8;color:#ffffff;padding:10px 18px;border-radius:6px;text-decoration:none\">{WebUtility.HtmlEncode(label)}</a></p>";

    private static string Layout(string content) =>
        "<div style=\"font-family:Segoe UI,Arial,sans-serif;max-width:560px;margin:0 auto;padding:24px\">" +
        "<div style=\"font-weight:700;color:#0b1b3a;font-size:16px;margin-bottom:16px\">StockForge</div>" +
        content +
        "<p style=\"color:#94a3b8;font-size:12px;margin-top:24px\">This is an automated message from StockForge.</p></div>";
}
