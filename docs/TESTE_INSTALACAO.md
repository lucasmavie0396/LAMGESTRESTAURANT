# Teste do Setup num computador limpo

Objectivo: confirmar que `LAMGest-Setup.exe` instala o servidor num computador **sem nada do LAMGest**, antes de o levar para um cliente. Use uma máquina virtual descartável (Hyper-V, VirtualBox, VMware ou uma VM na nuvem) ou um PC de testes — **nunca o servidor de produção**.

**Máquina:** Windows 10/11 Pro ou Windows Server 2019+, 64 bits, 4 GB de RAM, 40 GB de disco, rede em modo «Privada». Anote cada resultado (✔/✖) e, se falhar, a mensagem exacta e o ficheiro `C:\ProgramData\LAMGestRestaurant\logs\lamgest-AAAAMMDD.log`.

## A. Sem SQL Server (verifica os avisos)
| # | Passo | Resultado esperado |
|---|---|---|
| A1 | Executar `LAMGest-Setup.exe` (duplo clique) | Pede elevação (UAC). Se o SmartScreen avisar: *Mais informações → Executar mesmo assim*. Título mostra a versão |
| A2 | Escolher **Servidor central** → Seguinte | Página SQL Server com aviso vermelho «Não encontrei o SQL Server…» e a ligação para o SQL Server Express |
| A3 | Carregar na ligação | Abre a página de transferência do SQL Server |
| A4 | **Testar ligação** com `.\SQLEXPRESS` | «Não consegui ligar a «.\SQLEXPRESS»…» (sem erro técnico em inglês) |

## B. Instalar o SQL Server sem fechar o assistente
| # | Passo | Resultado esperado |
|---|---|---|
| B1 | Instalar o SQL Server Express, instalação **Básica** | Instância `SQLEXPRESS` criada |
| B2 | No assistente: **Procurar de novo** | O aviso desaparece; `.\SQLEXPRESS` na lista |
| B3 | Parar o serviço (`services.msc` → SQL Server (SQLEXPRESS) → Parar) e **Procurar de novo** | Aviso «O serviço do SQL Server … está parado» |
| B4 | Iniciar o serviço, **Procurar de novo**, **Testar ligação** (Windows) | ✔ «SQL Server 16… — a base não existe e vai ser criada» |

## C. Aplicação e sqlcmd
| # | Passo | Resultado esperado |
|---|---|---|
| C1 | Seguinte → página Aplicação (sem sqlcmd instalado) | Linha vermelha «A cópia de segurança diária precisa do sqlcmd…» |
| C2 | Seguinte com a cópia diária ligada | Recusa com a mesma mensagem |
| C3 | Numa consola de administrador: `winget install Microsoft.Sqlcmd`; voltar à página (Voltar → Seguinte) | Linha cinzenta «sqlcmd: C:\…\sqlcmd.exe» |
| C4 | Porta 8080 (ou outra), firewall ligada, cópia diária ligada → Seguinte | Avança para o administrador |

## D. Instalar
| # | Passo | Resultado esperado |
|---|---|---|
| D1 | Administrador `admin`, nome, palavra-passe forte (2×) → Seguinte → **Instalar** | Passos a verde até ao fim; mostra `http://IP:8080` |
| D2 | `Get-Service LAMGestRestaurant` | `Running`, arranque automático |
| D3 | Abrir `http://localhost:8080/api/saude` | `"baseDados":true`, `"migracoesPendentes":false` |
| D4 | Outro PC da rede: `http://IP-DA-VM:8080` | Abre o ecrã de entrada (aviso vermelho de licença) |
| D5 | Entrar com o administrador | Entra; pede para mudar a palavra-passe se for o caso |
| D6 | Configurações → Licença → activar uma licença de teste | «Válida»; o aviso vermelho desaparece |
| D7 | Agendador de Tarefas → «LAMGest Restaurant - Backup SQL» → Executar | Resultado 0x0; `.bak` novo na pasta de backups da instância; log `backup-AAAAMM.log` «Concluído com sucesso» |
| D8 | Reiniciar a VM | O serviço arranca sozinho e a sessão no navegador mantém-se |

## E. Actualização e posto
| # | Passo | Resultado esperado |
|---|---|---|
| E1 | Fazer uma venda de teste no POS (abrir caixa, Take Away, pagar) | Venda a Dinheiro `VD-…` |
| E2 | Executar o mesmo Setup outra vez, modo Servidor, mesma base | Mostra «base do LAMGest actualizada (1 pedidos)»; instala sem pedir administrador; a venda continua lá |
| E3 | Noutro PC com Chrome: Setup → **Posto de trabalho**, IP da VM, posto 1 | «Testar ligação e posto» ✔; atalho no ambiente de trabalho abre o POS ligado ao posto |

## F. Desinstalar (limpeza)
Siga [INSTALACAO_LAN.md § 10](INSTALACAO_LAN.md#10-desinstalar) ou apague a VM.
