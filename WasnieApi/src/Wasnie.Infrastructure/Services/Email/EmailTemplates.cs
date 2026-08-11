namespace Wasnie.Infrastructure.Services.Email;

internal static class EmailTemplates
{
    public static (string Subject, string Html) Confirmation(string firstName, string confirmUrl, string language) =>
        language switch
        {
            "es" => ConfirmationEs(firstName, confirmUrl),
            "pl" => ConfirmationPl(firstName, confirmUrl),
            _ => ConfirmationEn(firstName, confirmUrl),
        };

    public static (string Subject, string Html) PasswordReset(string firstName, string resetUrl, string language) =>
        language switch
        {
            "es" => PasswordResetEs(firstName, resetUrl),
            "pl" => PasswordResetPl(firstName, resetUrl),
            _ => PasswordResetEn(firstName, resetUrl),
        };

    private static (string, string) ConfirmationEn(string firstName, string url) => (
        "Confirm your Incentra account",
        Layout($"Hi {Escape(firstName)},",
            "You're almost there. Please confirm your email address to activate your Incentra workspace.",
            "Confirm email address", url,
            "This link is valid for 48 hours. If you didn't create an Incentra account, you can safely ignore this email."));

    private static (string, string) ConfirmationEs(string firstName, string url) => (
        "Confirma tu cuenta de Incentra",
        Layout($"Hola {Escape(firstName)},",
            "Ya casi estás. Por favor confirma tu dirección de correo electrónico para activar tu espacio de trabajo en Incentra.",
            "Confirmar correo electrónico", url,
            "Este enlace es válido durante 48 horas. Si no creaste una cuenta en Incentra, puedes ignorar este mensaje."));

    private static (string, string) ConfirmationPl(string firstName, string url) => (
        "Potwierdź swoje konto w Incentrze",
        Layout($"Cześć {Escape(firstName)},",
            "Prawie gotowe. Potwierdź swój adres e-mail, aby aktywować swoją przestrzeń roboczą w Incentrze.",
            "Potwierdź adres e-mail", url,
            "Link jest ważny przez 48 godzin. Jeśli nie zakładałeś konta w Incentrze, możesz zignorować tę wiadomość."));

    private static (string, string) PasswordResetEn(string firstName, string url) => (
        "Reset your Incentra password",
        Layout($"Hi {Escape(firstName)},",
            "We received a request to reset the password for your Incentra account. Click the button below to choose a new password.",
            "Reset password", url,
            "This link expires in 1 hour. If you didn't request a password reset, you can safely ignore this email."));

    private static (string, string) PasswordResetEs(string firstName, string url) => (
        "Restablece tu contraseña de Incentra",
        Layout($"Hola {Escape(firstName)},",
            "Recibimos una solicitud para restablecer la contraseña de tu cuenta de Incentra. Haz clic en el botón para elegir una nueva contraseña.",
            "Restablecer contraseña", url,
            "Este enlace expira en 1 hora. Si no solicitaste un restablecimiento de contraseña, ignora este mensaje."));

    private static (string, string) PasswordResetPl(string firstName, string url) => (
        "Zresetuj hasło do Incentry",
        Layout($"Cześć {Escape(firstName)},",
            "Otrzymaliśmy prośbę o zresetowanie hasła do Twojego konta w Incentrze. Kliknij przycisk, aby wybrać nowe hasło.",
            "Zresetuj hasło", url,
            "Link wygasa po 1 godzinie. Jeśli nie prosiłeś o reset hasła, możesz zignorować tę wiadomość."));

