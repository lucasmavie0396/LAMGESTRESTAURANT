# Instalação na rede local (LAN)

Arquitectura: **um servidor central** (Windows) corre o SQL Server e o serviço LAMGest Restaurant, que serve a API e a aplicação web na mesma porta. Os **terminais** (caixas, tablets dos garçons, ecrã da cozinha, gerência) só precisam de um navegador (Chrome ou Edge) e abrem `http://IP-DO-SERVIDOR:8080`. Não há instalação nos terminais.

```text
 Terminais (Chrome/Edge) ──LAN──►  Servidor central
   caixa, cozinha, garçons          ├─ serviço "LAMGestRestaurant" (porta 8080: aplicação + API + tempo real)
                                    └─ SQL Server (só acessível localmente pelo serviço)
```

## 1. Preparar o servidor
1. Windows 10/11 Pro ou Windows Server, ligado por cabo ao router/switch.
2. **IP fixo**: reserve o IP do servidor no router (reserva DHCP) ou configure-o manualmente. Os terminais usam sempre esse endereço.
3. Energia: desactive a suspensão/hibernação (*Definições → Sistema → Energia*). Idealmente com UPS.
4. Instale:
   - **SQL Server 2019+** (Express serve) com autenticação Windows;
   - **ASP.NET Core Runtime 9 — Hosting Bundle** (dotnet.microsoft.com → .NET 9 → ASP.NET Core Runtime → Windows Hosting Bundle);
   - **sqlcmd** (vem com o SQL Server; se faltar: `winget install Microsoft.Sqlcmd`).
5. O SQL Server **não** precisa de estar aberto na rede: só o serviço local se liga a ele. Não abra a porta 1433 na firewall.

## 2. Gerar o pacote (máquina de desenvolvimento)
```powershell
.\scripts\publicar.ps1
```
Cria `publicar\LAMGestRestaurant` com a aplicação, `migracoes.sql`, os scripts e esta documentação. Copie a pasta para o servidor, por exemplo para `C:\LAMGestRestaurant`.

## 3. Criar a base de dados
Na consola do servidor, como Administrador:
```powershell
sqlcmd -S .\SYSON -E -Q "CREATE DATABASE [LAMGestRestaurantDB]"
sqlcmd -S .\SYSON -E -d LAMGestRestaurantDB -b -i C:\LAMGestRestaurant\migracoes.sql
```
O `migracoes.sql` é idempotente: só aplica o que falta. Substitua `.\SYSON` pelo nome da sua instância.

## 4. Instalar o serviço
PowerShell **como Administrador**:
```powershell
cd C:\LAMGestRestaurant
.\scripts\instalar-servico.ps1 -Porta 8080 -Servidor ".\SYSON" -BaseDados LAMGestRestaurantDB
```
O script:
- cria o serviço `LAMGestRestaurant` (arranque automático, reinício automático em falha) com a conta virtual `NT SERVICE\LAMGestRestaurant`, sem palavra-passe;
- dá a essa conta acesso de leitura/escrita **só** à base do LAMGest (não pode alterar a estrutura);
- cria `C:\ProgramData\LAMGestRestaurant\chaves` (chaves das sessões, só acessível ao serviço e administradores) e `...\logs`;
- grava as variáveis do serviço (connection string, porta, pastas) — nada fica no código;
- abre a porta na firewall do Windows só para redes **Domínio/Privada** (confirme que a rede do restaurante está marcada como *Privada*);
- arranca o serviço e mostra os endereços a usar nos terminais.

## 5. Primeiro administrador
Uma única vez, na consola do servidor como Administrador (a palavra-passe é pedida sem eco):
```powershell
cd C:\LAMGestRestaurant
$env:ConnectionStrings__Default = "Server=.\SYSON;Database=LAMGestRestaurantDB;Trusted_Connection=True;TrustServerCertificate=True"
.\LAMGestRestaurant.Api.exe bootstrap-admin
```
Feche a consola no fim. Os restantes utilizadores criam-se na aplicação (**Utilizadores**).

