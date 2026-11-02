# LAMGest Restaurant ERP

Sistema web de gestão integrada de restaurante/bar (POS, mesas, cozinha, caixa, stock, compras, clientes, relatórios) para funcionar numa rede local com um servidor central e SQL Server.

> **Estado:** em desenvolvimento, fase 2 (fundação). Ver [docs/TAREFAS.md](docs/TAREFAS.md). Ainda não está pronto para produção.

## Pré-requisitos (servidor)
- .NET SDK 9.0
- Node.js 22 LTS e npm
- Microsoft SQL Server 2019+ (Express serve) com autenticação Windows ou SQL
- Navegador moderno nos terminais (Chrome/Edge)

## Estrutura
```text
backend/   Solução .NET (Domain, Application, Infrastructure, Api, Tests)
frontend/  React + TypeScript + Vite
docs/      Documentação (tarefas, instalação LAN, backup)
scripts/   Scripts auxiliares
```

## Configuração da base de dados (desenvolvimento)
A connection string **nunca** fica no repositório. Em desenvolvimento usa-se user-secrets:

```bash
cd backend
dotnet user-secrets --project LAMGestRestaurant.Api set "ConnectionStrings:Default" "Server=.\SYSON;Database=LAMGestRestaurantDB;Trusted_Connection=True;TrustServerCertificate=True"
```

Em produção usa-se a variável de ambiente `ConnectionStrings__Default` (ver `.env.example`).

## Base de dados: migrations
As migrations **não** são aplicadas automaticamente no arranque. Para criar ou actualizar a base:

```bash
cd backend
dotnet tool restore
dotnet ef database update -p LAMGestRestaurant.Infrastructure -s LAMGestRestaurant.Api
```

Antes de actualizar uma base com dados reais, faça backup e reveja o SQL gerado:

```bash
cd backend
dotnet ef migrations script --idempotent -p LAMGestRestaurant.Infrastructure -s LAMGestRestaurant.Api -o migracao.sql
```

Com a base actualizada, a API semeia no arranque os dados de referência em falta (perfis, permissões, métodos de pagamento, unidades, armazém principal, configurações). Não cria produtos, mesas nem clientes.

## Primeiro administrador
Corra uma única vez, na consola do servidor (a palavra-passe é pedida sem eco e não fica gravada em nenhum ficheiro):

```bash
cd backend
dotnet run --project LAMGestRestaurant.Api -- bootstrap-admin
```

O comando recusa-se a correr se já existir um administrador.

## Executar em desenvolvimento
API em `http://localhost:5080` (Swagger em `/swagger`):

```bash
cd backend
dotnet run --project LAMGestRestaurant.Api
```

Frontend em `http://localhost:5173` (reencaminha `/api` e `/hubs` para a API):

```bash
cd frontend
npm run dev
```

## Testes
Os testes do backend criam e recriam a base **`LAMGestRestaurantDB_Tests`** no servidor `.\SYSON` (altere com a variável `LAMGEST_TESTES_SQL`). Nunca tocam na base principal.

```bash
cd backend
dotnet test
```
```bash
cd frontend
npm test
```

## Autenticação e permissões
- Sessão por cookie HttpOnly (`LAMGest.Sessao`, SameSite=Strict, 12 h deslizantes).
- 5 tentativas falhadas bloqueiam a conta durante 15 minutos.
- Permissões no formato `modulo.operacao`, atribuídas a perfis. O perfil Administrador recebe sempre todas.
- Recuperação de palavra-passe: não há envio de e-mail (rede local). O administrador repõe a palavra-passe e o utilizador é obrigado a alterá-la no login seguinte. A reposição ainda não está implementada.

## Resolução de problemas
- **"Connection string 'Default' não configurada":** em desenvolvimento, confirme os user-secrets e que a API corre com o perfil `http` (ambiente Development).
- **`/api/saude` devolve 503 com `migracoesPendentes: true`:** aplique as migrations (ver acima).
- **Não liga ao SQL Server:** confirmar que o serviço `SQL Server (SYSON)` está a correr e o nome da instância na connection string.
- **Outros computadores não acedem:** ver `docs/INSTALACAO_LAN.md` (a criar na fase 5) para firewall e endereço IP.
