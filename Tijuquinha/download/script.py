import pandas as pd
import zipfile
from collections import defaultdict

def read_csv_from_zip(zip_path, file_name):
    with zipfile.ZipFile(zip_path, 'r') as z:
        return pd.read_csv(z.open(file_name))

def main():
    # Carregar dados GTFS
    zip_path = r"C:\projetos_one\automacao\Tijuquinha\Tijuquinha\download\dados.zip"
    
    # Carregar os arquivos necessários
    print("Carregando arquivos GTFS...")
    routes = read_csv_from_zip(zip_path, 'routes.txt')
    trips = read_csv_from_zip(zip_path, 'trips.txt')
    stop_times = read_csv_from_zip(zip_path, 'stop_times.txt')
    stops = read_csv_from_zip(zip_path, 'stops.txt')
    
    # Linhas de interesse conforme o código original
    line_numbers = ["165", "220", "229", "301", "302", "SN302", "303", "315", "435", "448", 
                    "603", "607", "608", "645", "702", "805", "SP805", "810", "SN810", 
                    "SP810", "865"]
    
    print(f"Analisando {len(line_numbers)} linhas de ônibus...")
    
    # Filtrar rotas de interesse
    filtered_routes = routes[routes['route_short_name'].isin(line_numbers)]
    
    # Mapear route_id para route_short_name
    route_id_to_name = dict(zip(filtered_routes['route_id'], filtered_routes['route_short_name']))
    
    # Mapear route_short_name para route_id (pode haver múltiplos route_ids para uma linha)
    name_to_route_ids = defaultdict(list)
    for _, row in filtered_routes.iterrows():
        name_to_route_ids[row['route_short_name']].append(row['route_id'])
    
    # Dicionário para armazenar os pontos de parada de cada linha por direção
    line_stops = defaultdict(lambda: defaultdict(set))
    
    # Dicionário para contar o número total de pontos para cada linha/direção
    total_stops_count = defaultdict(lambda: defaultdict(int))
    
    # Obter todos os pontos de parada para as linhas de interesse
    print("Identificando pontos de parada para cada linha de interesse...")
    for route_short_name, route_ids in name_to_route_ids.items():
        for route_id in route_ids:
            # Obter viagens para esta rota
            route_trips = trips[trips['route_id'] == route_id]
            
            for _, trip in route_trips.iterrows():
                direction = "Ida" if trip['direction_id'] == 0 else "Volta"
                
                # Obter os pontos de parada para esta viagem
                trip_stops = stop_times[stop_times['trip_id'] == trip['trip_id']]
                
                # Adicionar os IDs dos pontos de parada ao conjunto
                for _, stop_time in trip_stops.iterrows():
                    line_stops[route_short_name][direction].add(stop_time['stop_id'])
    
    # Calcular o número total de pontos para cada linha/direção
    for line, directions in line_stops.items():
        for direction, stops_set in directions.items():
            total_stops_count[line][direction] = len(stops_set)
    
    # Mapear todos os pontos de parada para TODAS as linhas (incluindo as que não são de interesse)
    print("Mapeando pontos de parada para todas as linhas...")
    all_line_stops = defaultdict(lambda: defaultdict(set))
    
    for _, route in routes.iterrows():
        route_id = route['route_id']
        route_short_name = route['route_short_name']
        
        # Obter viagens para esta rota
        route_trips = trips[trips['route_id'] == route_id]
        
        for _, trip in route_trips.iterrows():
            direction = "Ida" if trip['direction_id'] == 0 else "Volta"
            
            # Obter os pontos de parada para esta viagem
            trip_stops = stop_times[stop_times['trip_id'] == trip['trip_id']]
            
            # Adicionar os IDs dos pontos de parada ao conjunto
            for _, stop_time in trip_stops.iterrows():
                all_line_stops[route_short_name][direction].add(stop_time['stop_id'])
    
    # Dicionário para armazenar as linhas que compartilham os mesmos pontos
    # Estrutura: {linha_base: {direcao_base: {linha_compartilhada: {direcao_compartilhada: set(stop_ids)}}}}
    shared_stops = defaultdict(lambda: defaultdict(lambda: defaultdict(lambda: defaultdict(set))))
    
    # Para cada linha de interesse, encontrar outras linhas que compartilham seus pontos
    print("Identificando linhas que compartilham pontos por direção...")
    for base_line, base_directions in line_stops.items():
        for base_direction, base_stop_ids in base_directions.items():
            # Comparar com todas as outras linhas
            for other_line, other_directions in all_line_stops.items():
                # Pular apenas a mesma linha
                if other_line == base_line:
                    continue
                # Não vamos mais pular as linhas do conjunto de interesse
                
                # Verificar cada direção da linha concorrente
                for other_direction, other_stop_ids in other_directions.items():
                    # Encontrar a interseção entre os conjuntos de pontos
                    common_stops = base_stop_ids.intersection(other_stop_ids)
                    
                    # Se houver pontos em comum, adicionar à estrutura
                    if common_stops:
                        shared_stops[base_line][base_direction][other_line][other_direction] = common_stops
    
    # Converter os IDs dos pontos para nomes
    stop_id_to_name = dict(zip(stops['stop_id'], stops['stop_name']))
    
    # Gerar relatório
    print("\n=== RELATÓRIO DE LINHAS QUE COMPARTILHAM PONTOS DE ÔNIBUS POR DIREÇÃO (INCLUINDO LINHAS DO CONJUNTO) ===\n")
    
    for base_line in sorted(shared_stops.keys()):
        print(f"Linha: {base_line}")
        
        for base_direction, competing_lines in sorted(shared_stops[base_line].items()):
            total_stops = total_stops_count[base_line][base_direction]
            print(f"  Direção: {base_direction} (Total de pontos: {total_stops})")
            
            # Ordenar as outras linhas alfabeticamente
            for other_line in sorted(competing_lines.keys()):
                print(f"    Linha concorrente: {other_line}")
                
                # Ordenar as direções da linha concorrente por número de pontos compartilhados (decrescente)
                sorted_directions = sorted(
                    competing_lines[other_line].items(),
                    key=lambda x: len(x[1]),
                    reverse=True
                )
                
                for other_direction, shared_stop_ids in sorted_directions:
                    shared_stop_names = [stop_id_to_name.get(stop_id, f"Parada {stop_id}") for stop_id in shared_stop_ids]
                    print(f"      Direção: {other_direction} - {len(shared_stop_ids)} pontos compartilhados")
                    for i, stop_name in enumerate(sorted(shared_stop_names), 1):
                        print(f"        {i}. {stop_name}")
                
                print()  # Linha em branco entre linhas concorrentes
            
            print()  # Linha em branco entre direções
        
        print()  # Linha em branco entre linhas base
    
    # Gerar um arquivo CSV com os resultados
    print("Gerando arquivo CSV com os resultados...")
    
    # Preparar os dados para o DataFrame
    rows = []
    for base_line in shared_stops.keys():
        for base_direction in shared_stops[base_line].keys():
            total_stops = total_stops_count[base_line][base_direction]
            
            for other_line in shared_stops[base_line][base_direction].keys():
                for other_direction, shared_stop_ids in shared_stops[base_line][base_direction][other_line].items():
                    shared_stop_names = [stop_id_to_name.get(stop_id, f"Parada {stop_id}") for stop_id in shared_stop_ids]
                    rows.append({
                        'linha_base': base_line,
                        'direcao_base': base_direction,
                        'total_pontos_linha_base': total_stops,
                        'linha_compartilhada': other_line,
                        'direcao_compartilhada': other_direction,
                        'num_pontos_compartilhados': len(shared_stop_ids),
                        'percentual_cobertura': round((len(shared_stop_ids) / total_stops) * 100, 2) if total_stops > 0 else 0,
                        'pontos_compartilhados': ', '.join(sorted(shared_stop_names))
                    })
    
    # Criar o DataFrame e salvar como CSV
    results_df = pd.DataFrame(rows)
    results_df = results_df.sort_values(['linha_base', 'direcao_base', 'linha_compartilhada', 'direcao_compartilhada'], 
                                         ascending=[True, True, True, True])
    
    csv_path = "linhas_compartilhadas_por_direcao_completo.csv"
    results_df.to_csv(csv_path, index=False, encoding='utf-8-sig')
    
    print(f"Relatório salvo como {csv_path}")
    print("Análise concluída!")

if __name__ == "__main__":
    main()