## 6. Configurar os terminais
1. Em cada terminal abra `http://IP-DO-SERVIDOR:8080` (ou `http://NOME-DO-SERVIDOR:8080`) e crie um atalho/favorito. Em tablets: «Adicionar ao ecrã principal».
2. Caixas: em **Caixa → Terminais** registe um terminal por computador de caixa; depois, em cada computador, escolha uma vez o terminal correspondente (fica guardado no navegador desse computador).
3. Impressoras de talão: instale-as no Windows do terminal com o driver do fabricante e defina-as como predefinidas. O navegador tem de permitir pop-ups para o endereço do servidor.
4. Ecrã da cozinha: abra **Cozinha** num navegador em ecrã inteiro (F11).
5. A barra superior mostra «Ligado ao servidor» e «Tempo real». Se o servidor ou a base falhar, as gravações ficam bloqueadas e aparece uma faixa vermelha (não há modo offline).

## 7. HTTPS (recomendado)
Em HTTP simples, quem estiver na mesma rede pode ver o tráfego. Para cifrar:
1. Crie um certificado para o nome e o IP do servidor (PowerShell como Administrador):
   ```powershell
   New-SelfSignedCertificate -Subject "CN=lamgest-servidor" -DnsName "lamgest-servidor","192.168.1.10" `
     -CertStoreLocation Cert:\LocalMachine\My -NotAfter (Get-Date).AddYears(5) -KeyExportPolicy Exportable
   ```
   Exporte o certificado **sem a chave privada** (.cer) e importe-o em cada terminal em *Autoridades de Certificação de Raiz Fidedigna*.
2. Dê ao serviço acesso de leitura à chave privada: `certlm.msc` → Pessoal → Certificados → certificado → *Todas as tarefas → Gerir chaves privadas* → adicionar `NT SERVICE\LAMGestRestaurant` (Leitura).
3. Acrescente às variáveis do serviço (Editor de Registo: `HKLM\SYSTEM\CurrentControlSet\Services\LAMGestRestaurant`, valor `Environment`), substituindo `ASPNETCORE_URLS`:
   ```text
   Kestrel__Endpoints__Https__Url=https://0.0.0.0:8443
   Kestrel__Endpoints__Https__Certificate__Subject=lamgest-servidor
   Kestrel__Endpoints__Https__Certificate__Store=My
   Kestrel__Endpoints__Https__Certificate__Location=LocalMachine
   Seguranca__CookieSoHttps=true
   ```
4. Abra a porta 8443 na firewall (como no passo 4), reinicie o serviço (`Restart-Service LAMGestRestaurant`) e use `https://lamgest-servidor:8443` nos terminais.

## 8. Actualizar para uma nova versão
1. Gere o pacote novo (`publicar.ps1`) e copie-o para o servidor numa pasta nova.
2. **Faça uma cópia de segurança** (`scripts\backup-sql.ps1`, ver [BACKUP.md](BACKUP.md)).
3. Pare o serviço: `Stop-Service LAMGestRestaurant`.
4. Reveja o `migracoes.sql` novo: se apagar colunas/tabelas (`DROP`), não aplique sem confirmar que os dados não são necessários. Aplique: `sqlcmd -S .\SYSON -E -d LAMGestRestaurantDB -b -i migracoes.sql`.
5. Substitua o conteúdo de `C:\LAMGestRestaurant` pelo pacote novo (os dados, chaves e logs estão em `C:\ProgramData\LAMGestRestaurant` e na base de dados) e volte a correr `instalar-servico.ps1` (actualiza o serviço existente) ou só `Start-Service LAMGestRestaurant`.
6. Confirme em `http://localhost:8080/api/saude` que `migracoesPendentes` é `false`.

## Resolução de problemas
| Sintoma | Verificar |
|---|---|
| Terminais não abrem a página | `Test-NetConnection IP-DO-SERVIDOR -Port 8080` no terminal; rede marcada como *Privada* no servidor; serviço em execução (`Get-Service LAMGestRestaurant`). |
| «Sem ligação à base de dados» | Serviço `SQL Server (SYSON)` em execução; login `NT SERVICE\LAMGestRestaurant` existe na instância. |
| «Migrations pendentes» | Aplicar o `migracoes.sql` da versão instalada (passo 8.4). |
| Todos têm de voltar a entrar após reiniciar | `DataProtection__Pasta` definida e a pasta de chaves acessível ao serviço. |
| Erros inesperados | Logs em `C:\ProgramData\LAMGestRestaurant\logs\lamgest-AAAAMMDD.log` (30 dias). |
