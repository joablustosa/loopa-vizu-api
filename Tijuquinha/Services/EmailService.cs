using System.Net.Mail;
using Microsoft.Extensions.Logging;

namespace Tijuquinha.Services
{
    public class EmailService
    {
        private readonly ILogger<EmailService> _logger;
        private readonly string _smtpServer;
        private readonly int _smtpPort;
        private readonly string _smtpUsername;
        private readonly string _smtpPassword;
        private readonly string _fromEmail;
        private readonly string _toEmail;

        public EmailService(
            ILogger<EmailService> logger,
            string smtpServer,
            int smtpPort,
            string smtpUsername,
            string smtpPassword,
            string fromEmail,
            string toEmail)
        {
            _logger = logger;
            _smtpServer = smtpServer;
            _smtpPort = smtpPort;
            _smtpUsername = smtpUsername;
            _smtpPassword = smtpPassword;
            _fromEmail = fromEmail;
            _toEmail = toEmail;
        }

        public async Task SendExecutionReportAsync(bool success, string output, string error)
        {
            try
            {
                using var client = new SmtpClient(_smtpServer, _smtpPort)
                {
                    EnableSsl = true,
                    Credentials = new System.Net.NetworkCredential(_smtpUsername, _smtpPassword)
                };

                // 🗓️ Gera as últimas 9 semanas no formato "dd/MM"
                List<string> semanas = new();
                DateTime hoje = DateTime.Today;
                // Encerra no domingo anterior
                int diasDesdeDomingo = (int)hoje.DayOfWeek + 1;
                DateTime fim = hoje.AddDays(-diasDesdeDomingo);
                for (int i = 8; i >= 0; i--)
                {
                    var inicio = fim.AddDays(-6 - (i * 7));
                    var termino = fim.AddDays(-i * 7);
                    semanas.Add($"{inicio:dd/MM} a {termino:dd/MM}");
                }

                string rangeSemanas = string.Join(", ", semanas);

                var subject = success
                    ? $"✅ Relatório - Últimas 9 semanas ({rangeSemanas})"
                    : $"❌ Erro no Relatório - Últimas 9 semanas ({rangeSemanas})";

                var body = $@"
            <h2>Relatório de Carregamento e Concorrentes</h2>
            <p><strong>Status:</strong> {(success ? "Sucesso" : "Falha")}</p>
            <p><strong>Intervalo:</strong> {rangeSemanas}</p>
            
            {(string.IsNullOrEmpty(output) ? "" : $@"
            <h3>Mensagem:</h3>
            <pre>Aqui estão dados de carregamento e de concorrentes em linhas compartilhadas.</pre>
            ")}
        ";

                var message = new MailMessage(_fromEmail, _toEmail, subject, body)
                {
                    IsBodyHtml = true
                };

                // 📎 Anexar os PDFs da pasta download/outputs
                var outputDir = Path.Combine(Directory.GetCurrentDirectory(), "download", "output");
                if (Directory.Exists(outputDir))
                {
                    var pdfs = Directory.GetFiles(outputDir, "*.pdf")
                                        .OrderByDescending(File.GetLastWriteTime)
                                        .Take(8)
                                        .ToList();

                    foreach (var pdf in pdfs)
                    {
                        message.Attachments.Add(new Attachment(pdf));
                    }
                }
                else
                {
                    _logger.LogWarning("Diretório de saída não encontrado: " + outputDir);
                }

                await client.SendMailAsync(message);
                _logger.LogInformation("Email de relatório enviado com sucesso com {0} anexos", message.Attachments.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao enviar email de relatório");
                throw;
            }
        }
    }
} 