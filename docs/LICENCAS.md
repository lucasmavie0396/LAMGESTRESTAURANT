# Licenças e cobrança (LAM Company)

Só para o fabricante. O `LAMGest-Licencas.exe` não vai no Setup dos clientes.

## Como funciona

- **Cada cliente** (um NUIT) recebe licenças **mensais** (ou do período que pagou). A licença é assinada pelo gerador e o
  sistema do cliente verifica-a **sem internet**, de 5 em 5 minutos.
- **Pagamento → licença.** Ao registar um pagamento, o gerador emite logo a licença até ao fim do período pago e copia a
  mensagem pronta para WhatsApp ou e-mail.
- **Sem pagamento, não há renovação.** A licença expira na data; o sistema do cliente avisa nos últimos 5 dias (15 nas licenças
  de mais de 45 dias), dá 7 dias de tolerância e depois **bloqueia as vendas e gravações**. A consulta e os relatórios
  continuam (os dados são do cliente).
- **Suspender** um cliente impede o gerador de lhe emitir licenças. Uma licença já emitida **não se retira**: vale até à data
  dela. Por isso as licenças devem ser curtas (mensais).

## Onde ficam os dados (no computador do fabricante)

| O quê | Onde |
|---|---|
| Chave de assinatura (protegida pelo Windows) | `%APPDATA%\LAMGest\Licencas\chave-privada.dat` |
| Ligação à base (instância, nome e, com utilizador do SQL Server, a palavra-passe cifrada pelo Windows) | `%APPDATA%\LAMGest\Licencas\base.json` |
| Registo antigo das licenças (continua a ser escrito, como cópia) | `%APPDATA%\LAMGest\Licencas\registo.json` |
| Clientes, pagamentos, licenças e histórico | Base SQL Server `LAMGestLicencas` (autenticação do Windows) |

Nada disto vai para o repositório. Nunca use a base de um sistema de cliente nem a de outro programa.

## Primeira utilização

1. Instale o SQL Server Express neste computador (se ainda não tiver).
2. Abra o `LAMGest-Licencas.exe`. Sem chave, reponha a cópia de segurança da chave (`.pem`).
3. Escolha a instância do SQL Server (a lista mostra as instaladas neste computador) e como entrar:
   - **Autenticação do Windows**, se a sua conta puder criar bases no SQL Server;
   - **Utilizador do SQL Server** (ex.: `sa`) — necessário quando aparece «CREATE DATABASE permission denied». A
     palavra-passe fica guardada cifrada pelo Windows, só para a sua conta (`base.json`); o SQL Server tem de aceitar
     autenticação mista.

   Indique o nome da base (`LAMGestLicencas`) e carregue em **Ligar**: a base é criada. Para mudar depois: menu Base de
   dados → «Ligar a outra instância ou base…».
4. As licenças do registo antigo são importadas (um cliente por NUIT, plano anual, valor 0): **preencha em cada cliente o
   plano e o valor por mês**.

## No dia-a-dia

- **Novo cliente:** «Novo cliente» → nome, NUIT, contacto, plano, valor por mês → «Guardar cliente».
- **Pagamento:** seleccione o cliente → Pagamentos → «Registar pagamento…» (data, meses, valor, referência M-Pesa/banco).
  A janela mostra até quando fica a licença. Ao confirmar, a licença é emitida e a **mensagem fica copiada**: cole-a no
  WhatsApp ou no e-mail do cliente. O cliente activa-a em Configurações → Licença.
  - Pago antes do fim, ou até 7 dias depois (tolerância), o período continua do fim do anterior.
  - Pago depois de o sistema bloquear, conta a partir da data do pagamento.
  - Um pagamento antigo cujo período já terminou fica só registado (sem licença).
- **Falta de pagamento:** a lista mostra a vermelho os clientes em atraso e a laranja os que vencem em 7 dias (filtros no
  topo). «Suspender» (com o motivo) impede novas licenças; «Reactivar» quando regularizar.
- **Pagamento errado:** «Anular pagamento…» (com o motivo). Fica marcado como anulado; nada se apaga. A licença já emitida com
  ele continua a valer no cliente até à data dela.
- **Teste, oferta ou contrato pago por fora:** Licenças → «Emitir sem pagamento…» (com o motivo).
- **Histórico:** cada acção fica registada com a data e o utilizador do Windows; não se altera nem se apaga.

## Cópias de segurança

- **Base:** menu Base de dados → «Cópia de segurança da base…» (ficheiro `.bak` na pasta de cópias do SQL Server ou noutra).
  Para uma cópia diária automática, crie uma tarefa no Agendador de Tarefas do Windows com:

  ```powershell
  sqlcmd -S .\SQLEXPRESS -E -Q "BACKUP DATABASE [LAMGestLicencas] TO DISK = N'D:\Copias\LAMGestLicencas.bak' WITH INIT, CHECKSUM"
  ```

- **Chave:** menu Chave → «Exportar cópia de segurança…» (`.pem` cifrado com palavra-passe).
- Guarde as duas **fora deste computador** (pen USB num cofre ou nuvem). Sem a chave não se emitem licenças que o sistema
  aceite; sem a base perde-se o registo dos pagamentos.

## Mudar de computador

1. No computador novo, instale o SQL Server Express e restaure o `.bak` (SQL Server Management Studio → Restore Database).
2. Abra o `LAMGest-Licencas.exe`, reponha a cópia da chave (`.pem`) e indique a instância e a base.

## Contrato com o cliente

Convém o contrato dizer que a licença é mensal, renovada com o pagamento, e que sem pagamento o sistema deixa de permitir
vendas e gravações (mantendo o acesso aos dados e relatórios).