    private static string Layout(string greeting, string body, string ctaLabel, string ctaUrl, string disclaimer) => $"""
        <!DOCTYPE html>
        <html lang="en">
        <head>
          <meta charset="UTF-8" />
          <meta name="viewport" content="width=device-width,initial-scale=1" />
          <title>Incentra</title>
        </head>
        <body style="margin:0;padding:0;background:#f4f5f7;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,sans-serif;">
          <table width="100%" cellpadding="0" cellspacing="0" style="background:#f4f5f7;padding:40px 0;">
            <tr><td align="center">
              <table width="560" cellpadding="0" cellspacing="0" style="background:#ffffff;border-radius:8px;border:1px solid #e2e8f0;overflow:hidden;max-width:560px;">
                <!-- Header -->
                <tr>
                  <td style="background:#1a1a2e;padding:24px 32px;">
                    <span style="color:#ffffff;font-size:20px;font-weight:700;letter-spacing:-0.5px;">Incentra</span>
                  </td>
                </tr>
                <!-- Body -->
                <tr>
                  <td style="padding:40px 32px 32px;">
                    <p style="margin:0 0 16px;font-size:18px;font-weight:600;color:#1a1a2e;">{greeting}</p>
                    <p style="margin:0 0 32px;font-size:15px;line-height:1.6;color:#4a5568;">{body}</p>
                    <table cellpadding="0" cellspacing="0">
                      <tr>
                        <td style="background:#5b6cf8;border-radius:6px;">
                          <a href="{ctaUrl}" style="display:inline-block;padding:14px 28px;color:#ffffff;font-size:15px;font-weight:600;text-decoration:none;">{ctaLabel}</a>
                        </td>
                      </tr>
                    </table>
                    <p style="margin:32px 0 0;font-size:12px;color:#a0aec0;line-height:1.5;">{disclaimer}</p>
                  </td>
                </tr>
                <!-- Footer -->
                <tr>
                  <td style="background:#f8fafc;border-top:1px solid #e2e8f0;padding:20px 32px;">
                    <p style="margin:0;font-size:12px;color:#a0aec0;">© 2025 Incentra. All rights reserved. · <a href="https://incentra.work" style="color:#5b6cf8;text-decoration:none;">incentra.work</a></p>
                  </td>
                </tr>
              </table>
            </td></tr>
          </table>
        </body>
        </html>
        """;

    public static (string Subject, string Html) EmailChangeConfirmation(string firstName, string confirmUrl, string language) =>
        language switch
        {
            "es" => EmailChangeConfirmationEs(firstName, confirmUrl),
            "pl" => EmailChangeConfirmationPl(firstName, confirmUrl),
            _ => EmailChangeConfirmationEn(firstName, confirmUrl),
        };

    private static (string, string) EmailChangeConfirmationEn(string firstName, string url) => (
        "Confirm your new Incentra email address",
        Layout($"Hi {Escape(firstName)},",
            "We received a request to change the email address associated with your Incentra account. Click the button below to confirm your new email address.",
            "Confirm new email address", url,
            "This link is valid for 24 hours. If you didn't request this change, please ignore this email — your current email remains unchanged."));

    private static (string, string) EmailChangeConfirmationEs(string firstName, string url) => (
        "Confirma tu nueva dirección de correo electrónico en Incentra",
        Layout($"Hola {Escape(firstName)},",
            "Recibimos una solicitud para cambiar el correo electrónico asociado a tu cuenta de Incentra. Haz clic en el botón para confirmar tu nueva dirección de correo.",
            "Confirmar nuevo correo electrónico", url,
            "Este enlace es válido durante 24 horas. Si no solicitaste este cambio, ignora este mensaje — tu correo actual sigue sin cambios."));

    private static (string, string) EmailChangeConfirmationPl(string firstName, string url) => (
        "Potwierdź nowy adres e-mail w Incentrze",
        Layout($"Cześć {Escape(firstName)},",
            "Otrzymaliśmy prośbę o zmianę adresu e-mail przypisanego do Twojego konta w Incentrze. Kliknij przycisk, aby potwierdzić nowy adres e-mail.",
            "Potwierdź nowy adres e-mail", url,
            "Link jest ważny przez 24 godziny. Jeśli nie prosiłeś o tę zmianę, zignoruj tę wiadomość — Twój obecny adres e-mail pozostaje bez zmian."));

    private static string Escape(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
