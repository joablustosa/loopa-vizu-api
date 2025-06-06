using System.Net.Http;
using Quartz;
using System.IO;
using System.Threading.Tasks;
using Google.Cloud.BigQuery.V2;
using System.Data;
using ClosedXML.Excel;
using System.Diagnostics;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.BigQuery.V2;
using Tijuquinha.Services;
using System.Text;
using MySql.Data.MySqlClient;
using Dapper;
using Microsoft.Extensions.Logging;

namespace Tijuquinha
{
    public class DownloadQueryJob : IJob
    {
        private readonly ILogger<DownloadQueryJob> _logger;
        private const string BigQueryQuery = @"
        SELECT
            id_transacao,
            TIMESTAMP(datetime_transacao) AS datetime_transacao,
            IFNULL(CAST(id_veiculo AS STRING), '') AS id_veiculo
        FROM `rj-smtr.br_rj_riodejaneiro_bilhetagem.transacao_riocard`
        WHERE DATE(datetime_transacao) >= '2025-05-01'
          AND DATE(datetime_transacao) < '2025-05-27'";

        public DownloadQueryJob(ILogger<DownloadQueryJob> logger)
        {
            _logger = logger;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            try
            {

                var credentialsJson = @"{
                    ""type"": ""service_account"",
                    ""project_id"": ""fretamento"",
                    ""private_key_id"": ""9d85bbeec3da3c5b616db0d2700fb3c9165042e7"",
                    ""private_key"": ""-----BEGIN PRIVATE KEY-----\nMIIEvQIBADANBgkqhkiG9w0BAQEFAASCBKcwggSjAgEAAoIBAQCl81h2X+L/Xvze\nFP7rFnOZ1psFgYkF3wMauDxGFxeww8qEaMViE42/2pbAuaJs0n7rxCwFYzEaVac6\nR85Of9xMeCC55+1v9ZIYx/wUGLmPa7gpM5goMAspWsLkm4zxpdzgmsaXGeKdJ9Ru\n0gVdxdv/Tl4MdVQK4mnRVLKEsC7h7C4A2dcgf/9GFfdm2kxW/qqdliBMEe0WgxE5\n1SLJ5gyA4Fa5NepqHxNIcGPhRGQ/71Pc7uLZaywbFItXjK+5a3htOxkI70puZfRg\njLD1t+3Br/cTFpqlfvp8zhOA8XVKKtK0b7OKD/5CDWBVx76T3+GcD3NZY4twTG5c\n68NQKArhAgMBAAECggEANELphgX2d9PTMKFOEnz0lOsH0PkVTNsJoD2LBcu58zoF\nqiNQne1og5X0SedsJnc370nNTzkIurFkw67fYstTdmWScNmAZfThOJqqYS3UKj2T\nNq5/6ZAPw8tIezQdc/B3GejER8uoGTP0652KgRiRitqENcoxWGgrSPgybCUL7quv\nkU0VsYoBGbvI383q0XtH0yaDZc8znreRolx4bh8l+fEVWb3UqvBDgMYNzOAk0GJ/\nScd9A1Q3NvLgNqcnjqZbfaYkhDx7b64KKnoYt0TWKwcZDxrzJSOQ0LWBfyf2k9y0\ngJFUTcxx4O2TiYcCK26cxHk74ds0YPFrsrWTpjB3bQKBgQDQCkxpK31Vh/dhs//a\npdaw3l+E+3PP7n7WirG9OFhlVHM08plozOEbIpyJetHgE8YkupANFrGezPKZlgKC\nwN46jUsCvsZE2kU7LB/eOvj/U4KgnbOmlRLqv7pRc/VqdpoX9spgq1nt29QdvDoA\nOe44USrNwsL6ZF2GaEvHCA5c+wKBgQDMNRV2AwFqLUuXGvj5a7gQyaQAI/58Pn1c\nA4T0VxmT9YbahMuJO6NgY1SiNliyFZ6W1pL+wdQzZrDYa80BE54fE4eFlVn7tzZF\nRfxIMCCqyEMLCqC1Uztm/saOCKZiuKIm8oMwuJKq+2W4vmkk3MsYgMmHJgmpBWlP\nbPZV6ee40wKBgAFiUCfS9j5/bRHlVKpruAXtNM15rsePWqCqw4vyuAPUj/+mLYcY\n9dZsYIY5nvPSrdrIsvSjVgMsceC7ssCT7+aL0hfulPsYSKWgIYYk9kscjx3qbquJ\nClstc1vfXZ6bs2K9bZM/EJYYhEy+V9RwjjkpsRM1XH619DlUsExerVnJAoGAKJmt\nSKdUUq3qx4I/WifGkt/kUXrWkBFEj1TLzGC83yQDydJ5PTG0S+ez3gR8IfwWadsD\nos8ax5V1N7JHMh2aZIdXfIGzQE6u5ZsCi7+13v6uBbX5OdPwjYu+ImMp4Zrf8mpp\nFvi7gG83TEHfWcrkPlzstIglh4th4r7BQ1ecEK0CgYEAyruiajRiP58TJDw0nkPq\n+2s40G9hTGvW+rUn4oFyEsp+P0rQspUDoFiA/6pgXWxD6OU3A9M43mfxCWje1E1t\nnBFHkRsezpTDtP4QwTsjN3VG0d7+UO221PHMgYuVWeLvYBl2DxcgQVpRjMxKOQe4\nwJCjeJ8kUZ9GGLmHjU13vGc=\n-----END PRIVATE KEY-----\n"",
                    ""client_email"": ""fretamento-teste@fretamento.iam.gserviceaccount.com"",
                    ""client_id"": ""101714716194222710044"",
                    ""auth_uri"": ""https://accounts.google.com/o/oauth2/auth"",
                    ""token_uri"": ""https://oauth2.googleapis.com/token"",
                    ""auth_provider_x509_cert_url"": ""https://www.googleapis.com/oauth2/v1/certs"",
                    ""client_x509_cert_url"": ""https://www.googleapis.com/robot/v1/metadata/x509/fretamento-teste%40fretamento.iam.gserviceaccount.com"",
                    ""universe_domain"": ""googleapis.com""
                }";

                var credentialsPath = Path.Combine("download", "credentials.json");
                await File.WriteAllTextAsync(credentialsPath, credentialsJson);

                try
                {
                    var credential = GoogleCredential.FromJson(credentialsJson);
                    var bigQueryClient = await BigQueryClient.CreateAsync("fretamento", credential);

                    _logger.LogInformation("Iniciando consulta ao BigQuery...");
                    var results = await bigQueryClient.ExecuteQueryAsync(BigQueryQuery, null);
                    if (results == null)
                    {
                        _logger.LogWarning("Nenhum resultado retornado da consulta.");
                        return;
                    }
                    var connectionString = "server=encontrafesta.mysql.database.azure.com;user=encontra_festa;password=$$0bg1mt2fs3$$;database=loopa_db";
                    using var connection = new MySqlConnection(connectionString);
                    await connection.OpenAsync();

                    const int batchSize = 8000;
                    var batch = new List<dynamic>();
                    _logger.LogInformation("Iniciando gravação no mysql...");

                    foreach (var row in results)
                    {
                        batch.Add(new
                        {
                            id_transacao = row["id_transacao"].ToString() ?? string.Empty,
                            datetime_transacao = Convert.ToDateTime(row["datetime_transacao"] ?? DateTime.UtcNow),
                            id_veiculo = row["id_veiculo"].ToString() ?? string.Empty
                        });

                        if (batch.Count >= batchSize)
                        {
                            await InsertBatchAsync(connection, batch);
                            batch.Clear();
                        }
                    }

                    if (batch.Count > 0)
                    {
                        await InsertBatchAsync(connection, batch);
                    }

                    _logger.LogInformation("Dados gravados com sucesso no MySQL.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Erro ao processar BigQuery");
                    throw;
                }
                finally
                {
                    if (File.Exists(credentialsPath))
                    {
                        File.Delete(credentialsPath);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao executar o job");
                throw;
            }
        }

        private static async Task InsertBatchAsync(MySqlConnection connection, List<dynamic> batch)
        {
            if (batch == null || batch.Count == 0)
            {
                return; // Evita inserir lote vazio ou nulo
            }

            var sql = new StringBuilder("INSERT INTO transacao_riocard2 (id_transacao, datetime_transacao, id_veiculo) VALUES ");
            var parameters = new DynamicParameters();

            for (int i = 0; i < batch.Count; i++)
            {
                sql.Append($"(@id_transacao{i}, @datetime_transacao{i}, @id_veiculo{i}),");

                parameters.Add($"@id_transacao{i}", batch[i].id_transacao ?? string.Empty);
                parameters.Add($"@datetime_transacao{i}", batch[i].datetime_transacao);
                parameters.Add($"@id_veiculo{i}", batch[i].id_veiculo ?? string.Empty);
            }

            sql.Length--; // Remove vírgula final

            await connection.ExecuteAsync(sql.ToString(), parameters);
        }

    }
} 