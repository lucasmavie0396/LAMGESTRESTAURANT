# Cópias de segurança e recuperação

O SQL Server **Express não tem SQL Server Agent**, por isso as cópias são feitas por um script PowerShell agendado no **Agendador de Tarefas do Windows**. Toda a informação do LAMGest está na base `LAMGestRestaurantDB`; a única outra coisa a guardar é a pasta de chaves das sessões (opcional: sem ela os utilizadores só têm de voltar a entrar).

## O que o script faz
`scripts\backup-sql.ps1`:
1. cópia **completa** (`BACKUP DATABASE ... WITH CHECKSUM`) para um ficheiro novo `LAMGestRestaurantDB_AAAAMMDD_HHMMSS.bak`;
2. **verifica** o ficheiro (`RESTORE VERIFYONLY`);
3. copia-o para uma segunda localização (`-CopiaPara`: disco externo ou partilha de rede);
4. apaga as cópias deste script com mais de `-DiasRetencao` dias (14 por omissão) — nunca outros ficheiros;
5. regista tudo em `C:\ProgramData\LAMGestRestaurant\logs\backup-AAAAMM.log` e termina com erro se algum passo falhar.

O script recusa-se a trabalhar com bases que não sejam `LAMGestRestaurant*` (a instância pode ter bases de outros sistemas).

A base usa o modelo de recuperação **SIMPLE**: recupera-se até à última cópia completa. Se perder um dia de vendas for inaceitável, agende também uma cópia a meio do dia (ver abaixo).

## Activar a cópia diária
PowerShell **como Administrador**, na pasta do pacote:
```powershell
.\scripts\registar-backup.ps1 -Hora 23:30 -CopiaPara "E:\Backups\LAMGest"
Start-ScheduledTask -TaskName "LAMGest Restaurant - Backup SQL"
```
Confirme no log que aparece «Concluído com sucesso». Para uma segunda cópia diária, registe outra tarefa com outro nome: `-Hora 15:00 -NomeTarefa "LAMGest Restaurant - Backup SQL 15h"`.

Regras práticas:
- A pasta principal é a pasta de backups da instância (`...\MSSQL\Backup`), onde o serviço do SQL Server já pode escrever.
- A segunda cópia deve sair do servidor (disco USB que se troca semanalmente, NAS ou partilha). Uma cópia no mesmo disco não protege contra avaria do disco, roubo ou ransomware.
- Leve uma cópia para fora do restaurante pelo menos uma vez por semana.

## Testar a recuperação (mensalmente)
Uma cópia que nunca foi restaurada não está provada. Sem tocar na base de produção:
```powershell
.\scripts\testar-restauro.ps1 -Ficheiro "C:\Program Files\Microsoft SQL Server\MSSQL16.SYSON\MSSQL\Backup\LAMGestRestaurantDB_20261103_233000.bak"
```
Restaura para `LAMGestRestaurantDB_Restauro`, mostra contagens de controlo (pedidos, recibos, sessões de caixa, data do último pedido) e elimina a base temporária.

## Recuperar a produção a partir de uma cópia
Só em caso de perda ou corrupção da base. Todas as vendas posteriores à cópia perdem-se.
1. Pare o serviço: `Stop-Service LAMGestRestaurant`.
2. Guarde a base actual, se ainda existir, por precaução:
   ```powershell
   .\scripts\backup-sql.ps1 -CopiaPara "E:\Backups\LAMGest\antes-do-restauro"
   ```
3. Restaure (substitua o nome do ficheiro):
   ```powershell
   sqlcmd -S .\SYSON -E -b -Q "ALTER DATABASE [LAMGestRestaurantDB] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; RESTORE DATABASE [LAMGestRestaurantDB] FROM DISK = N'C:\...\LAMGestRestaurantDB_20261103_233000.bak' WITH REPLACE, CHECKSUM; ALTER DATABASE [LAMGestRestaurantDB] SET MULTI_USER"
   ```
4. Se a cópia for de uma versão anterior da aplicação, aplique o `migracoes.sql` da versão instalada.
5. Arranque o serviço (`Start-Service LAMGestRestaurant`) e confirme `http://localhost:8080/api/saude`.
6. Em **Caixa**, verifique as sessões abertas e feche-as com contagem real: os movimentos posteriores à cópia não existem na base restaurada.

Num servidor novo: instale como em [INSTALACAO_LAN.md](INSTALACAO_LAN.md) passos 1–2, restaure a cópia com `RESTORE DATABASE ... WITH MOVE` (ou crie a base vazia e restaure com `REPLACE`) e só depois corra `instalar-servico.ps1`, que recria o login do serviço.
