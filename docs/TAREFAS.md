# Lista de tarefas por módulo

Legenda: `[x]` concluído e verificado · `[~]` em curso · `[ ]` por fazer

## Fase 1 — Inspecção e planeamento
- [x] Inspecção do ambiente (.NET 9.0.315, Node 22.23, Git 2.56, SQL Server 2022 Express `.\SYSON`)
- [x] Arquitectura e modelo de dados inicial apresentados
- [x] Pasta do projecto e servidor SQL decididos

## Fase 2 — Fundação
- [x] Solução backend (Domain, Application, Infrastructure, Api, Tests) — compila sem avisos
- [x] Frontend Vite + React + TS com dependências instaladas
- [x] CLAUDE.md, README.md, TAREFAS.md, .gitignore, .env.example
- [ ] Tailwind, layout base (menu lateral, barra superior, estado da ligação)
- [ ] Entidades de domínio e `AppDbContext` com configurações
- [ ] Connection string via user-secrets
- [ ] Identity, perfis, permissões por claims, bootstrap do administrador
- [ ] Serilog, middleware global de excepções, ProblemDetails
- [ ] Migration inicial aplicada a `LAMGestRestaurantDB`
- [ ] Auditoria (serviço + interceptor)
- [ ] Testes base xUnit e Vitest a passar

## Fase 3 — Fluxo principal
- [ ] Abrir caixa
- [ ] Produtos e categorias
- [ ] Mesas/zonas; Take Away/Delivery
- [ ] Pedido e itens (idempotente)
- [ ] Envio à cozinha + KDS em tempo real (SignalR)
- [ ] Pagamento idempotente, troco, recibo
- [ ] Fechar pedido e libertar mesa
- [ ] Baixa de stock única por receita
- [ ] Auditoria das operações
- [ ] Relatório de vendas a partir de dados persistidos

## Fase 4 — Restantes módulos
- [ ] Compras e fornecedores
- [ ] Clientes e fidelização
- [ ] Reservas
- [ ] Despesas
- [ ] Receitas/fichas técnicas (versionadas)
- [ ] Inventários
- [ ] Relatórios + exportação CSV/Excel/PDF
- [ ] Configurações
- [ ] Funcionários

## Fase 5 — Testes e LAN
- [ ] Testes de concorrência e reenvio de pagamentos
- [ ] Testes de permissões
- [ ] Playwright E2E
- [ ] Publicação, firewall, IP — `docs/INSTALACAO_LAN.md`
- [ ] Backups e recuperação — `docs/BACKUP.md`
- [ ] Lista de verificação de produção
