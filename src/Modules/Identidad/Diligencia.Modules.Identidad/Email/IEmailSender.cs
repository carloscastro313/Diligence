namespace Diligencia.Modules.Identidad.Email;

// Interno hasta que otro módulo lo necesite (R7). Las implementaciones no lanzan: un fallo de envío no debe
// revertir el alta ni revelar si un correo existe.
internal interface IEmailSender
{
    Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken);
}

internal static class EmailTemplates
{
    public static (string Subject, string Body) Invitation(string name, string link, int hours) => (
        "Activa tu cuenta de Diligencia",
        $"Hola {name},\n\nSe creó una cuenta para ti en Diligencia. Establece tu contraseña en este enlace (válido {hours} horas, un solo uso):\n{link}\n");

    public static (string Subject, string Body) Reset(string name, string link, int minutes) => (
        "Restablece tu contraseña de Diligencia",
        $"Hola {name},\n\nUsa este enlace para establecer una nueva contraseña (válido {minutes} minutos, un solo uso):\n{link}\n\nSi no lo pediste, ignora este correo.\n");
}
