import json
import numpy as np
import pandas as pd

def corrigir_notebook(notebook_path):
    # Lê o notebook
    with open(notebook_path, 'r', encoding='utf-8') as f:
        notebook = json.load(f)
    
    # Encontra a célula que contém a função gerar_tabela_compacta
    for cell in notebook['cells']:
        if cell['cell_type'] == 'code':
            source = ''.join(cell['source'])
            if 'def gerar_tabela_compacta' in source:
                # Substitui o código da função
                novo_codigo = '''
def gerar_tabela_compacta(canvas, titulo, dados_resumo, posicao):
    """
    Gera uma tabela compacta diretamente em um canvas existente.
    Adiciona percentuais de variação entre parênteses.
    
    Args:
        canvas: Canvas do ReportLab para desenhar
        titulo: Título da tabela
        dados_resumo: DataFrame com os dados resumidos
        posicao: Tupla (x, y) da posição na página
    """
    # Verifica se o DataFrame está vazio
    if dados_resumo.empty:
        return
        
    # Limita o número de linhas para garantir que caiba na página
    # Máximo de 10 linhas por tabela para evitar que saia da página
    if len(dados_resumo) > 10:
        dados_resumo = dados_resumo.head(10)
    
    # Prepara os dados para a tabela (sem casas decimais e abreviados)
    tabela_dados = [['Interv.', 'Pass.', 'Part.', 'Frota']]
    
    for _, row in dados_resumo.iterrows():
        # Abrevia os nomes dos intervalos para economizar espaço
        intervalo = row['intervalo']
        intervalo = intervalo.replace('semana', 'sem')
        intervalo = intervalo.replace('Janeiro', 'Jan')
        intervalo = intervalo.replace('Fevereiro', 'Fev')
        intervalo = intervalo.replace('Março', 'Mar')
        intervalo = intervalo.replace('Abril', 'Abr')
        intervalo = intervalo.replace('Maio', 'Mai')
        intervalo = intervalo.replace('Junho', 'Jun')
        intervalo = intervalo.replace('Julho', 'Jul')
        intervalo = intervalo.replace('Agosto', 'Ago')
        intervalo = intervalo.replace('Setembro', 'Set')
        intervalo = intervalo.replace('Outubro', 'Out')
        intervalo = intervalo.replace('Novembro', 'Nov')
        intervalo = intervalo.replace('Dezembro', 'Dez')
        
        # Limita o tamanho do texto do intervalo para 12 caracteres
        if len(intervalo) > 12:
            intervalo = intervalo[:9] + '...'
        
        # Prepara a formatação dos valores com variações percentuais (sem casas decimais)
        passageiros_str = f"{int(row['passageiros']):,}".replace(',', '.') if not pd.isna(row['passageiros']) else "-"
        if not pd.isna(row['passageiros_var']):
            # Trata valores infinitos
            if np.isinf(row['passageiros_var']):
                passageiros_str += " (∞%)"
            else:
                passageiros_str += f" ({int(row['passageiros_var'])}%)"
        
        partidas_str = f"{int(row['partidas'])}" if not pd.isna(row['partidas']) else "-"
        if not pd.isna(row['partidas_var']):
            # Trata valores infinitos
            if np.isinf(row['partidas_var']):
                partidas_str += " (∞%)"
            else:
                partidas_str += f" ({int(row['partidas_var'])}%)"
        
        frota_str = f"{int(row['frota'])}" if not pd.isna(row['frota']) else "-"
        if not pd.isna(row['frota_var']):
            # Trata valores infinitos
            if np.isinf(row['frota_var']):
                frota_str += " (∞%)"
            else:
                frota_str += f" ({int(row['frota_var'])}%)"
        
        tabela_dados.append([
            intervalo,
            passageiros_str,
            partidas_str,
            frota_str
        ])
    
    # Cria uma tabela com melhor espaçamento entre colunas e coluna de intervalo reduzida
    table = Table(tabela_dados, colWidths=[0.9*inch, 1.0*inch, 0.7*inch, 0.7*inch], spaceBefore=5, spaceAfter=5)
    table.setStyle(TableStyle([
        ('BACKGROUND', (0, 0), (-1, 0), colors.lightgrey),
        ('TEXTCOLOR', (0, 0), (-1, 0), colors.black),
        ('ALIGN', (0, 0), (-1, -1), 'CENTER'),
        ('ALIGN', (0, 1), (0, -1), 'LEFT'),
        ('FONTNAME', (0, 0), (-1, 0), 'Helvetica-Bold'),
        ('FONTSIZE', (0, 0), (-1, 0), 8),           # Fonte um pouco maior para legibilidade
        ('FONTSIZE', (0, 1), (-1, -1), 7),          # Fonte um pouco maior para legibilidade
        ('BOTTOMPADDING', (0, 0), (-1, -1), 3),     # Padding um pouco maior
        ('TOPPADDING', (0, 0), (-1, -1), 3),        # Padding um pouco maior
        ('GRID', (0, 0), (-1, -1), 1, colors.black), # Linha da grade mais grossa
        ('VALIGN', (0, 0), (-1, -1), 'MIDDLE'),
        ('BACKGROUND', (0, 1), (-1, -1), colors.white),
    ]))
    
    # Posição da tabela
    table_x, table_y = posicao
    
    # Garante que a tabela caiba na página (ajusta posição Y se necessário)
    # Obtém as dimensões da tabela
    table_width, table_height = table.wrapOn(canvas, 300, 500)
    
    # Se a tabela for ficar fora da página, ajuste a posição Y
    if table_y - table_height < 30:  # Garante pelo menos 30 pontos de margem inferior
        table_y = 30 + table_height
    
    # Adiciona título da tabela acima dela (com mais espaço)
    canvas.setFont("Helvetica-Bold", 9)  # Fonte um pouco maior para legibilidade
    canvas.drawString(table_x, table_y + 15, titulo)  # 15 pontos acima da tabela
    
    # Desenha a tabela
    table.drawOn(canvas, table_x, table_y - table_height)
'''
                cell['source'] = novo_codigo.split('\n')
                break
    
    # Salva o notebook corrigido
    with open(notebook_path, 'w', encoding='utf-8') as f:
        json.dump(notebook, f, ensure_ascii=False, indent=1)

if __name__ == '__main__':
    notebook_path = 'analise_concorrencia_tijuca.ipynb'
    corrigir_notebook(notebook_path) 