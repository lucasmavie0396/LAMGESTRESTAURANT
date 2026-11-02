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
- [x] Tailwind com tema azul, layout base (menu lateral filtrado por permissões, barra superior com utilizador, perfil, estado da ligação, data/hora de Maputo)
- [x] Indicador de ligação (`/api/saude` a cada 10 s; 3 s após falha) e faixa de bloqueio de gravações sem servidor/BD
- [x] Ecrã de login, alteração de palavra-passe e alteração obrigatória no primeiro acesso (imposta também pela API)
- [x] Gestão de utilizadores (API + ecrã): pesquisa, filtro, ordenação, paginação, criar, editar perfis, activar/desactivar com confirmação, repor palavra-passe, desbloquear
- [x] Gestão de perfis (API + ecrã): permissões por módulo; Administrador fixo com todas; perfis de sistema não renomeáveis nem elimináveis
- [x] Protecção: tem de existir sempre outro administrador activo; não é possível desactivar a própria conta
- [x] `bootstrap-admin` testado (cria na base vazia; recusa se já houver administrador; tolera BOM na entrada redireccionada)
- [x] Testes: 30 xUnit + 3 Vitest; verificação manual no browser (login, validações, criação de utilizador, alteração obrigatória, sem permissão, queda e recuperação da API)
- [ ] Testes de componentes React (Vitest + Testing Library) para login e formulários
- [ ] Interface `IAppDbContext` em Application para os serviços de caso de uso

## Fase 3 — Fluxo principal
- [x] `IAppDbContext` em Application; serviços de caso de uso em Application (catálogo, sala, caixa)
- [x] Caixa: terminais; abrir (1 sessão por terminal e por utilizador, protegido por índice único — testada corrida de 4 aberturas simultâneas); entradas/saídas com motivo (saída não excede o esperado na gaveta); fecho com contagem por método, diferenças gravadas e relatório imprimível; histórico (cada caixa vê só as suas; `caixa.ver-todas` vê todas)
- [x] Produtos e categorias: CRUD, subcategorias sem ciclos, estações de preparação, código sugerido, código de barras único, esgotado/disponível (também pela cozinha), desactivação em vez de eliminação, alteração de preço com permissão própria e auditoria, concorrência optimista (409)
- [x] Mesas e zonas: CRUD, mapa por zona com cores e contagem por estado, Livre↔Indisponível manual (Ocupada/Reservada só por pedidos/reservas)
- [x] Validação decimal independente da cultura do servidor (corrigido bug real com cultura pt)
- [x] Testes: 47 xUnit; verificação manual no browser de categorias, produtos, mesas e ciclo completo de caixa
- [x] Pedidos Mesa/Take Away/Delivery: criação idempotente (Idempotency-Key), numeração PED-AAAA-NNNNNN sem duplicados, uma mesa = um pedido aberto (índice único), totais e impostos incluídos calculados no servidor (`CalculoPedido`, imposto por linha), alterações com RowVersion (clique repetido → 409, sem duplicar itens)
- [x] Itens: adicionar, alterar/retirar antes do envio, observações, cancelar depois do envio (permissão + motivo), desconto em valor ou % (permissão + auditoria), transferência de mesa registada, cancelamento do pedido (motivo; exige anular pagamentos antes)
- [x] Cozinha (KDS): envio em tempo real via SignalR (`/hubs/operacoes`), estados Pendente → Em preparação → Pronto → Entregue com utilizador/hora no histórico, filtro por estação e estado, alerta visual por tempo de espera, protecção contra dois postos alterarem o mesmo item (409), aviso «Pronto» no POS
- [x] Pagamentos: idempotentes (testado com 5 reenvios simultâneos), parciais/divisão por valor, troco só em Dinheiro, referência obrigatória em M-Pesa/e-Mola/Transferência/Cheque, sessão de caixa aberta do operador, dois terminais a pagar o mesmo pedido em simultâneo nunca excedem o total
- [x] Fecho automático quando o saldo chega a zero: mesa libertada, recibo interno REC-AAAA-NNNNNN, baixa de stock única pela ficha técnica (conversão de unidades, stock negativo bloqueado salvo autorização), tudo na mesma transacção
- [x] Recibo imprimível em formato de talão 80 mm (janela de impressão do Windows)
- [x] Auditoria: criar, cancelar item, desconto, transferir, cancelar pedido, pagamento, anulação, fecho
- [x] Testes: 79 xUnit (incl. corridas de concorrência e stock); verificação manual no browser do ciclo mesa → cozinha em tempo real → pagamento parcial → M-Pesa → recibo
- [ ] Estorno/reembolso de pedidos já fechados (reverte stock e caixa com auditoria)
- [ ] Separar conta por produto (por valor já funciona com pagamentos parciais)
- [ ] Ecrã de gestão de Delivery/Take Away (o estado de entrega já existe na API)
- [ ] Relatório de vendas a partir de dados persistidos
- [ ] Impressão directa ESC/POS em impressoras de rede (talões de cozinha e recibos)

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
- [ ] Persistir as chaves de Data Protection numa pasta protegida do servidor (necessário para as sessões sobreviverem a reinícios quando a API corre como serviço Windows)
- [ ] HTTPS na LAN (certificado interno) — recomendado; actualmente cookie `SecurePolicy=SameAsRequest`
- [ ] Backups e recuperação — `docs/BACKUP.md`
- [ ] Lista de verificação de produção
