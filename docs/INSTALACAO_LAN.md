# Instalação na rede local (LAN)

Arquitectura: **um servidor central** (Windows) corre o SQL Server e o serviço LAMGest Restaurant, que serve a aplicação e a API na mesma porta. Os **postos** (caixas, tablets dos garçons, ecrã da cozinha, gerência) só precisam do Google Chrome e abrem `http://IP-DO-SERVIDOR:8080`.

```text
 Postos (Chrome) ──LAN──►  Servidor central
   caixa, cozinha,          ├─ serviço "LAMGestRestaurant" (porta 8080: aplicação + API + tempo real)
   garçons, gerência        └─ SQL Server (só acessível localmente pelo serviço)
```

Antes de instalar num cliente, teste cada versão nova do Setup num computador limpo com a lista [TESTE_INSTALACAO.md](TESTE_INSTALACAO.md).

Tudo se instala com **um único ficheiro, `LAMGest-Setup.exe`** (gerado com `scripts\criar-setup.ps1`, ver [SETUP.md](SETUP.md)). Não é preciso instalar o .NET em nenhum computador.

## 1. Requisitos do servidor
| | |
|---|---|
| Sistema | Windows 10/11 Pro ou Windows Server, **64 bits**; conta de **administrador** |
| Hardware | 4 GB de RAM (8 GB recomendado), 2 núcleos, **10 GB livres** (SQL Server + aplicação + cópias de segurança) |
| Rede | ligado por cabo; **IP fixo** (reserva DHCP no router ou IP manual) — os postos usam sempre esse endereço |
| Energia | suspensão/hibernação desligadas (*Definições → Sistema → Energia*); UPS recomendada |
| **SQL Server** | **2016 ou mais recente** (2016, 2017, 2019, 2022, 2025); o **Express** (gratuito) serve. O Setup **não** o instala — ver o passo 2 |
| **sqlcmd** | usado pelas cópias de segurança diárias; o Setup diz se falta (passo 2) |

O Setup verifica estes pontos antes de instalar e **explica o que falta** em vez de falhar a meio:
- sem SQL Server neste computador → mensagem com a ligação para o SQL Server Express e o botão **Procurar de novo** (para o encontrar depois de o instalar, sem fechar o assistente);
- serviço do SQL Server parado → como o iniciar em `services.msc`;
- instância errada ou inacessível → mensagem clara (nome da instância e serviço em execução);
- cópia diária escolhida sem `sqlcmd` → como o instalar (ou desligue a cópia diária);
- menos de 1 GB livre no disco da instalação, ou porta já usada por outro programa → pede outra pasta/porta.

## 2. Preparar o servidor (uma vez)
1. **IP fixo**: reserve o IP no router (ou configure-o em *Definições de rede*). Marque a rede do restaurante como **Privada**.
2. **SQL Server Express**: descarregue de <https://www.microsoft.com/pt-pt/sql-server/sql-server-downloads> → *Express* → **Básica**. A instância fica `.\SQLEXPRESS` com autenticação Windows — é o que o Setup propõe.
3. **sqlcmd** (se o Setup disser que falta), numa consola de administrador:
   ```powershell
   winget install Microsoft.Sqlcmd
   ```
   (ou o pacote «Microsoft Command Line Utilities for SQL Server»). A cópia de segurança encontra-o no PATH ou nas pastas onde é instalado.
