
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
                                                