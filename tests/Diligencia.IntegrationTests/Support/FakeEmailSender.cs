using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Diligencia.Modules.Identidad.Email;

namespace Diligencia.IntegrationTests.Support;

public sealed record SentEmail(string To, string Subject, string Body);

// Guarda los correos en memoria para que los tests lean los enlaces de invitación y recuperación.
public sealed partial class FakeEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<SentEmail> _sent = new();

    public IReadOnlyList<SentEmail> Sent => [.. _sent];

    public Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken)
    {
        _sent.Enqueue(new SentEmail(to, subject, body));
        return Task.CompletedTask;
    }

    public IReadOnlyList<SentEmail> To(string email) => [.. _sent.Where(e => e.To == email)];

    /// <summary>Token del último correo enviado a <paramref name="email"/>.</summary>
    public string LastTokenFor(string email) =>
        TokenPattern().Match(To(email).Last().Body) is { Success: true } m
            ? m.Groups[1].Value
            : throw new InvalidOperationException("El correo no trae un enlace con token.");

    [GeneratedRegex(@"restablecer\?token=([A-Za-z0-9_-]+)")]
    private static partial Regex TokenPattern();
}
