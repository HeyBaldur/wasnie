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

    /// <param name="forgotPasswordUrl">
    /// The ordinary "forgot password" screen — no token, no one-off link. A security warning
    /// that carries a freshly minted action link is the exact shape a phishing message imitates,
    /// so this one points at a page the recipient can also reach by typing the address themselves.
    /// </param>
    public static (string Subject, string Html) AccountLocked(string firstName, string forgotPasswordUrl, int minutes, string language) =>
        language switch
        {
            "es" => AccountLockedEs(firstName, forgotPasswordUrl, minutes),
            "pl" => AccountLockedPl(firstName, forgotPasswordUrl, minutes),
            _ => AccountLockedEn(firstName, forgotPasswordUrl, minutes),
        };

    private static (string, string) AccountLockedEn(string firstName, string url, int minutes) => (
        "Unusual sign-in activity on your Incentra account",
        Layout($"Hi {Escape(firstName)},",
            $"We blocked access to your Incentra account after several failed sign-in attempts. It will unlock automatically in about {minutes} minutes. "
            + "If those attempts were not yours, someone may know or be guessing your email address — we recommend changing your password.",
            "Change your password", url,
            "If it was you and you simply mistyped your password, no action is needed. We will not send another notice for this block."));

    private static (string, string) AccountLockedEs(string firstName, string url, int minutes) => (
        "Actividad de acceso inusual en su cuenta de Incentra",
        Layout($"Hola {Escape(firstName)},",
            $"Se ha bloqueado el acceso a su cuenta de Incentra tras varios intentos fallidos de inicio de sesión. Se desbloqueará automáticamente en unos {minutes} minutos. "
            + "Si esos intentos no fueron suyos, es posible que alguien conozca o esté probando su dirección de correo; recomendamos cambiar la contraseña.",
            "Cambiar la contraseña", url,
            "Si fue usted y simplemente se equivocó al escribir la contraseña, no hace falta hacer nada. No se enviará otro aviso por este bloqueo."));

    private static (string, string) AccountLockedPl(string firstName, string url, int minutes) => (
        "Nietypowa aktywność logowania na koncie Incentra",
        Layout($"Cześć {Escape(firstName)},",
            $"Zablokowaliśmy dostęp do Twojego konta Incentra po kilku nieudanych próbach logowania. Odblokuje się automatycznie za około {minutes} minut. "
            + "Jeśli to nie Ty podejmowałeś te próby, ktoś może znać lub zgadywać Twój adres e-mail — zalecamy zmianę hasła.",
            "Zmień hasło", url,
            "Jeśli to Ty i po prostu pomyliłeś hasło, nie musisz nic robić. Nie wyślemy kolejnego powiadomienia o tej blokadzie."));

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

    /// <summary>
    /// The invitation to join a tenant (KAN-32).
    ///
    /// ★ IT NAMES THE INVITER AND THE COMPANY. The recipient may have no idea what Incentra is, and an
    /// unexplained "set your password" link from an unknown product is indistinguishable from phishing.
    /// Saying who asked for them by name is what makes it legible.
    ///
    /// ★ IT DOES NOT NAME THE ROLE. What "CompManager" means is a matter for the person's first day,
    /// not for an email they read before they have an account, and a role name out of context reads as
    /// jargon rather than as information.
    /// </summary>
    public static (string Subject, string Html) Invitation(
        string inviterName, string companyName, string acceptUrl, int expiryDays, string language) =>
        language switch
        {
            "es" => InvitationEs(inviterName, companyName, acceptUrl, expiryDays),
            "pl" => InvitationPl(inviterName, companyName, acceptUrl, expiryDays),
            _ => InvitationEn(inviterName, companyName, acceptUrl, expiryDays),
        };

    private static (string, string) InvitationEn(string inviter, string company, string url, int days) => (
        $"{Escape(inviter)} invited you to {Escape(company)} on Incentra",
        Layout("Hello,",
            $"{Escape(inviter)} has invited you to join <strong>{Escape(company)}</strong> on Incentra, "
            + "where the company manages its sales commissions. Use the button below to set your password and sign in.",
            "Accept the invitation", url,
            $"This invitation expires in {days} days. If you were not expecting it, you can ignore this email — no account is created until you accept."));

    private static (string, string) InvitationEs(string inviter, string company, string url, int days) => (
        $"{Escape(inviter)} le ha invitado a {Escape(company)} en Incentra",
        Layout("Hola:",
            $"{Escape(inviter)} le ha invitado a unirse a <strong>{Escape(company)}</strong> en Incentra, "
            + "la plataforma donde la empresa gestiona sus comisiones de ventas. Use el botón para establecer su contraseña y acceder.",
            "Aceptar la invitación", url,
            $"Esta invitación caduca en {days} días. Si no la esperaba, puede ignorar este mensaje: no se crea ninguna cuenta hasta que la acepte."));

    private static (string, string) InvitationPl(string inviter, string company, string url, int days) => (
        $"{Escape(inviter)} zaprasza Cię do {Escape(company)} w Incentrze",
        Layout("Witaj,",
            $"{Escape(inviter)} zaprasza Cię do dołączenia do firmy <strong>{Escape(company)}</strong> w Incentrze, "
            + "gdzie firma zarządza prowizjami sprzedażowymi. Użyj przycisku poniżej, aby ustawić hasło i się zalogować.",
            "Przyjmij zaproszenie", url,
            $"To zaproszenie wygasa za {days} dni. Jeśli się go nie spodziewałeś, zignoruj tę wiadomość — konto powstaje dopiero po jego przyjęciu."));

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

    /// <summary>
    /// The Organization identifier(s) an administrator asked to be reminded of (KAN-93).
    /// </summary>
    /// <param name="loginUrl">
    /// The ordinary sign-in page — no token, no one-off link. The identifier in the body IS the answer
    /// and nothing here needs to be clicked; the button only saves the reader finding the tab again.
    /// The same reasoning as <see cref="AccountLocked"/>: a security email carrying a freshly minted
    /// action link is the exact shape phishing imitates.
    /// </param>
    public static (string Subject, string Html) OrganizationIdentifier(
        string firstName,
        IReadOnlyList<(string Name, string Slug)> organizations,
        string loginUrl,
        string language) =>
        language switch
        {
            "es" => OrganizationIdentifierEs(firstName, organizations, loginUrl),
            "pl" => OrganizationIdentifierPl(firstName, organizations, loginUrl),
            _ => OrganizationIdentifierEn(firstName, organizations, loginUrl),
        };

    /// <summary>
    /// The workspaces as lines inside the body paragraph.
    ///
    /// ★ INLINE MARKUP, NOT A NESTED TABLE. <c>Layout</c> drops the body inside a &lt;p&gt;, and a
    /// table inside a paragraph is invalid HTML that mail clients render at their own discretion.
    /// Breaks and a monospace span survive everywhere.
    ///
    /// ★ BOTH VALUES ARE ESCAPED. A workspace name is whatever its founder typed at registration —
    /// untrusted text on its way into an HTML document.
    /// </summary>
    private static string OrganizationLines(IReadOnlyList<(string Name, string Slug)> organizations) =>
        string.Join("<br /><br />", organizations.Select(o =>
            $"""<strong style="color:#1a1a2e;">{Escape(o.Name)}</strong><br /><span style="display:inline-block;margin-top:4px;padding:6px 10px;background:#f4f5f7;border:1px solid #e2e8f0;border-radius:4px;font-family:monospace;font-size:15px;color:#1a1a2e;">{Escape(o.Slug)}</span>"""));

    private static (string, string) OrganizationIdentifierEn(
        string firstName, IReadOnlyList<(string Name, string Slug)> organizations, string url) => (
        organizations.Count == 1
            ? "Your Incentra Organization identifier"
            : "Your Incentra Organization identifiers",
        Layout($"Hi {Escape(firstName)},",
            (organizations.Count == 1
                ? "You asked us to remind you of the Organization identifier for the workspace you administer. Type it into the Organization identifier field when you sign in:<br /><br />"
                : "You asked us to remind you of your Organization identifiers. You administer more than one workspace, so here is each of them — type the right one into the Organization identifier field when you sign in:<br /><br />")
            + OrganizationLines(organizations),
            "Go to sign in", url,
            "If you did not ask for this, you can ignore this message — nothing about your account has changed. Only administrators can request this reminder."));

    private static (string, string) OrganizationIdentifierEs(
        string firstName, IReadOnlyList<(string Name, string Slug)> organizations, string url) => (
        organizations.Count == 1
            ? "Su Organization identifier de Incentra"
            : "Sus Organization identifiers de Incentra",
        Layout($"Hola {Escape(firstName)},",
            (organizations.Count == 1
                ? "Ha solicitado que le recordemos el Organization identifier del espacio de trabajo que administra. Escríbalo en el campo «Organization identifier» al iniciar sesión:<br /><br />"
                : "Ha solicitado que le recordemos sus Organization identifiers. Administra más de un espacio de trabajo, así que aquí está cada uno — escriba el que corresponda en el campo «Organization identifier» al iniciar sesión:<br /><br />")
            + OrganizationLines(organizations),
            "Ir al inicio de sesión", url,
            "Si no ha solicitado esto, puede ignorar este mensaje: no ha cambiado nada en su cuenta. Solo los administradores pueden pedir este recordatorio."));

    private static (string, string) OrganizationIdentifierPl(
        string firstName, IReadOnlyList<(string Name, string Slug)> organizations, string url) => (
        organizations.Count == 1
            ? "Twój identyfikator organizacji w Incentrze"
            : "Twoje identyfikatory organizacji w Incentrze",
        Layout($"Cześć {Escape(firstName)},",
            (organizations.Count == 1
                ? "Poprosiłeś o przypomnienie identyfikatora organizacji (Organization identifier) obszaru roboczego, którym administrujesz. Wpisz go w pole „Organization identifier” podczas logowania:<br /><br />"
                : "Poprosiłeś o przypomnienie swoich identyfikatorów organizacji. Administrujesz więcej niż jednym obszarem roboczym, więc poniżej jest każdy z nich — wpisz właściwy w pole „Organization identifier” podczas logowania:<br /><br />")
            + OrganizationLines(organizations),
            "Przejdź do logowania", url,
            "Jeśli nie prosiłeś o tę wiadomość, możesz ją zignorować — nic w Twoim koncie się nie zmieniło. Tylko administratorzy mogą poprosić o to przypomnienie."));

    private static string Escape(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
