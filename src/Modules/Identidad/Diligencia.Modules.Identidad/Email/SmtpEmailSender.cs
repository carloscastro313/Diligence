using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Diligencia.Modules.Identidad.Email;

internal sealed class SmtpEmailSender(IConfiguration configuration, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken)
    {
        try
        {
            var section = configuration.GetSection("Email:Smtp");
            using var client = new SmtpClient(section["Host"] ?? "localhost", section.GetValue("Port", 25));
            using var message = new MailMessage(section["From"] ?? "no-reply@diligencia.local", to, subject, body);
            await client.SendMailAsync(message, cancellationToken);
        }
        catch (Exception ex)
        {
            // El cuerpo lleva el token: nunca se registra.
            logger.LogError(ex, "No se pudo enviar el correo '{Subject}'", subject);
        }
    }
}
