using System.Net;
using System.Text.Encodings.Web;
using CriatorioVirtual.Application.Identity;

namespace CriatorioVirtual.Infrastructure.Identity;

public sealed record AuthenticationEmailContent(
    string Subject,
    string HtmlBody,
    string TextBody);

public static class AuthenticationEmailTemplateRenderer
{
    private const string BrandName = "Criatório Virtual";
    private const string BrandGreen = "#087f5b";
    private const string DarkText = "#23313b";
    private const string MutedText = "#718096";

    public static AuthenticationEmailContent Render(AuthenticationEmailMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var actionUrl = message.ActionUrl.ToString();
        var htmlActionUrl = HtmlEncoder.Default.Encode(actionUrl);
        var content = message.Kind switch
        {
            AuthenticationEmailKind.Confirmation => new EmailCopy(
                "Confirme seu e-mail",
                "Clique no botão abaixo para confirmar seu e-mail e ativar sua conta no Criatório Virtual.",
                "Confirmar meu e-mail"),
            AuthenticationEmailKind.PasswordReset => new EmailCopy(
                "Redefina sua senha",
                "Clique no botão abaixo para redefinir sua senha do Criatório Virtual.",
                "Redefinir minha senha"),
            _ => throw new ArgumentOutOfRangeException(nameof(message), message.Kind, "Unsupported authentication email kind.")
        };

        var htmlBody = $"""
            <!doctype html>
            <html lang="pt-BR">
              <head>
                <meta charset="utf-8">
                <meta name="x-apple-disable-message-reformatting">
                <meta name="viewport" content="width=device-width, initial-scale=1.0">
                <title>{WebUtility.HtmlEncode(content.Heading)} — {BrandName}</title>
              </head>
              <body style="margin:0;padding:0;background-color:#eef5f2;font-family:Arial,Helvetica,sans-serif;color:{DarkText};">
                <div style="display:none;max-height:0;overflow:hidden;opacity:0;">
                  {WebUtility.HtmlEncode(content.Description)}
                </div>
                <table role="presentation" width="100%" cellspacing="0" cellpadding="0" border="0" style="width:100%;background-color:#eef5f2;">
                  <tr>
                    <td align="center" style="padding:34px 12px;">
                      <table role="presentation" class="email-shell" width="600" cellspacing="0" cellpadding="0" border="0" style="width:100%;max-width:600px;">
                        <tr>
                          <td class="email-card" style="background-color:#ffffff;border:1px solid #dce9e3;border-radius:12px;box-shadow:0 4px 16px rgba(27,67,54,0.08);overflow:hidden;">
                            <table role="presentation" width="100%" cellspacing="0" cellpadding="0" border="0">
                              <tr>
                                <td align="center" style="padding:30px 28px 20px;">
                                  <table role="presentation" cellspacing="0" cellpadding="0" border="0">
                                    <tr>
                                      <td valign="middle" style="padding-right:9px;">
                                        <span style="display:inline-block;width:24px;height:29px;background-color:#18a878;border-radius:100% 0 100% 0;transform:rotate(36deg);vertical-align:middle;"></span>
                                      </td>
                                      <td valign="middle" style="font-size:20px;line-height:29px;font-weight:700;letter-spacing:-0.3px;color:{DarkText};">
                                        {BrandName}
                                      </td>
                                    </tr>
                                  </table>
                                </td>
                              </tr>
                              <tr>
                                <td class="email-content" style="padding:8px 42px 38px;">
                                  <table role="presentation" width="100%" cellspacing="0" cellpadding="0" border="0" style="background-color:#fbfcfc;border-radius:8px;">
                                    <tr>
                                      <td style="padding:30px 24px 28px;">
                                        <h1 style="margin:0 0 20px;font-size:24px;line-height:32px;text-align:center;color:{DarkText};">{WebUtility.HtmlEncode(content.Heading)}</h1>
                                        <p style="margin:0 0 12px;font-size:15px;line-height:23px;color:{MutedText};">Olá!</p>
                                        <p style="margin:0 0 24px;font-size:15px;line-height:23px;color:{MutedText};">{WebUtility.HtmlEncode(content.Description)}</p>
                                        <table role="presentation" width="100%" cellspacing="0" cellpadding="0" border="0">
                                          <tr>
                                            <td align="center" style="padding:0 0 25px;">
                                              <a href="{htmlActionUrl}" style="display:block;padding:14px 18px;background-color:{BrandGreen};border-radius:5px;color:#ffffff;font-size:15px;line-height:20px;font-weight:700;text-align:center;text-decoration:none;">{WebUtility.HtmlEncode(content.ButtonText)}</a>
                                            </td>
                                          </tr>
                                        </table>
                                        <p style="margin:0 0 8px;font-size:13px;line-height:20px;color:{MutedText};">Se o botão não funcionar, copie e cole o link abaixo no seu navegador:</p>
                                        <p style="margin:0 0 24px;font-size:12px;line-height:18px;word-break:break-all;color:{BrandGreen};">{htmlActionUrl}</p>
                                        <p style="margin:0;font-size:13px;line-height:20px;color:{MutedText};">Atenciosamente,<br><strong style="color:{DarkText};">Equipe {BrandName}</strong></p>
                                      </td>
                                    </tr>
                                  </table>
                                </td>
                              </tr>
                            </table>
                          </td>
                        </tr>
                      </table>
                    </td>
                  </tr>
                </table>
              </body>
            </html>
            """;

        var textBody = $"""
            {content.Heading} — {BrandName}

            Olá!

            {content.Description}

            {content.ButtonText}: {actionUrl}

            Se o botão não funcionar, copie e cole o link acima no seu navegador.

            Atenciosamente,
            Equipe {BrandName}
            """;

        var subject = message.Kind switch
        {
            AuthenticationEmailKind.Confirmation => $"{content.Heading} — {BrandName}",
            AuthenticationEmailKind.PasswordReset => $"{content.Heading} — {BrandName}",
            _ => throw new ArgumentOutOfRangeException(nameof(message), message.Kind, "Unsupported authentication email kind.")
        };

        return new AuthenticationEmailContent(subject, htmlBody, textBody);
    }

    private sealed record EmailCopy(string Heading, string Description, string ButtonText);
}
