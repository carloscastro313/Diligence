using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Diligencia.BuildingBlocks.Email;

public interface IEmailTransport
{
    // Lanza si el servidor no acepta el mensaje. Nunca registra el cuerpo.
    Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken);
}

public static class EmailSetup
{
    public static IServiceCollection AddEmailTransport(this IServiceCollection services) =>
        services.AddScoped<IEmailTransport, SmtpEmailTransport>();
}

internal sealed class SmtpEmailTransport(IConfiguration configuration) : IEmailTransport
{
    public async Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken)
    {
        var section = configuration.GetSection("Email:Smtp");
        using var client = new SmtpClient(section["Host"] ?? "localhost", section.GetValue("Port", 25));
        using var message = new MailMessage(section["From"] ?? "no-reply@diligencia.local", to, subject, body);
        await client.SendMailAsync(message, cancellationToken);
    }
}
