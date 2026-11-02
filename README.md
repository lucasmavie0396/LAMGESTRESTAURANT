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

## Executar em desenvolvimento
```bash
cd backend
dotnet run --project LAMGestRestaurant.Api
```
```bash
cd frontend
npm run dev
```

## Testes
```bash
cd backend
dotnet test
```
```bash
cd frontend
npm test
```

## Resolução de problemas
- **Não liga ao SQL Server:** confirmar que o serviço `SQL Server (SYSON)` está a correr e o nome da instância na connection string.
- **Outros computadores não acedem:** ver `docs/INSTALACAO_LAN.md` (a criar na fase 5) para firewall e endereço IP.
