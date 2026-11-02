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
- [x] Entidades de domínio (47 tabelas) e `AppDbContext` com convenções (decimais, UTC, enums texto, rowversion, FKs sem cascata)
- [x] Connection string via user-secrets; erro claro se faltar
- [x] Identity (cookie HttpOnly SameSite=Strict, bloqueio após 5 falhas), 6 perfis, 41 permissões por claims
- [x] Endpoints `api/auth` (login, logout, me, alterar-palavra-passe) e `api/saude`
- [x] Comando `bootstrap-admin` (implementado; ainda não executado — cabe ao administrador)
- [x] Serilog (consola + ficheiro diário), tratador global de excepções com ProblemDetails em Português
- [x] Migration `Inicial` aplicada a `LAMGestRestaurantDB` (.\SYSON)
- [x] Auditoria por inserção na mesma transacção; registos imutáveis protegidos no SaveChanges
- [x] Semeador idempotente de dados de referência (perfis, métodos de pagamento, unidades, armazém, taxas, configurações)
- [x] Testes: 21 xUnit (base `LAMGestRestaurantDB_Tests`) e 3 Vitest a passar
- [x] Proxy Vite → API em desenvolvimento
- [ ] Tailwind, layout base (menu lateral, barra superior, estado da ligação), ecrã de login
- [ ] Gestão de utilizadores e perfis (API + ecrãs), reposição de palavra-passe pelo administrador
- [ ] Interface `IAppDbContext` em Application para os serviços de caso de uso

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
