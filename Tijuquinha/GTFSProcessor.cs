using CsvHelper;
using CsvHelper.Configuration.Attributes;
using System;
using System.Collections.Generic;
using System.Formats.Asn1;
using System.Globalization;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tijuquinha
{
    public class GTFSProcessor
    {
        private const string DownloadFolder = "download";
        private class Route
        {
            [Name("route_id")]
            public string RouteId { get; set; }

            [Name("route_short_name")]
            public string RouteShortName { get; set; }

            [Name("route_long_name")]
            public string RouteLongName { get; set; }
        }

        private class Trip
        {
            [Name("route_id")]
            public string RouteId { get; set; }

            [Name("trip_id")]
            public string TripId { get; set; }

            [Name("direction_id")]
            public int DirectionId { get; set; }
        }

        private class StopTime
        {
            [Name("trip_id")]
            public string TripId { get; set; }

            [Name("stop_id")]
            public string StopId { get; set; }
        }

        private class Stop
        {
            [Name("stop_id")]
            public string StopId { get; set; }

            [Name("stop_name")]
            public string StopName { get; set; }
        }

        private class LinhaCompartilhadaModel
        {
            [Name("linha_base")]
            public string LinhaBase { get; set; }

            [Name("direcao_base")]
            public string DirecaoBase { get; set; }

            [Name("total_pontos_linha_base")]
            public int TotalPontosLinhaBase { get; set; }

            [Name("linha_compartilhada")]
            public string LinhaCompartilhada { get; set; }

            [Name("direcao_compartilhada")]
            public string DirecaoCompartilhada { get; set; }

            [Name("num_pontos_compartilhados")]
            public int NumPontosCompartilhados { get; set; }

            [Name("percentual_cobertura")]
            public double PercentualCobertura { get; set; }

            [Name("pontos_compartilhados")]
            public string PontosCompartilhados { get; set; }
        }

        public void ProcessarGTFS(string zipPath)
        {
            Console.WriteLine("Carregando arquivos GTFS...");
            
            // Carregar arquivos CSV do ZIP
            var routes = CarregarCsvDoZip<Route>(zipPath, "routes.txt");
            var trips = CarregarCsvDoZip<Trip>(zipPath, "trips.txt");
            var stopTimes = CarregarCsvDoZip<StopTime>(zipPath, "stop_times.txt");
            var stops = CarregarCsvDoZip<Stop>(zipPath, "stops.txt");

            // Linhas de interesse
            var lineNumbers = new List<string>
            {
                "165", "220", "229", "301", "302", "SN302", "303", "315", "435", "448",
                "603", "607", "608", "645", "702", "805", "SP805", "810", "SN810",
                "SP810", "865"
            };

            Console.WriteLine($"Analisando {lineNumbers.Count} linhas de ônibus...");

            // Filtrar rotas de interesse
            var filteredRoutes = routes.Where(r => lineNumbers.Contains(r.RouteShortName)).ToList();

            // Mapear route_id para route_short_name
            var routeIdToName = filteredRoutes.ToDictionary(r => r.RouteId, r => r.RouteShortName);

            // Mapear route_short_name para route_id (pode haver múltiplos route_ids para uma linha)
            var nameToRouteIds = new Dictionary<string, List<string>>();
            foreach (var route in filteredRoutes)
            {
                if (!nameToRouteIds.ContainsKey(route.RouteShortName))
                    nameToRouteIds[route.RouteShortName] = new List<string>();
                nameToRouteIds[route.RouteShortName].Add(route.RouteId);
            }

            // Dicionário para armazenar os pontos de parada de cada linha por direção
            var lineStops = new Dictionary<string, Dictionary<string, HashSet<string>>>();

            // Dicionário para contar o número total de pontos para cada linha/direção
            var totalStopsCount = new Dictionary<string, Dictionary<string, int>>();

            // Obter todos os pontos de parada para as linhas de interesse
            Console.WriteLine("Identificando pontos de parada para cada linha de interesse...");
            foreach (var routeShortName in nameToRouteIds.Keys)
            {
                if (!lineStops.ContainsKey(routeShortName))
                    lineStops[routeShortName] = new Dictionary<string, HashSet<string>>();
                if (!totalStopsCount.ContainsKey(routeShortName))
                    totalStopsCount[routeShortName] = new Dictionary<string, int>();

                foreach (var routeId in nameToRouteIds[routeShortName])
                {
                    var routeTrips = trips.Where(t => t.RouteId == routeId);
                    foreach (var trip in routeTrips)
                    {
                        var direction = trip.DirectionId == 0 ? "Ida" : "Volta";
                        if (!lineStops[routeShortName].ContainsKey(direction))
                            lineStops[routeShortName][direction] = new HashSet<string>();

                        var tripStops = stopTimes.Where(st => st.TripId == trip.TripId);
                        foreach (var stopTime in tripStops)
                        {
                            lineStops[routeShortName][direction].Add(stopTime.StopId);
                        }
                    }
                }
            }

            // Calcular o número total de pontos para cada linha/direção
            foreach (var line in lineStops.Keys)
            {
                foreach (var direction in lineStops[line].Keys)
                {
                    if (!totalStopsCount[line].ContainsKey(direction))
                        totalStopsCount[line][direction] = 0;
                    totalStopsCount[line][direction] = lineStops[line][direction].Count;
                }
            }

            // Mapear todos os pontos de parada para TODAS as linhas
            Console.WriteLine("Mapeando pontos de parada para todas as linhas...");
            var allLineStops = new Dictionary<string, Dictionary<string, HashSet<string>>>();
            foreach (var route in routes)
            {
                if (!allLineStops.ContainsKey(route.RouteShortName))
                    allLineStops[route.RouteShortName] = new Dictionary<string, HashSet<string>>();

                var routeTrips = trips.Where(t => t.RouteId == route.RouteId);
                foreach (var trip in routeTrips)
                {
                    var direction = trip.DirectionId == 0 ? "Ida" : "Volta";
                    if (!allLineStops[route.RouteShortName].ContainsKey(direction))
                        allLineStops[route.RouteShortName][direction] = new HashSet<string>();

                    var tripStops = stopTimes.Where(st => st.TripId == trip.TripId);
                    foreach (var stopTime in tripStops)
                    {
                        allLineStops[route.RouteShortName][direction].Add(stopTime.StopId);
                    }
                }
            }

            // Dicionário para armazenar as linhas que compartilham os mesmos pontos
            var sharedStops = new Dictionary<string, Dictionary<string, Dictionary<string, Dictionary<string, HashSet<string>>>>>();

            // Identificar linhas que compartilham pontos por direção
            Console.WriteLine("Identificando linhas que compartilham pontos por direção...");
            foreach (var baseLine in lineStops.Keys)
            {
                foreach (var baseDirection in lineStops[baseLine].Keys)
                {
                    foreach (var otherLine in allLineStops.Keys)
                    {
                        if (otherLine == baseLine) continue;

                        foreach (var otherDirection in allLineStops[otherLine].Keys)
                        {
                            var commonStops = lineStops[baseLine][baseDirection].Intersect(allLineStops[otherLine][otherDirection]).ToHashSet();
                            if (commonStops.Any())
                            {
                                if (!sharedStops.ContainsKey(baseLine))
                                    sharedStops[baseLine] = new Dictionary<string, Dictionary<string, Dictionary<string, HashSet<string>>>>();
                                if (!sharedStops[baseLine].ContainsKey(baseDirection))
                                    sharedStops[baseLine][baseDirection] = new Dictionary<string, Dictionary<string, HashSet<string>>>();
                                if (!sharedStops[baseLine][baseDirection].ContainsKey(otherLine))
                                    sharedStops[baseLine][baseDirection][otherLine] = new Dictionary<string, HashSet<string>>();
                                sharedStops[baseLine][baseDirection][otherLine][otherDirection] = commonStops;
                            }
                        }
                    }
                }
            }

            // Mapear stop_id para stop_name
            var stopIdToName = stops.ToDictionary(s => s.StopId, s => s.StopName);

            // Gerar relatório
            Console.WriteLine("\n=== RELATÓRIO DE LINHAS QUE COMPARTILHAM PONTOS DE ÔNIBUS POR DIREÇÃO ===\n");

            foreach (var baseLine in sharedStops.Keys.OrderBy(k => k))
            {
                Console.WriteLine($"Linha: {baseLine}");

                foreach (var baseDirection in sharedStops[baseLine].Keys.OrderBy(k => k))
                {
                    var totalStops = totalStopsCount[baseLine][baseDirection];
                    Console.WriteLine($"  Direção: {baseDirection} (Total de pontos: {totalStops})");

                    foreach (var otherLine in sharedStops[baseLine][baseDirection].Keys.OrderBy(k => k))
                    {
                        Console.WriteLine($"    Linha concorrente: {otherLine}");

                        foreach (var otherDirection in sharedStops[baseLine][baseDirection][otherLine].Keys.OrderBy(k => k))
                        {
                            var sharedStopIds = sharedStops[baseLine][baseDirection][otherLine][otherDirection];
                            var sharedStopNames = sharedStopIds
                                .Select(stopId => stopIdToName.ContainsKey(stopId) ? stopIdToName[stopId] : $"Parada {stopId}")
                                .OrderBy(name => name)
                                .ToList();

                            Console.WriteLine($"      Direção: {otherDirection} - {sharedStopIds.Count} pontos compartilhados");
                            for (int i = 0; i < sharedStopNames.Count; i++)
                            {
                                Console.WriteLine($"        {i + 1}. {sharedStopNames[i]}");
                            }
                        }
                        Console.WriteLine();
                    }
                    Console.WriteLine();
                }
                Console.WriteLine();
            }

            // Gerar arquivo CSV
            Console.WriteLine("Gerando arquivo CSV com os resultados...");
            var linhasCompartilhadas = new List<LinhaCompartilhadaModel>();

            foreach (var baseLine in sharedStops.Keys)
            {
                foreach (var baseDirection in sharedStops[baseLine].Keys)
                {
                    var totalStops = totalStopsCount[baseLine][baseDirection];

                    foreach (var otherLine in sharedStops[baseLine][baseDirection].Keys)
                    {
                        foreach (var otherDirection in sharedStops[baseLine][baseDirection][otherLine].Keys)
                        {
                            var sharedStopIds = sharedStops[baseLine][baseDirection][otherLine][otherDirection];
                            var sharedStopNames = sharedStopIds
                                .Select(stopId => stopIdToName.ContainsKey(stopId) ? stopIdToName[stopId] : $"Parada {stopId}")
                                .OrderBy(name => name)
                                .ToList();

                            linhasCompartilhadas.Add(new LinhaCompartilhadaModel
                            {
                                LinhaBase = baseLine,
                                DirecaoBase = baseDirection,
                                TotalPontosLinhaBase = totalStops,
                                LinhaCompartilhada = otherLine,
                                DirecaoCompartilhada = otherDirection,
                                PercentualCobertura = totalStops > 0 ? Math.Round((sharedStopIds.Count / (double)totalStops) * 100, 2) : 0,
                                NumPontosCompartilhados = sharedStopIds.Count,
                                PontosCompartilhados = string.Join(", ", sharedStopNames)
                            });
                        }
                    }
                }
            }

            // Salvar como CSV
            var csvPath = Path.Combine(DownloadFolder, "linhas_compartilhadas_por_direcao_completo.csv");
            using (var writer = new StreamWriter(csvPath, false, Encoding.UTF8))
            using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
            {
                csv.WriteRecords(linhasCompartilhadas);
            }

            Console.WriteLine($"Relatório salvo como {csvPath}");
            Console.WriteLine("Análise concluída!");
        }

        private List<T> CarregarCsvDoZip<T>(string zipPath, string fileName)
        {
            using (var fileStream = File.OpenRead(zipPath))
            using (var zip = new ZipArchive(fileStream, ZipArchiveMode.Read))
            {
                var entry = zip.GetEntry(fileName);
                if (entry == null) return new List<T>();

                using (var reader = new StreamReader(entry.Open()))
                using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
                {
                    return csv.GetRecords<T>().ToList();
                }
            }
        }
    }
}
