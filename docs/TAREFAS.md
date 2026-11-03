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
- [x] Estorno total de pedidos pagos: reembolso pela sessão de caixa aberta do operador (movimento «Reembolso», limitado ao dinheiro na gaveta), recibo anulado, stock reposto com movimentos de estorno, pedido «Estornado» sai do KDS, auditoria; sessões fechadas nunca são alteradas
- [x] Ecrã Delivery/Take Away em colunas por estado (Recebido → Em preparação → Pronto → Saiu → Entregue), contacto e endereço, estado do pagamento, tempo real
- [x] Relatório de vendas (hoje, ontem, 7 dias, mês, mês anterior, personalizado): totais, ticket médio, impostos, margem estimada com metodologia, por dia (gráfico), método, tipo, categoria, produto (mais → menos vendido), utilizador, garçom, cancelamentos e estornos; CSV para Excel e impressão/PDF
- [x] Dashboard com dados reais: vendas de hoje vs ontem, pedidos, ticket médio, pedidos em aberto, cozinha, 7 dias, métodos, top produtos, caixas abertos (conforme permissão), stock baixo (com `stock.ver`), alertas de cancelamentos/estornos; actualiza em tempo real
- [x] Testes: 86 xUnit + 4 Vitest; verificação no browser com vendas reais (dashboard, relatório, delivery, estorno)
- [ ] Estorno parcial (hoje: estornar o pedido completo e registar um novo)
- [ ] Separar conta por produto (por valor já funciona com pagamentos parciais)
- [ ] Impressão directa ESC/POS em impressoras de rede (talões de cozinha e recibos)
- [ ] Relatórios financeiros (fechos de caixa e diferenças) e de stock/compras — depois dos módulos de stock, compras e despesas

## Fase 4 — Restantes módulos
- [x] API de stock: artigos (CRUD, mínimo, stock negativo só com `stock.ajustar`), movimentos, ajustes/perdas com motivo, transferências entre armazéns, inventários (contagem; no fecho o saldo passa a ser o contado, mesmo com movimentos posteriores à abertura)
- [x] API de fichas técnicas versionadas (nova versão encerra a anterior; validação de unidades; custo estimado pelo custo médio)
- [x] API de fornecedores e compras: rascunho editável/eliminável, totais no servidor (custos sem IVA), confirmação única com custo médio ponderado e último custo, anulação com estorno de stock (bloqueada se já consumido), resumo por fornecedor
- [x] Testes: 93 xUnit (custo médio, anulação, versões de receita, inventário, transferências, permissões)
- [x] Ecrã de Stock: artigos (pesquisa, filtros, alerta abaixo do mínimo, saldo por armazém, valor), ajuste/perda, transferência, histórico de movimentos, criação de armazéns, inventários (folha de contagem, guardar, confirmar com aviso, cancelar), fichas técnicas (lista com custo/preço, editor com unidades compatíveis, histórico de versões, retirar)
- [x] Ecrã de Compras: lista com pesquisa e estados, formulário de linhas com pré-visualização dos totais, rascunho → confirmar (entrada no stock) → anular com motivo, eliminar rascunho; fornecedores (NUIT validado, activar/desactivar, total comprado)
- [x] Verificação no browser: artigo → fornecedor → compra 10 kg (2 204 MT, custo médio 190/kg) → ficha técnica 300 g (custo 57 MT) → inventário (−0,5 kg) com movimentos correctos
- [x] Clientes e fidelização: ficha (telefone normalizado e único, NUIT, cartão), activar/desactivar, histórico de pedidos pagos; no POS procura/cria cliente e associa-o ao pedido aberto; ao fechar o pedido acumulam pontos (1 ponto por cada N MT, configurável) e no estorno são retirados — movimentos de pontos só por inserção
- [x] Reservas: agenda por dia, duração, mesa opcional com lugares suficientes, conflito de horário na mesma mesa (409), estados Pendente → Confirmada → Chegou / Não compareceu / Cancelada com motivo
- [x] Despesas: categorias (semeadas), registo com método e referência, opção «paga com dinheiro da caixa» (cria movimento «Despesa» na sessão aberta deste terminal), anulação com motivo, totais por categoria
- [x] Funcionários: função, contactos, documento (dado pessoal), ligação a um utilizador — os pedidos abertos por esse utilizador no POS ficam atribuídos ao garçom
- [x] Configurações: dados do restaurante, regional, taxa de entrega, fidelização, taxas de imposto, métodos de pagamento e terminais; valores numéricos aceitam vírgula decimal e são guardados em formato invariante
- [x] Testes: 99 xUnit + 6 Vitest; verificação no browser: cliente associado no POS → pagamento 450,50 MT → 4 pontos e 1 pedido; reserva + conflito na mesma mesa; despesa de 1 250,75 MT pela caixa (esperado 199,75 MT); funcionário ligado a utilizador
- [ ] Resgate de pontos de fidelização (hoje só acumulam e são retirados no estorno)
- [ ] Reservas não alteram automaticamente o estado da mesa (o operador ocupa a mesa ao abrir o pedido)
- [ ] Anular uma despesa paga por uma caixa já fechada não altera esse fecho (fica registada a anulação; o acerto é manual)
- [ ] Relatórios financeiros (fechos de caixa, despesas) e de stock/compras + exportação Excel

## Fase 5 — Testes e LAN
- [ ] Testes de concorrência e reenvio de pagamentos
- [ ] Testes de permissões
- [ ] Playwright E2E
- [ ] Publicação, firewall, IP — `docs/INSTALACAO_LAN.md`
- [ ] Persistir as chaves de Data Protection numa pasta protegida do servidor (necessário para as sessões sobreviverem a reinícios quando a API corre como serviço Windows)
- [ ] HTTPS na LAN (certificado interno) — recomendado; actualmente cookie `SecurePolicy=SameAsRequest`
- [ ] Backups e recuperação — `docs/BACKUP.md`
- [ ] Lista de verificação de produção
