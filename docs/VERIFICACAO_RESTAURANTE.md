# Verificação no restaurante (Fase 5 do plano de melhorias)

Lista única para a visita ao restaurante: tudo o que até agora só foi verificado em simulação (impressoras virtuais, leitores
simulados, PDA emulado) e precisa do equipamento real. Imprima esta página, anote ✔ / ✖ e, em cada ✖, a mensagem exacta e uma
fotografia (talão, ecrã). No fim, passe os resultados para `docs/TAREFAS.md` (as linhas indicadas em «Tarefa»).

**Antes de ir:** Setup novo no servidor e em cada posto (HTTPS, agente actualizado); APK nova no PDA; uma licença válida;
produtos de teste com código de barras e um com «pede a quantidade» (kg). Leve papel de 80 mm e de 58 mm e etiquetas 30 × 20 mm.

**Na chegada (5 min):** no servidor e em cada posto, numa consola de administrador:
```powershell
powershell -ExecutionPolicy Bypass -File "C:\Program Files\LAMGestRestaurant\scripts\verificar-instalacao.ps1" -Relatorio C:\Temp\verificacao.txt
```
Tem de terminar com **0 falhas**. Guarde os relatórios.

Equipamento usado (anotar marca e modelo de cada um):

| Equipamento | Marca / modelo | Ligação (USB, rede, série) | Posto |
|---|---|---|---|
| Impressora térmica de caixa | | | |
| Impressora térmica de cozinha / bar | | | |
| Gaveta de dinheiro | | (na impressora de caixa) | |
| Impressora de etiquetas | | | |
| Balança etiquetadora | | | |
| Balança ligada ao posto | | | |
| Leitor de código de barras | | | |
| Leitor de cartões magnéticos | | | |
| Visor de poste | | | |
| Monitor táctil / segundo monitor | | | |
| PDA Sunmi | | | |

## A. Impressoras térmicas e gaveta
| # | Passo | Resultado esperado | ✔ / ✖ | Tarefa* |
|---|---|---|---|---|
| A1 | Instalar o driver da térmica de caixa no posto; Configurações → Impressoras → Nova: função Caixa, «Ligada a: este posto», papel 80 mm → **Testar** | Sai a página de teste no rolo; o estado fica «Pronta» | | 104, 243 |
| A2 | Venda ao balcão paga em numerário | Recibo completo (cabeçalho, linhas, IVA, total, troco), papel **cortado** no fim | | 104, 355 |
| A3 | Margens do recibo | Nada cortado à esquerda nem à direita; o QR / código legível | | 355 |
| A4 | Pagamento em numerário com «abrir gaveta» activo na impressora | A gaveta abre em cada pagamento; com M-Pesa não abre (se configurado assim) | | 105, 355 |
| A5 | Mudar o papel da impressora para 58 mm e repetir A2 | Recibo ajustado a 58 mm, sem colunas partidas | | 226 |
| A6 | Relatório A4 a partir do posto com a térmica como predefinida | O A4 **não** sai no rolo (é pedido outro destino ou PDF) | | 319 |
| A7 | Pedido à mesa com produtos de cozinha e de bar | Cada talão sai na impressora da sua estação, cortado | | 104 |
| A8 | Desligar a impressora de cozinha e enviar um pedido | A venda continua; o estado da impressora mostra o erro; ao ligar, o talão sai (até 30 min) | | 243 |
| A9 | Impressão directa sem janela (posto com «Imprimir directamente») | O recibo sai sem abrir a janela de impressão | | 236 |
| A10 | Cópias configuradas (ex.: 2 cópias do recibo) | Saem as cópias pedidas | | 259 |

## B. Etiquetas e balanças
| # | Passo | Resultado esperado | ✔ / ✖ | Tarefa* |
|---|---|---|---|---|
| B1 | Imprimir etiquetas de preço 30 × 20 mm | Texto legível, sem cortes, uma etiqueta por rótulo | | 297 |
| B2 | Etiqueta da balança etiquetadora (EAN-13 com peso) passada no leitor do POS | Entra o produto certo com o peso da etiqueta | | 455 |
| B3 | Balança ligada ao posto: produto «pede a quantidade» → **Usar peso** | O peso da balança aparece no diálogo e no visor do cliente | | 455 |
| B4 | Anotar o modo de leitura da balança que funcionou (protocolo / porta COM / velocidade) | — | | 455 |