4. **Impressoras**: as do servidor instalam-se no Windows do servidor **para todos os utilizadores**; as ligadas a um posto (ex.: térmica USB do caixa) instalam-se nesse posto e são usadas pelo **agente de impressão** que o Setup instala em modo Posto (ver [SETUP.md](SETUP.md#impressoras-caixa-bar-cozinha)).

O SQL Server **não** precisa de estar aberto na rede: só o serviço local se liga a ele. Não abra a porta 1433 na firewall.

## 3. Instalar o servidor com o Setup
1. Copie `LAMGest-Setup.exe` para o servidor (pen USB ou rede) e execute-o **como administrador**. Como não tem assinatura digital, o Windows SmartScreen pode avisar: *Mais informações → Executar mesmo assim*.
2. Escolha **Servidor central** e siga as páginas (detalhe em [SETUP.md](SETUP.md#1-servidor-central)):
   - **SQL Server**: instância (ex.: `.\SQLEXPRESS`), autenticação Windows (ou `sa` + palavra-passe) e a base `LAMGestRestaurantDB`; **Testar ligação**;
   - **Aplicação**: pasta, porta (8080), firewall e cópia de segurança diária;
   - **Primeiro administrador**: utilizador e palavra-passe (não ficam gravados em ficheiro);
   - **Resumo → Instalar**.
3. No fim, o Setup mostra os endereços para os postos (ex.: `http://192.168.1.10:8080`) e confirma que o sistema responde.

O que fica configurado: serviço `LAMGestRestaurant` (arranque automático, conta virtual própria), acesso à base só de leitura/escrita de dados, configuração protegida em `C:\ProgramData\LAMGestRestaurant\config`, regra da firewall (redes privadas/domínio) e a tarefa «LAMGest Restaurant - Backup SQL». Tabela completa em [SETUP.md](SETUP.md#1-servidor-central).

## 4. Licença
Abra `http://localhost:8080` no servidor, entre com o administrador e vá a **Configurações → Licença** → **Carregar ficheiro .lic** (ou cole o texto `LAMGEST1.…`) → **Activar licença**. Sem licença o sistema abre, mas as vendas e gravações ficam bloqueadas. O NUIT e o nome comercial passam a ser os da licença.

## 5. Postos de trabalho
1. No sistema, em **Caixa → Terminais**, crie um registo por computador com o **N.º posto** (1, 2, 3…).
2. Em cada posto instale o **Google Chrome**, execute `LAMGest-Setup.exe` como administrador e escolha **Posto de trabalho**: IP e porta do servidor, número do posto, o ecrã a abrir (POS, Cozinha…) → **Instalar**. Fica um atalho que abre o sistema já ligado a esse posto ([SETUP.md](SETUP.md#3-postos-de-trabalho)).
3. A barra superior mostra «Ligado ao servidor» e «Posto N · nome». Se o servidor ou a base falharem, as gravações ficam bloqueadas e aparece uma faixa vermelha (não há modo offline).

## 6. Verificação final
Siga a lista [PRODUCAO.md](PRODUCAO.md) antes de usar com dinheiro real. Comece por correr, como administrador, a verificação automática (só lê, não altera nada) no servidor e em cada posto:
```powershell
powershell -ExecutionPolicy Bypass -File "C:\Program Files\LAMGestRestaurant\scripts\verificar-instalacao.ps1"
```
Mostra OK / AVISO / FALHA para o serviço, HTTP e HTTPS, certificado, firewall, cópias de segurança, posto e agente de impressão. No mínimo:
- `http://IP-DO-SERVIDOR:8080/api/saude` num posto responde com `"baseDados":true` e `"migracoesPendentes":false`;
- uma venda de teste no POS sai como **Venda a Dinheiro** e o caixa fecha;
- a tarefa de cópia corre: *Agendador de Tarefas → «LAMGest Restaurant - Backup SQL» → Executar*; o log fica em `C:\ProgramData\LAMGestRestaurant\logs\backup-AAAAMM.log`. Teste também um restauro ([BACKUP.md](BACKUP.md)).

## 7. Actualizar para uma nova versão
Gere o Setup novo e execute-o **no servidor**, em modo Servidor, com a mesma instância e base. O Setup pára o serviço, **faz uma cópia de segurança**, mostra as actualizações da base a aplicar (pede confirmação se houver dados), actualiza a aplicação e volta a arrancar o serviço. Dados, sessões, chaves e logs mantêm-se. Recusa bases actualizadas por uma versão mais recente.

## 8. Mudar de servidor
1. No servidor antigo: cópia de segurança (tarefa de backup ou `scripts\backup-sql.ps1`) e copie o `.bak` para o novo.
2. No servidor novo: passos 1–2, restaure a cópia com o nome `LAMGestRestaurantDB` (SQL Server Management Studio → *Restaurar base de dados*, ou `RESTORE DATABASE ... WITH MOVE`) e **depois** corra o Setup em modo Servidor apontando para essa base — recria o serviço e o acesso da base.
3. Mude o IP dos postos (ou dê ao servidor novo o IP do antigo) e volte a correr o Setup em modo Posto onde for preciso.
4. A licença está na base: continua activa (o NUIT é o mesmo).

## 9. HTTPS (ligação cifrada)
Em HTTP simples, quem estiver na mesma rede pode ver as palavras-passe e os códigos. O Setup liga o HTTPS por omissão (página **Aplicação** → «Ligação cifrada (HTTPS) na porta 8443»), sem depender da internet:
1. **Servidor.** O Setup cria uma **autoridade de certificação própria deste servidor** («LAMGest Restaurant - autoridade local»; a chave fica em `C:\ProgramData\LAMGestRestaurant\certificados`, só para administradores) e um certificado para o nome do computador e todos os endereços IP (incluindo o da VPN Tailscale, se estiver ligada). O serviço atende em **HTTPS na porta 8443** e continua em **HTTP na porta 8080**, para os postos e tablets ainda não actualizados. No fim, o Setup mostra a **impressão digital** da autoridade.
2. **Postos.** Volte a correr o Setup em modo Posto (ou o LAMGest-Configurar): o posto descarrega a autoridade do servidor, mostra a impressão digital (compare com a do servidor), passa a confiar nela e os atalhos e o agente de impressão passam a `https://IP:8443`. A palavra-passe do administrador usada para registar o agente já vai cifrada.
3. **Tablets (app LAMGest Venda).** No próximo arranque no Wi-Fi do restaurante, a app guarda a autoridade da loja e passa sozinha para HTTPS; só aceita certificados assinados por ela. Se o servidor for reinstalado do zero (autoridade nova), emparelhe o tablet de novo em «Ligação ao servidor».
4. **Só HTTPS.** Quando todos os postos e tablets estiverem em HTTPS, volte a correr o Setup no servidor e marque «Só HTTPS»: o HTTP passa a reencaminhar para o HTTPS e o cookie de sessão deixa de passar sem cifra. Continuam em HTTP só `/api/saude` e `/api/sistema/certificado` (é por aí que um posto ou tablet novo obtém a autoridade).
5. **Outros aparelhos** (ex.: um portátil da gerência sem o Setup): Configurações → Sistema → «Descarregar a autoridade local (.cer)» e importe-a em *Autoridades de Certificação de Raiz Fidedigna*.

Configurações → Sistema mostra se o HTTPS está activo e até quando vale o certificado (3 anos). Avisa 60 dias antes do fim e quando o servidor muda de endereço IP (o certificado deixa de servir para esse endereço): em ambos os casos, volte a correr o Setup no servidor — a autoridade mantém-se e os postos não precisam de ser reinstalados. Recomenda-se um **IP fixo** para o servidor (reserva no router).

## 10. Desinstalar
Execute `LAMGest-Setup.exe` e escolha **Remover o LAMGest deste computador** (pede o código de instalação). O assistente mostra o que encontrou e, depois de confirmar, retira:
- **servidor:** o serviço, a tarefa das cópias, as regras da firewall e a pasta da aplicação;
- **posto:** os atalhos, o agente de impressão (e a sua chave), o configurador, o registo do posto e o perfil do navegador;
- a autoridade local do HTTPS das raízes fidedignas do computador.

**Nunca** apaga a base de dados nem as cópias de segurança. A configuração, as chaves e os logs (`C:\ProgramData\LAMGestRestaurant`) também ficam: uma reinstalação volta a usar a mesma base e a mesma autoridade local. Para retirar um posto do servidor, use também Caixa → Terminais → «Desligar o agente de impressão».

## Resolução de problemas
| Sintoma | Verificar |
|---|---|
| Setup: «Não encontrei o SQL Server neste computador» | Instale o SQL Server Express (passo 2.2) e carregue em **Procurar de novo**. |
| Setup: «O serviço do SQL Server … está parado» | `services.msc` → *SQL Server (SQLEXPRESS)* → Iniciar (tipo de arranque: Automático). |
| Setup: «Não consegui ligar a …» | Nome da instância (ex.: `.\SQLEXPRESS`, não `SQLEXPRESS`); serviço em execução; com `sa`, o SQL Server tem de aceitar autenticação mista. |
| Setup: «A cópia de segurança diária precisa do sqlcmd» | `winget install Microsoft.Sqlcmd` (passo 2.3) e **Seguinte**, ou desligue a cópia diária. |
| Postos não abrem a página | `Test-NetConnection IP-DO-SERVIDOR -Port 8080` no posto; rede do servidor marcada como *Privada*; `Get-Service LAMGestRestaurant` em execução. |
| «Sem ligação à base de dados» | Serviço do SQL Server em execução; login do serviço existe na instância (volte a correr o Setup). |
| «Migrations pendentes» | Corra o Setup da versão instalada em modo Servidor (aplica as actualizações com cópia antes). |
| Sistema bloqueado / «Sistema sem licença» | Active a licença (passo 4). |
| Erros inesperados | Logs em `C:\ProgramData\LAMGestRestaurant\logs\lamgest-AAAAMMDD.log` (30 dias). |

## Anexo: instalação manual (sem o Setup)
Para casos especiais (ex.: servidor sem interface gráfica): `scripts\publicar.ps1` gera `publicar\LAMGestRestaurant` (aplicação, `migracoes.sql`, scripts); no servidor, crie a base e aplique `migracoes.sql` com `sqlcmd`, corra `scripts\instalar-servico.ps1 -Porta 8080 -Servidor ".\SQLEXPRESS" -BaseDados LAMGestRestaurantDB` como administrador, crie o primeiro administrador com `LAMGestRestaurant.Api.exe bootstrap-admin` (com `ConnectionStrings__Default` definida na consola) e registe a cópia diária com `scripts\registar-backup.ps1`. Este pacote **não** é *self-contained*: o servidor precisa do **ASP.NET Core Runtime 9 (Hosting Bundle)**, ao contrário do Setup.
