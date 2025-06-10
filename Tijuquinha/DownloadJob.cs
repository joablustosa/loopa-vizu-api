using Quartz;
using Google.Cloud.BigQuery.V2;
using ClosedXML.Excel;
using System.Diagnostics;
using Google.Apis.Auth.OAuth2;
using Tijuquinha.Services;
using System.Text;

namespace Tijuquinha
{
    public class DownloadJob : IJob
    {
        private readonly ILogger<DownloadJob> _logger;
        private readonly EmailService _emailService;
        private const string DownloadUrl = "https://www.arcgis.com/sharing/rest/content/items/8ffe62ad3b2f42e49814bf941654ea6c/data";
        private const string DownloadFolder = "download";
        private const string BigQueryQuery = @"
                                                WITH
                                                -- 1) Transações unificadas filtradas para fevereiro e março de 2025
                                                transacoes AS (
                                                  SELECT
                                                    id_transacao,
                                                    TIMESTAMP(datetime_transacao) AS datetime_transacao,
                                                    CAST(id_veiculo AS STRING) AS id_veiculo
                                                  FROM `rj-smtr.br_rj_riodejaneiro_bilhetagem.transacao_riocard`
                                                  WHERE DATE(datetime_transacao) >= '2025-04-02'
                                                    AND DATE(datetime_transacao) < '2025-06-03'
  
                                                  UNION ALL
  
                                                  SELECT
                                                    id_transacao,
                                                    TIMESTAMP(datetime_transacao) AS datetime_transacao,
                                                    CAST(id_veiculo AS STRING) AS id_veiculo
                                                  FROM `rj-smtr.br_rj_riodejaneiro_bilhetagem.transacao`
                                                  WHERE DATE(datetime_transacao) >= '2025-04-02'
                                                    AND DATE(datetime_transacao) < '2025-06-03'
                                                ),