## C. Leitores
| # | Passo | Resultado esperado | ✔ / ✖ | Tarefa* |
|---|---|---|---|---|
| C1 | Leitor de código de barras no POS (fora de campos de texto) | O produto entra; duas leituras seguidas somam 2 | | 259 |
| C2 | Ler um código no campo de pesquisa | Entra o produto e o campo fica limpo | | 259 |
| C3 | Leitor de cartões magnéticos: Cartão Evento no pagamento | O cartão é reconhecido e mostra o saldo | | 307 |
| C4 | App LAMGest Venda: Cartões → Registar cartão → «Câmara» (código impresso) e «NFC» (cartão com chip encostado às costas do telemóvel) | O número entra no campo; um cartão já registado aparece com o saldo e «Abrir cartão»; o mesmo cartão paga no POS da app pela mesma leitura | | |
| C5 | Leitor de banda magnética USB-OTG ou Bluetooth (modo teclado) ligado ao telemóvel | Com o campo «Número do cartão» seleccionado, passar o cartão escreve o número | | |
| C6 | Clientes → ficha de um cliente → Adiantamentos: «Cartão novo» com 1000 MT; pagar um pedido no POS com esse cartão; «Transferir saldo» para outro cartão (cartão perdido) | O pedido fica com o cliente; o extrato mostra recarga, consumo e transferência com o saldo certo; o cartão antigo fica bloqueado; Relatórios → Adiantamentos mostra o cliente; «Descarregar todos os saldos» deixa o cartão do cliente de fora | | |

## D. Visor do cliente e monitores
| # | Passo | Resultado esperado | ✔ / ✖ | Tarefa* |
|---|---|---|---|---|
| D1 | Visor de poste ESC/POS (Configurações → Postos → Visor) | Mostra artigo e total durante a venda e o troco no fim | | 440 |
| D2 | Visor de poste CD5220 (se houver) | Igual a D1 | | 440 |
| D3 | Segundo monitor como visor do cliente | Venda, troco e agradecimento no monitor do cliente | | 440 |
| D4 | Monitor táctil de 15″: entrada táctil, POS, pagamento | Botões tocáveis sem erros; anotar o tamanho do sistema que ficou melhor | | 447 |
| D5 | Documento em PDF (modo «Abrir em PDF») no posto | O PDF aparece dentro do visualizador | | 161 |
| D6 | Posto com o programa LAMGest (sem navegador): venda paga em numerário | O recibo sai **sem janela** na impressora predefinida, com o logótipo e cortado; repetir com papel de 58 mm | | 563 |
| D7 | No programa: Configurações → Postos → Visor → **Abrir** | O visor abre noutra janela, em ecrã inteiro no segundo monitor | | 563 |
| D8 | No programa: desligar o cabo de rede do posto e voltar a ligar | «Sem ligação ao servidor»; ao voltar a rede, o sistema reaparece sozinho (até 5 s) | | 563 |

## E. PDA Sunmi (app LAMGest Venda)
| # | Passo | Resultado esperado | ✔ / ✖ | Tarefa* |
|---|---|---|---|---|
| E1 | Instalar a APK nova pelo QR «Instalar a app de venda» e emparelhar pelo QR do posto (Configurações → Postos) | Liga ao posto; em servidores com HTTPS passa a `https://` | | 333, 532 |
| E2 | Venda à mesa no PDA e recibo na impressora do PDA | Pedido criado; recibo impresso no PDA | | 333 |
| E3 | Versão do WebView (Definições → Apps → Android System WebView) | 70 ou mais | | 333 |
| E4 | Desligar o Wi-Fi do restaurante | A app mostra «Não foi possível ligar…» e não grava nada | | 339 |
| E5 | Dados móveis com o Tailscale ligado | «Ligado pela VPN»; venda funciona | | 406 |
| E6 | Telemóvel de gestor com duas lojas na VPN | «Mudar de loja» troca de servidor | | 413 |

## F. Rede, HTTPS e servidor
| # | Passo | Resultado esperado | ✔ / ✖ | Tarefa* |
|---|---|---|---|---|
| F1 | Em cada posto, o atalho abre `https://IP:8443/...` | Cadeado sem aviso de segurança | | 538 |
| F2 | Configurações → Sistema | «Ligação cifrada (HTTPS) activa»; sem avisos de certificado | | 538 |
| F3 | Depois de todos os postos e PDA em HTTPS: Setup no servidor com «Só HTTPS» | `http://IP:8080` passa para HTTPS; postos e PDA continuam a vender | | 538 |
| F4 | Agendador de Tarefas → «LAMGest Restaurant - Backup SQL» → Executar | Resultado 0x0; Configurações → Sistema mostra a cópia de hoje | | 517 |
| F5 | Produtos → filtro «Activos» → Exportar → Excel e PDF; Configurações → Sistema → Limpeza e espaço → «Trabalhos de impressão» → Limpar | O Excel e o PDF têm todos os produtos activos (não só a página); a limpeza mostra quanto apaga antes de confirmar e fica na Auditoria | | |

* Linha da tarefa em `docs/TAREFAS.md` (numeração de 22/11/2026; procure pelo texto se o ficheiro tiver mudado).

## Fecho da visita
- Passar cada ✔ / ✖ para `docs/TAREFAS.md` (marcar `[x]` o que passou; nos ✖, a mensagem e o que se viu).
- Guardar os relatórios do `verificar-instalacao.ps1`, as fotografias dos talões e as marcas e modelos do equipamento.
- Se for uma instalação que já vende a sério: Configurações → Sistema → «Iniciar produção mantendo os dados» (tarefa 361).
