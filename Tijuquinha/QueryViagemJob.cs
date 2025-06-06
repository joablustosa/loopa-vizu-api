using Dapper;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.BigQuery.V2;
using MySql.Data.MySqlClient;
using Quartz;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tijuquinha
{
    internal class QueryViagemJob : IJob
    {
        private readonly ILogger<DownloadQueryJob> _logger;
        private const string BigQueryQuery = @"
                                                  SELECT
                                                    id_viagem,
                                                    COALESCE(
                                                      SAFE.PARSE_TIMESTAMP('%Y-%m-%d %H:%M:%S.%f', CAST(datetime_partida AS STRING)),
                                                      SAFE.PARSE_TIMESTAMP('%Y-%m-%d %H:%M:%S', CAST(datetime_partida AS STRING))
                                                    ) AS datetime_partida,
                                                    COALESCE(
                                                      SAFE.PARSE_TIMESTAMP('%Y-%m-%d %H:%M:%S.%f', CAST(datetime_chegada AS STRING)),
                                                      SAFE.PARSE_TIMESTAMP('%Y-%m-%d %H:%M:%S', CAST(datetime_chegada AS STRING))
                                                    ) AS datetime_chegada,
                                                    sentido,
                                                    servico_realizado,
                                                    REGEXP_EXTRACT(id_veiculo, r'(\d+)') AS id_veiculo_normalizado
                                                  FROM `rj-smtr.projeto_subsidio_sppo.viagem_completa`
                                                  WHERE DATE(datetime_partida) >= '2025-02-01'
                                                    AND DATE(datetime_partida) < '2025-05-26'
                                                    AND servico_realizado IN (
                                                      '165', '220', '229', '301', '302', '315', '435', '448', '584', '603', '607', '608', '645',
                                                      '702', '805', '810', '865', 'SN302', 'SN810', 'SP805', 'SP810', '117', '422', 'SN422',
                                                      '583', '585', '497', 'SN497', '104', '108', '109', '110', '112', '209', 'SN104', 'SN209',
                                                      'SN309', '362', '443', '498', 'SR362', '275', '472', '474', '485', 'SN474', '415', 'SN415',
                                                      '426', '409', '416', '410', '626', '239', '432', '433', 'SN433', '238', 'SN238', '863',
                                                      'SV863', '862', '990', '309', '552', '957', '878', 'SN878', 'SV878', '361', 'SP315', '553',
                                                      '181', '2334', '2335', 'SN554', '352', '2337', '2338', '2802', '2803', '2804', '554', '338',
                                                      '343', '348', '380', '483', '954', 'SN483', 'SN954', '2345', '300', '388', '393', '394',
                                                      '397', '486', '961', 'SN388', 'SN393', 'SN397', '292', '342', 'SP343', 'SP553', '349',
                                                      '378', '384', '399', 'SN399', '539', '558', '444', '157', '557', 'SPA550', 'SP309', '105',
                                                      '538', '548', '439', 'SN538', '518', 'SP404', 'SN636', '636', '606', 'SV606', '653', '621',
                                                      '363', 'SN363', '601', '685', 'SN685', 'SVA685', 'SVB685', '353', 'SN353', '622', '652',
                                                      '651', '217', '565', 'SN565', '371', '678', '306', '917', '783', 'SP783', '2111', '779',
                                                      'SN779', 'SV779', '844', '550', '555', 'SPB550', '613', '368', 'SN368', '881', '611'
                                                    )";

        public QueryViagemJob(ILogger<DownloadQueryJob> logger)
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
                            id_viagem = row["id_viagem"].ToString() ?? string.Empty,
                            datetime_partida = Convert.ToDateTime(row["datetime_partida"] ?? DateTime.UtcNow),
                            datetime_chegada = Convert.ToDateTime(row["datetime_chegada"] ?? DateTime.UtcNow),
                            sentido = row["sentido"].ToString() ?? string.Empty,
                            servico_realizado = row["servico_realizado"].ToString() ?? string.Empty,
                            id_veiculo_normalizado = row["id_veiculo_normalizado"].ToString() ?? string.Empty,
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

            var sql = new StringBuilder("INSERT INTO viagem_completa (id_viagem, datetime_partida, datetime_chegada, sentido, servico_realizado, id_veiculo_normalizado) VALUES ");
            var parameters = new DynamicParameters();

            for (int i = 0; i < batch.Count; i++)
            {
                sql.Append($"(@id_viagem{i}, @datetime_partida{i}, @datetime_chegada{i}, @sentido{i}, @servico_realizado{i}, @id_veiculo_normalizado{i}),");

                parameters.Add($"@id_viagem{i}", batch[i].id_viagem ?? string.Empty);
                parameters.Add($"@datetime_partida{i}", batch[i].datetime_partida);
                parameters.Add($"@datetime_chegada{i}", batch[i].datetime_chegada);
                parameters.Add($"@sentido{i}", batch[i].sentido ?? string.Empty);
                parameters.Add($"@servico_realizado{i}", batch[i].servico_realizado ?? string.Empty);
                parameters.Add($"@id_veiculo_normalizado{i}", batch[i].id_veiculo_normalizado ?? string.Empty);
            }

            sql.Length--; // Remove a última vírgula

            await connection.ExecuteAsync(sql.ToString(), parameters);
        }

    }
}