                                                -- 2) Viagens filtradas, com normalização do id_veiculo, para fevereiro e março de 2025,
                                                -- filtradas pelo servico_realizado conforme a lista fornecida
                                                viagens_filtradas AS (
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
                                                  WHERE DATE(datetime_partida) >= '2025-04-02'
                                                    AND DATE(datetime_partida) < '2025-06-03'
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
                                                    )
                                                ),

                                                -- 3) Match direto: transações que caem dentro do intervalo (partida ≤ transação ≤ chegada)
                                                direct_matches AS (
                                                  SELECT
                                                    t.id_transacao,
                                                    t.datetime_transacao,
                                                    t.id_veiculo,
                                                    v.id_viagem,
                                                    v.sentido,
                                                    v.servico_realizado,
                                                    v.datetime_partida,
                                                    v.datetime_chegada,
                                                    ROW_NUMBER() OVER (
                                                      PARTITION BY t.id_transacao
                                                      ORDER BY v.datetime_partida
                                                    ) AS rn
                                                  FROM transacoes t
                                                  JOIN viagens_filtradas v
                                                    ON v.id_veiculo_normalizado = t.id_veiculo
                                                    AND t.datetime_transacao BETWEEN v.datetime_partida AND v.datetime_chegada
                                                ),

                                                -- 4) Transações sem match direto (fora do intervalo)
                                                no_direct_matches AS (
                                                  SELECT t.*
                                                  FROM transacoes t
                                                  LEFT JOIN direct_matches dm
                                                    ON t.id_transacao = dm.id_transacao
                                                    AND dm.rn = 1
                                                  WHERE dm.id_viagem IS NULL
                                                ),

                                                -- 5) Próxima viagem: para transações sem match direto, busca a viagem cuja partida seja maior que a transação
                                                next_trip_candidates AS (
                                                  SELECT
                                                    ndm.id_transacao,
                                                    ndm.datetime_transacao,
                                                    ndm.id_veiculo,
                                                    v.id_viagem,
                                                    v.sentido,
                                                    v.servico_realizado,
                                                    v.datetime_partida,
                                                    v.datetime_chegada,
                                                    ROW_NUMBER() OVER (
                                                      PARTITION BY ndm.id_transacao
                                                      ORDER BY v.datetime_partida
                                                    ) AS rn
                                                  FROM no_direct_matches ndm
                                                  JOIN viagens_filtradas v
                                                    ON v.id_veiculo_normalizado = ndm.id_veiculo
                                                    AND v.datetime_partida > ndm.datetime_transacao
                                                ),

                                                -- 6) Transações que não encontraram nem intervalo nem próxima viagem
                                                no_next_trip AS (
                                                  SELECT ndm.*
                                                  FROM no_direct_matches ndm
                                                  LEFT JOIN next_trip_candidates ntc
                                                    ON ndm.id_transacao = ntc.id_transacao
                                                    AND ntc.rn = 1
                                                  WHERE ntc.id_viagem IS NULL
                                                ),

                                                -- 7) Última viagem: para transações sem match e sem próxima viagem, busca a viagem cuja partida seja menor que a transação (a mais recente)
                                                last_trip_candidates AS (
                                                  SELECT
                                                    nnt.id_transacao,
                                                    nnt.datetime_transacao,
                                                    nnt.id_veiculo,
                                                    v.id_viagem,
                                                    v.sentido,
                                                    v.servico_realizado,
                                                    v.datetime_partida,
                                                    v.datetime_chegada,
                                                    ROW_NUMBER() OVER (
                                                      PARTITION BY nnt.id_transacao
                                                      ORDER BY v.datetime_partida DESC
                                                    ) AS rn
                                                  FROM no_next_trip nnt
                                                  JOIN viagens_filtradas v
                                                    ON v.id_veiculo_normalizado = nnt.id_veiculo
                                                    AND v.datetime_partida < nnt.datetime_transacao
                                                ),

                                                -- 8) Consolida todas as transações alocadas (match direto / próxima viagem / última viagem)
                                                final_alocado AS (
                                                  SELECT
                                                    id_transacao,
                                                    datetime_transacao,
                                                    id_veiculo,
                                                    id_viagem,
                                                    sentido,
                                                    servico_realizado,
                                                    datetime_partida,
                                                    datetime_chegada
                                                  FROM direct_matches
                                                  WHERE rn = 1
  
                                                  UNION ALL
  
                                                  SELECT
                                                    id_transacao,
                                                    datetime_transacao,
                                                    id_veiculo,
                                                    id_viagem,
                                                    sentido,
                                                    servico_realizado,
                                                    datetime_partida,
                                                    datetime_chegada
                                                  FROM next_trip_candidates
                                                  WHERE rn = 1
  
                                                  UNION ALL
  
                                                  SELECT
                                                    id_transacao,
                                                    datetime_transacao,
                                                    id_veiculo,
                                                    id_viagem,
                                                    sentido,
                                                    servico_realizado,
                                                    datetime_partida,
                                                    datetime_chegada
                                                  FROM last_trip_candidates
                                                  WHERE rn = 1
                                                ),

                                                -- 9) Agregação das viagens, considerando todas as viagens (mesmo sem transações)
                                                agg_viagens AS (
                                                  SELECT
                                                    DATE(datetime_partida) AS data,
                                                    servico_realizado,
                                                    sentido,
                                                    COUNT(DISTINCT id_viagem) AS quantidade_viagens
                                                  FROM viagens_filtradas
                                                  GROUP BY data, servico_realizado, sentido
                                                ),

                                                -- 10) Agregação das transações alocadas
                                                agg_transacoes AS (
                                                  SELECT
                                                    DATE(datetime_transacao) AS data,
                                                    servico_realizado,
                                                    sentido,
                                                    COUNT(DISTINCT id_transacao) AS quantidade_transacoes
                                                  FROM final_alocado
                                                  GROUP BY data, servico_realizado, sentido
                                                ),

                                                -- 11) Agregação dos veículos utilizados (a partir das viagens)
                                                agg_veiculos AS (
                                                  SELECT
                                                    DATE(datetime_partida) AS data,
                                                    servico_realizado,
                                                    sentido,
                                                    COUNT(DISTINCT id_veiculo_normalizado) AS quantidade_veiculos
                                                  FROM viagens_filtradas
                                                  GROUP BY data, servico_realizado, sentido
                                                )

                                                -- 12) Junta as agregações, garantindo que viagens sem transações também sejam consideradas
                                                SELECT
                                                  COALESCE(v.data, t.data, ve.data) AS data,
                                                  COALESCE(v.servico_realizado, t.servico_realizado, ve.servico_realizado) AS servico_realizado,
                                                  COALESCE(v.sentido, t.sentido, ve.sentido) AS sentido,
                                                  v.quantidade_viagens,
                                                  IFNULL(t.quantidade_transacoes, 0) AS quantidade_transacoes,
                                                  ve.quantidade_veiculos
                                                FROM agg_viagens v
                                                FULL OUTER JOIN agg_transacoes t
                                                  ON v.data = t.data
                                                  AND v.servico_realizado = t.servico_realizado
                                                  AND v.sentido = t.sentido
                                                FULL OUTER JOIN agg_veiculos ve
                                                  ON COALESCE(v.data, t.data) = ve.data
                                                  AND COALESCE(v.servico_realizado, t.servico_realizado) = ve.servico_realizado
                                                  AND COALESCE(v.sentido, t.sentido) = ve.sentido
                                                ORDER BY data, servico_realizado, sentido";

        public DownloadJob(ILogger<DownloadJob> logger, EmailService emailService)
        {
            _logger = logger;
            _emailService = emailService;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            try
            {
                _logger.LogInformation("Iniciando download do arquivo...");

                if (!Directory.Exists(DownloadFolder))
                {
                    Directory.CreateDirectory(DownloadFolder);
                }

                using var httpClient = new HttpClient();
                var response = await httpClient.GetAsync(DownloadUrl);
                response.EnsureSuccessStatusCode();

                var fileName = Path.Combine(DownloadFolder, "dados.zip");
                using (var fileStream = File.Create(fileName))
                {
                    await response.Content.CopyToAsync(fileStream);
                }

                _logger.LogInformation("Download concluído com sucesso!");

                try
                {
                    _logger.LogInformation("Iniciando processamento do arquivo GTFS...");
                    var gtfsProcessor = new GTFSProcessor();
                    gtfsProcessor.ProcessarGTFS(fileName);
                    _logger.LogInformation("Processamento GTFS concluído com sucesso!");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Erro ao processar arquivo GTFS");
                    throw;
                }

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

                var credentialsPath = Path.Combine(DownloadFolder, "credentials.json");
                await File.WriteAllTextAsync(credentialsPath, credentialsJson);

                try
                {
                    var credential = GoogleCredential.FromJson(credentialsJson);
                    var bigQueryClient = await BigQueryClient.CreateAsync("fretamento", credential);

                    _logger.LogInformation("Iniciando consulta ao BigQuery...");
                    var results = await bigQueryClient.ExecuteQueryAsync(BigQueryQuery, null);

                    _logger.LogInformation("Criando arquivo Excel...");
                    using var workbook = new XLWorkbook();
                    var worksheet = workbook.Worksheets.Add("Dados");

                    worksheet.Cell(1, 1).Value = "data";
                    worksheet.Cell(1, 2).Value = "servico_realizado";
                    worksheet.Cell(1, 3).Value = "sentido";
                    worksheet.Cell(1, 4).Value = "quantidade_viagens";
                    worksheet.Cell(1, 5).Value = "quantidade_transacoes";
                    worksheet.Cell(1, 6).Value = "quantidade_veiculos";

                    int row = 2;
                    if (results != null && results.Any())
                    {
                        foreach (var rowData in results)
                        {
                            worksheet.Cell(row, 1).Value = rowData["data"]?.ToString() ?? "N/A";
                            worksheet.Cell(row, 2).Value = rowData["servico_realizado"]?.ToString() ?? "N/A";
                            worksheet.Cell(row, 3).Value = rowData["sentido"]?.ToString() ?? "N/A";
                            worksheet.Cell(row, 4).Value = rowData["quantidade_viagens"]?.ToString() ?? "N/A";
                            worksheet.Cell(row, 5).Value = rowData["quantidade_transacoes"]?.ToString() ?? "N/A";
                            worksheet.Cell(row, 6).Value = rowData["quantidade_veiculos"]?.ToString() ?? "N/A";
                            row++;
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Nenhum resultado encontrado na consulta BigQuery");
                    }

                    var excelFileName = Path.Combine(DownloadFolder, "dados_bigquery.xlsx");
                    workbook.SaveAs(excelFileName);
                    _logger.LogInformation("Arquivo Excel criado com sucesso!");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Erro ao processar BigQuery");
                    throw;
                }
                finally
                {
                    // Limpa o arquivo de credenciais
                    if (File.Exists(credentialsPath))
                    {
                        File.Delete(credentialsPath);
                    }
                }
                var jupyterExePath = @"C:\ProgramData\anaconda3\Scripts\jupyter-nbconvert.exe";
                var notebookPath = Path.Combine(DownloadFolder, "analise_concorrencia_tijuca.ipynb");
                var outputBuilder = new StringBuilder();
                var errorBuilder = new StringBuilder();
                if (File.Exists(notebookPath))
                {
                    _logger.LogInformation("Executando analise_concorrencia.ipynb...");
                    using (var process = new Process())
                    {
                            process.StartInfo = new ProcessStartInfo
                            {
                                FileName = jupyterExePath,
                                Arguments = $"--execute --to notebook --inplace \"{notebookPath}\"",
                                RedirectStandardOutput = true,
                                RedirectStandardError = true,
                                UseShellExecute = false,
                                CreateNoWindow = true,
                                EnvironmentVariables = {
                                ["PYTHONIOENCODING"] = "utf-8"
                            }
                            };

                            // Adiciona um script Python para tratar valores infinitos antes de executar o notebook
                            var preProcessScript = @"
                                                import pandas as pd
                                                import numpy as np

                                                def tratar_valores_infinitos(df):
                                                    # Substitui infinitos por NaN
                                                    df = df.replace([np.inf, -np.inf], np.nan)
                                                    # Substitui NaN por 0 ou outro valor apropriado
                                                    df = df.fillna(0)
                                                    return df

                                                # Carrega o notebook
                                                with open('analise_concorrencia_tijuca.ipynb', 'r', encoding='utf-8') as f:
                                                    notebook = f.read()

                                                # Adiciona o código de tratamento de infinitos antes da geração dos relatórios
                                                notebook = notebook.replace(
                                                    'for dia in dias_semana:',
                                                    'for dia in dias_semana:\n    df_tabela = tratar_valores_infinitos(df_tabela)\n    df_concorrentes = tratar_valores_infinitos(df_concorrentes)'
                                                )

                                                # Salva o notebook modificado
                                                with open('analise_concorrencia_tijuca.ipynb', 'w', encoding='utf-8') as f:
                                                    f.write(notebook)
                                                ";

                            // Salva o script de pré-processamento
                            var preProcessScriptPath = Path.Combine(DownloadFolder, "preprocess.py");
                            await File.WriteAllTextAsync(preProcessScriptPath, preProcessScript);

                            // Executa o script de pré-processamento
                            using (var preProcess = new Process())
                            {
                                preProcess.StartInfo = new ProcessStartInfo
                                {
                                    FileName = "python",
                                    Arguments = $"\"{preProcessScriptPath}\"",
                                    RedirectStandardOutput = true,
                                    RedirectStandardError = true,
                                    UseShellExecute = false,
                                    CreateNoWindow = true
                                };

                                preProcess.Start();
                                await preProcess.WaitForExitAsync();
                            }

                            // Agora executa o notebook
                            process.Start();

                            process.OutputDataReceived += (sender, args) =>
                            {
                                if (args.Data != null)
                                    outputBuilder.AppendLine(args.Data);
                            };

                            process.ErrorDataReceived += (sender, args) =>
                            {
                                if (args.Data != null)
                                    errorBuilder.AppendLine(args.Data);
                            };

                            process.BeginOutputReadLine();
                            process.BeginErrorReadLine();

                            await process.WaitForExitAsync();

                            string output = outputBuilder.ToString();
                            string error = errorBuilder.ToString();
                            bool success = process.ExitCode == 0;

                            if (success)
                            {
                                _logger.LogInformation("analise_concorrencia.ipynb executado com sucesso!");
                                if (!string.IsNullOrEmpty(output))
                                    _logger.LogInformation($"Saída do notebook: {output}");
                            }
                            else
                            {
                                _logger.LogError($"Erro ao executar analise_concorrencia.ipynb: {error}");
                            }

                            // Verifica se o notebook foi executado com sucesso
                            var notebookOutputPath = Path.Combine(DownloadFolder, "analise_concorrencia_tijuca.ipynb");
                            var notebookExecuted = File.Exists(notebookOutputPath) &&
                                                  File.GetLastWriteTime(notebookOutputPath) > DateTime.Now.AddMinutes(-3);

                            if (notebookExecuted)
                            {
                                _logger.LogInformation("Notebook executado e arquivo atualizado com sucesso!");
                                success = true;
                            }
                            else
                            {
                                _logger.LogError("Notebook não foi executado corretamente ou arquivo não foi atualizado!");
                                success = false;
                                error = "O notebook não foi executado corretamente ou o arquivo não foi atualizado dentro do tempo esperado.";
                            }

                            // Enviar email com o relatório de execução
                            try
                        {
                            _logger.LogInformation("Iniciando envio do email...");
                            await _emailService.SendExecutionReportAsync(true, "output", "error");
                            _logger.LogInformation("Email enviado com sucesso!");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Erro ao enviar email de relatório");
                            throw; // Re-throw para que o erro seja registrado no log do job
                        }
                    }
                }
                else
                {
                    _logger.LogWarning("Arquivo analise_concorrencia.ipynb não encontrado!");
                    try
                    {
                        await _emailService.SendExecutionReportAsync(
                            false, 
                            string.Empty, 
                            "Arquivo analise_concorrencia.ipynb não encontrado!");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Erro ao enviar email de relatório");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao executar o job");
                throw;
            }
        }
    }
} 