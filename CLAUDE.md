# CLAUDE.md — LAMGest Restaurant ERP

Decisões arquitecturais e regras permanentes. Ler antes de qualquer alteração.

## Stack
- Backend: .NET 9, ASP.NET Core Web API (controllers), EF Core 9 + SQL Server, ASP.NET Core Identity, SignalR, Serilog, Swagger, FluentValidation.
- Frontend: React 19 + TypeScript + Vite 6, Tailwind 3, React Router 7, React Hook Form + Zod, Lucide, Recharts, @microsoft/signalr.
- Testes: xUnit (backend), Vitest (frontend), Playwright (E2E, fase 5).

## Camadas (backend/)
- `Domain` — entidades, enums, regras puras. Não depende de nada.
- `Application` — serviços de caso de uso, DTOs, validadores, interfaces (ex.: `IAppDbContext`, `ICurrentUser`).
- `Infrastructure` — EF Core (`AppDbContext`, configurações, migrations), Identity, auditoria, impressão.
- `Api` — controllers, hubs SignalR, middleware de excepções, autorização, serve o frontend compilado em `wwwroot`.
- `Tests` — xUnit; integração com `WebApplicationFactory`.

## Ambiente de desenvolvimento
- SQL Server 2022 Express, instância `.\SYSON`, autenticação Windows, base `LAMGestRestaurantDB`.
- A instância contém a base `MZullo_Rest` de outro sistema: **nunca** a ler, alterar ou apagar.
- Connection string só via `dotnet user-secrets` (dev) ou variável de ambiente `ConnectionStrings__Default` (produção). Nunca no repositório.
- Express não tem SQL Server Agent: backups automáticos têm de ser feitos com o Agendador de Tarefas do Windows + `sqlcmd`.

## Regras permanentes
1. Dinheiro: `decimal(18,2)`; quantidades: `decimal(18,3)`; taxas: `decimal(9,4)`. Nunca `float`/`double`.
2. Datas guardadas em UTC (`DateTime` UTC); apresentação no fuso configurado (padrão `Africa/Maputo`).
3. Moeda padrão MZN ("MT"), configurável. Interface em Português (Moçambique).
4. Totais, preços, descontos e estados finais são sempre calculados/validados no servidor.
5. Pedidos, pagamentos, compras e movimentos de stock: sempre dentro de transacção.
6. Concorrência optimista com `rowversion` em entidades editáveis concorrentemente (Pedido, Mesa, SessaoCaixa, StockSaldo, Produto).
7. Idempotência: cabeçalho `Idempotency-Key` obrigatório em criação de pedidos, pagamentos e confirmações; chave única na BD.
8. Nunca apagar registos financeiros/históricos: usar estado `Anulado`/estorno + auditoria. Produtos/clientes usados → desactivar.
9. Permissões: claims `perm` no formato `modulo.operacao` (ex.: `pos.desconto`). Cada endpoint sensível tem `[Authorize(Policy = ...)]`.
10. Auditoria só por inserção; nenhum endpoint de edição/eliminação.
11. Sem modo offline: se a API/BD não responder, o frontend bloqueia operações de gravação e mostra o estado.
12. Primeiro administrador criado por comando de bootstrap (`dotnet run -- bootstrap-admin`), palavra-passe pedida na consola. Nenhuma palavra-passe fixa no código.
13. Dados de demonstração só via script explícito e marcados como "DEMO".
14. Migrations: nunca aplicar migrations destrutivas a uma base existente sem aprovação do utilizador.

## Comandos
- Backend: `cd backend && dotnet build && dotnet test`
- API: `cd backend && dotnet run --project LAMGestRestaurant.Api`
- Frontend: `cd frontend && npm run dev` / `npm test` / `npm run build`
- Migration: `cd backend && dotnet ef migrations add <Nome> -p LAMGestRestaurant.Infrastructure -s LAMGestRestaurant.Api`

## Progresso
Ver `docs/TAREFAS.md` — só marcar como concluído o que foi verificado.
