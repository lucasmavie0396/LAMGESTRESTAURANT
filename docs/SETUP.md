# Instalador (LAMGest-Setup.exe)

Um único ficheiro instala o **servidor central** ou um **posto de trabalho**. Não precisa de instalar o .NET em nenhum computador: o servidor só precisa do SQL Server; os postos só precisam do Edge (já vem no Windows) ou do Chrome.

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

## 2. Registar os postos no sistema
Entre no sistema (`http://IP-DO-SERVIDOR:8080`) com o administrador e, em **Caixa → Terminais**, crie um registo por computador com o **N.º posto** (1, 2, 3…) e um nome (ex.: «Caixa 1 — Balcão», «Cozinha»).

## Impressoras (Caixa, Bar, Cozinha)
O **servidor** imprime directamente, sem janelas: talões da cozinha e do bar quando o POS carrega em «Enviar», e o recibo na impressora do caixa quando o pedido fica pago.

1. Instale cada impressora **no Windows do servidor, para todos os utilizadores** (o serviço corre com uma conta própria e não vê impressoras ligadas só à sessão de um utilizador):
   - impressora USB no próprio servidor: instale o driver do fabricante;
   - impressora de rede: *Adicionar impressora → Adicionar impressora local com porta TCP/IP* e o IP da impressora;
   - impressora USB ligada a um posto: partilhe-a no Windows desse posto e, no servidor, numa consola de administrador:
     ```powershell
     rundll32 printui.dll,PrintUIEntry /ga /n"\\NOME-DO-POSTO\NomeDaPartilha"
     Restart-Service Spooler
     ```
     (`/ga` adiciona a ligação para todos os utilizadores, incluindo o serviço).
2. Em **Configurações → Impressoras → Nova impressora**: nome, função (**Caixa**, **Bar** ou **Cozinha**), a impressora do Windows (lista das instaladas no servidor) e o papel (**80 mm**, **58 mm** ou **A4**).
   - Caixa: opcionalmente associada a um terminal (com vários caixas, cada recibo sai na impressora do caixa onde foi pago; a impressora sem terminal serve os restantes).
   - Bar/Cozinha: opcionalmente presa a uma estação de preparação (só recebe os produtos das categorias dessa estação).
3. Carregue em **Testar** (ícone da impressora): sai uma página de teste e a coluna «Estado» mostra o resultado (ou o erro, ex.: impressora desligada).

Encaminhamento dos talões: produtos de categorias com estação → impressora dessa estação; senão produtos do tipo **Bar** → impressoras Bar; restantes que vão à cozinha → impressoras Cozinha. Se não houver impressoras configuradas, a cozinha usa só o ecrã e o recibo imprime-se pela janela do navegador do posto. Em **Configurações → Impressoras → Recibos** pode desligar a impressão automática do recibo (fica só o botão «Imprimir recibo» no POS).

Uma impressora parada nunca bloqueia uma venda: a impressão corre em segundo plano e os erros ficam no estado da impressora e nos logs.

## 3. Postos de trabalho
Em cada computador, execute `LAMGest-Setup.exe` como administrador e escolha **Posto de trabalho**:
1. **Servidor (IP)** e **Porta** — os endereços mostrados no fim da instalação do servidor.
2. **Número do posto** — o registado em Caixa → Terminais. «Testar ligação e posto» confirma o servidor e mostra o nome do posto.
3. **Abrir em** — POS, Cozinha, Mesas, Delivery ou Início; opções de atalho no ambiente de trabalho, arranque automático com o Windows e ecrã inteiro (cozinha).
4. **Instalar**.

O instalador cria o atalho «LAMGest Restaurant» (menu Iniciar e ambiente de trabalho de todos os utilizadores) que abre o servidor numa janela própria do Edge/Chrome com `?posto=N`. Ao abrir, o sistema associa o computador ao terminal com esse número e mostra «Posto N · nome» na barra superior; nos caixas, o ecrã Caixa já abre com o terminal certo. Se o número ainda não estiver registado, a barra mostra «Posto N não registado».

Para mudar o servidor, a porta ou o número, execute o instalador outra vez: os atalhos são substituídos. O registo do posto fica em `C:\ProgramData\LAMGestRestaurant\posto.json`.

## Problemas frequentes
| Sintoma | Causa provável |
|---|---|
| «esta conta não é administradora (sysadmin)» | Use `sa` ou uma conta Windows com sysadmin na instância. |
| «Login failed for user 'sa'» | Palavra-passe errada, ou a instância só aceita autenticação Windows (active o modo misto no SQL Server Management Studio). |
| No posto: «O servidor … não respondeu» | IP/porta errados, serviço parado no servidor, firewall, ou rede do servidor marcada como *Pública*. |
| «O serviço arrancou mas não responde» | Ver `C:\ProgramData\LAMGestRestaurant\logs\lamgest-AAAAMMDD.log`. |
