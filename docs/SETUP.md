# Instalador (LAMGest-Setup.exe)

Um único ficheiro instala o **servidor central** ou um **posto de trabalho**. Não precisa de instalar o .NET em nenhum computador: o servidor só precisa do **SQL Server** (Express serve; o Setup não o instala) e do **sqlcmd** para as cópias diárias — o assistente verifica os dois e explica o que falta (ver [INSTALACAO_LAN.md](INSTALACAO_LAN.md#1-requisitos-do-servidor)); os postos só precisam do **Google Chrome** (instale-o antes de correr o instalador no posto; sem Chrome, o instalador avisa e só usa o Edge se o confirmar).

## Gerar o instalador (máquina de desenvolvimento)
```powershell
.\scripts\criar-setup.ps1
```
Cria `publicar\LAMGest-Setup.exe` (~100 MB) com a aplicação, o frontend, os scripts de cópia de segurança e a lista de actualizações da base de dados. Copie-o para uma pen USB ou partilha de rede.

## 1. Servidor central
Execute `LAMGest-Setup.exe` como administrador e escolha **Servidor central**.

1. **SQL Server** — instância (as instâncias deste computador aparecem na lista, ex.: `.\SQLEXPRESS`), autenticação **Windows** ou **SQL Server** (utilizador `sa` e palavra-passe) e o nome da base (`LAMGestRestaurantDB`). «Testar ligação» confirma o acesso e mostra a situação da base:
   - não existe → é criada;
   - existe e é do LAMGest → é actualizada, **com cópia de segurança antes** e confirmação explícita se tiver dados;
   - existe e não é do LAMGest → o instalador recusa (só trabalha com bases `LAMGestRestaurant*`).
2. **Aplicação** — pasta (`C:\Program Files\LAMGestRestaurant`), porta (8080), firewall e cópia de segurança diária (hora e cópia adicional opcional para disco externo/rede).
3. **Primeiro administrador** — só aparece se a base ainda não tiver um. A palavra-passe não fica gravada.
4. **Resumo** → **Instalar**. No fim aparecem os endereços a usar nos postos (ex.: `http://192.168.1.10:8080`).

O que fica configurado:
| | |
|---|---|
| Serviço Windows | `LAMGestRestaurant`, arranque automático, reinício em caso de falha, conta virtual `NT SERVICE\LAMGestRestaurant` |
| Acesso à base | Windows: a conta do serviço; SQL: login `lamgest_servico` com palavra-passe aleatória. Em ambos os casos só leitura/escrita de dados (sem alterar estrutura). **A palavra-passe do `sa` não é guardada.** |
| Configuração | `C:\ProgramData\LAMGestRestaurant\config\servidor.json` (connection string), acessível só ao serviço e aos administradores |
| Sessões | chaves em `C:\ProgramData\LAMGestRestaurant\chaves` (os utilizadores não perdem a sessão quando o serviço reinicia) |
| Logs | `C:\ProgramData\LAMGestRestaurant\logs` (aplicação e cópias de segurança) |
| Firewall | regra de entrada TCP na porta escolhida, só redes privadas e de domínio |
| Cópias | tarefa «LAMGest Restaurant - Backup SQL» (SYSTEM), cópia verificada, 14 dias (ver [BACKUP.md](BACKUP.md)) |

**Actualizar:** gere um Setup novo e execute-o no servidor com os mesmos dados. O serviço é parado, a base é copiada, actualizada e o serviço volta a arrancar. Os dados, sessões e logs mantêm-se.

> Se o SQL Server estiver noutro computador, use autenticação **SQL Server** (a conta virtual do serviço só funciona com um SQL Server local).

### Activar a licença
Depois da instalação, entre com o administrador e vá a **Configurações → Licença**: cole o texto da licença (ou carregue o ficheiro `.lic`) enviado pela LAM company e carregue em **Activar licença**. Sem licença, o sistema só deixa consultar (as vendas e gravações ficam bloqueadas). Para renovar, active da mesma forma a licença nova.

## 2. Registar os postos no sistema
Entre no sistema (`http://IP-DO-SERVIDOR:8080`) com o administrador e, em **Caixa → Terminais**, crie um registo por computador com o **N.º posto** (1, 2, 3…) e um nome (ex.: «Caixa 1 — Balcão», «Cozinha»).

## Impressoras (Caixa, Bar, Cozinha)
Os documentos saem directamente, sem janelas: talões da cozinha e do bar quando o POS carrega em «Enviar», e o recibo na impressora do caixa quando o pedido fica pago. Cada impressora está **ligada a** um sítio:
- **Servidor** — instalada no Windows do servidor (passo 1);
- **Posto N** — instalada no Windows de um posto (ex.: térmica USB no caixa, impressora de rede vista por esse computador). O **agente de impressão** do posto, instalado pelo `LAMGest-Setup.exe` em modo Posto, envia ao servidor a lista das impressoras desse computador, recebe os trabalhos e imprime-os com o mesmo desenho do servidor. Cada posto gere as suas impressoras; não é preciso partilhá-las no servidor. Ver [Agente de impressão do posto](#agente-de-impressão-do-posto).

1. Impressoras do servidor: instale cada uma **no Windows do servidor, para todos os utilizadores** (o serviço corre com uma conta própria e não vê impressoras ligadas só à sessão de um utilizador):
   - impressora USB no próprio servidor: instale o driver do fabricante;
   - impressora de rede: *Adicionar impressora → Adicionar impressora local com porta TCP/IP* e o IP da impressora;
   - impressora USB ligada a um posto: partilhe-a no Windows desse posto e, no servidor, numa consola de administrador:
     ```powershell
     rundll32 printui.dll,PrintUIEntry /ga /n"\\NOME-DO-POSTO\NomeDaPartilha"
     Restart-Service Spooler
     ```
     (`/ga` adiciona a ligação para todos os utilizadores, incluindo o serviço).
2. Em **Configurações → Impressoras → Nova impressora**: nome, função (**Caixa**, **Bar** ou **Cozinha**), **Ligada a** (Servidor ou o posto), a impressora do Windows (lista das instaladas nesse computador) e o papel (**80 mm**, **58 mm** ou **A4**).
   - Caixa: opcionalmente associada a um terminal (com vários caixas, cada recibo sai na impressora do caixa onde foi pago; a impressora sem terminal serve os restantes).
   - Bar/Cozinha: opcionalmente presa a uma estação de preparação (só recebe os produtos das categorias dessa estação).
   - **Modo de impressão:** «Imprimir directo» (o servidor imprime sem janelas) ou «Abrir em PDF»: recibos, contas, talões, relatórios e a página de teste abrem em PDF (com o papel escolhido) num visualizador no ecrã de quem fez a operação, para imprimir ou guardar. Em PDF não é preciso escolher uma impressora do Windows. Os PDF ficam disponíveis 2 horas e só para esse utilizador.
3. Carregue em **Testar** (ícone da impressora): sai uma página de teste (ou abre o PDF) e a coluna «Estado» mostra o resultado (ou o erro, ex.: impressora desligada).

Encaminhamento dos talões: produtos de categorias com estação → impressora dessa estação; senão produtos do tipo **Bar** → impressoras Bar; restantes que vão à cozinha → impressoras Cozinha. Se não houver impressoras configuradas, a cozinha usa só o ecrã e o recibo imprime-se pela janela do navegador do posto. Em **Configurações → Impressoras → Recibos** pode desligar a impressão automática do recibo (fica só o botão «Imprimir recibo» no POS).

Uma impressora parada nunca bloqueia uma venda: a impressão corre em segundo plano e os erros ficam no estado da impressora e nos logs.

## 3. Postos de trabalho
Em cada computador, execute `LAMGest-Setup.exe` como administrador e escolha **Posto de trabalho**:
1. **Servidor (IP)** e **Porta** — os endereços mostrados no fim da instalação do servidor.
2. **Número do posto** — o registado em Caixa → Terminais. «Testar ligação e posto» confirma o servidor e mostra o nome do posto.
3. **Abrir em** — POS, Cozinha, Mesas, Delivery ou Início; opções de atalho no ambiente de trabalho, arranque automático com o Windows, ecrã inteiro (cozinha) e **ecrã de entrada táctil** (botões com os nomes e código num teclado no ecrã; os códigos definem-se em Utilizadores → «Código do ecrã táctil»).
4. **Impressão directa** (ligada por omissão) — o que o navegador do posto imprime (ex.: recibo sem impressora configurada) sai **sem janela** na impressora predefinida do Windows deste posto (o atalho abre o Chrome com `--kiosk-printing` e um perfil próprio em `C:\ProgramData\LAMGestRestaurant\navegador`). Desligue-a nos postos que imprimem facturas em A4 e querem escolher a impressora.
5. **Agente de impressão** (ligado por omissão) — com o utilizador e a palavra-passe de um administrador do LAMGest (a palavra-passe não fica guardada). Precisa do posto já registado em Caixa → Terminais.
6. **Instalar**.

### Agente de impressão do posto
O serviço do servidor só vê as impressoras **instaladas no próprio servidor** (não vê impressoras USB dos postos, nem as redireccionadas pelo Ambiente de Trabalho Remoto). O agente resolve isso:
1. Instale o driver da impressora **no posto** (USB, rede…), como para qualquer programa.
2. Instale o posto com **Agente de impressão** ligado (passo 5 acima). O instalador:
   - copia o agente para `C:\Program Files\LAMGestRestaurant\Agente\LAMGest-Agente.exe`;
   - regista-o no servidor (`POST /api/postos/{N}/agente`) e guarda a chave em `C:\ProgramData\LAMGestRestaurant\agente.json` (os utilizadores só a lêem; só administradores a alteram);
   - põe-no a arrancar com o Windows de qualquer utilizador e inicia-o. O ícone LAM junto ao relógio mostra o estado («Ligado ao servidor», «Sem ligação…») e as impressoras deste computador; o registo fica em `%LOCALAPPDATA%\LAMGestRestaurant\agente.log`.
3. Em **Configurações → Impressoras → Nova impressora** escolha **Ligada a: Posto N** e a impressora desse computador; para recibos, a função **Caixa** com o terminal desse posto. Carregue em **Testar**: a página de teste sai no posto e o estado (ou o erro, ex.: «sem papel») aparece na lista.

Em **Caixa → Terminais** vê-se se o agente de cada posto está ligado, a versão e as impressoras encontradas; o botão «Desligar o agente de impressão» retira a chave (ex.: computador substituído). Reinstalar o agente gera uma chave nova (a anterior deixa de valer).

Com o computador do posto desligado, os documentos desse posto ficam à espera e saem quando o agente voltar; ao fim de 30 minutos deixam de valer (falha registada no estado da impressora e aviso a quem pediu a impressão). Sem nenhuma impressora configurada para o terminal, continua a valer a impressão pelo navegador do posto (passo 4).

O instalador cria o atalho «LAMGest Restaurant» (menu Iniciar e ambiente de trabalho de todos os utilizadores) que abre o servidor numa janela própria do Google Chrome (modo aplicação) com `?posto=N`. Para passar um posto já instalado do Edge para o Chrome, instale o Chrome e volte a correr o instalador em modo Posto: os atalhos são substituídos. Ao abrir, o sistema associa o computador ao terminal com esse número e mostra «Posto N · nome» na barra superior; nos caixas, o ecrã Caixa já abre com o terminal certo. Se o número ainda não estiver registado, a barra mostra «Posto N não registado».

Para mudar o servidor, a porta ou o número, execute o instalador outra vez: os atalhos são substituídos. O registo do posto fica em `C:\ProgramData\LAMGestRestaurant\posto.json`.

## Problemas frequentes
| Sintoma | Causa provável |
|---|---|
| «esta conta não é administradora (sysadmin)» | Use `sa` ou uma conta Windows com sysadmin na instância. |
| «Login failed for user 'sa'» | Palavra-passe errada, ou a instância só aceita autenticação Windows (active o modo misto no SQL Server Management Studio). |
| No posto: «O servidor … não respondeu» | IP/porta errados, serviço parado no servidor, firewall, ou rede do servidor marcada como *Pública*. |
| «O serviço arrancou mas não responde» | Ver `C:\ProgramData\LAMGestRestaurant\logs\lamgest-AAAAMMDD.log`. |
