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
- [x] Relatório financeiro (`relatorios.financeiros`): resultado estimado (vendas sem impostos − custo estimado − despesas), recebimentos por método, despesas por categoria e forma de pagamento, fechos de caixa do período com esperado/contado/diferença e totais por método; estornos e compras mostrados à parte
- [x] Relatório de stock e compras (`relatorios.stock`): valor actual do stock (por tipo e armazém), movimentos por tipo, consumo por artigo, ajustes/perdas/inventário, entradas por compra e compras por fornecedor
- [x] Ecrã de Relatórios com separadores conforme as permissões (Vendas / Financeiro e caixa / Stock e compras), período partilhado, CSV para Excel em cada secção e impressão/PDF
- [x] Testes: 102 xUnit; verificação no browser dos três separadores com os dados da base de testes (resultado 4 763,76 − 1 415 − 500 = 2 848,76; fechos com diferença −60)
- [ ] Exportação nativa .xlsx (hoje CSV, que o Excel abre directamente)
- [ ] Dividir o bundle do frontend por rotas (aviso do Vite > 500 kB; não afecta o funcionamento em LAN)

## Fase 5 — Testes e LAN
- [x] Testes de concorrência e reenvio de pagamentos (feitos nas fases 3–4: aberturas de caixa simultâneas, 5 reenvios do mesmo pagamento, dois terminais a pagar o mesmo pedido, RowVersion → 409)
- [x] Testes de permissões em cada módulo (403 por perfil) — 104 xUnit no total
- [x] Playwright E2E (`npm run e2e`): base própria `LAMGestRestaurantDB_E2E`, administrador com palavra-passe aleatória apagada no fim; login, venda à mesa com cozinha em tempo real noutro separador até ao recibo e relatório, bloqueio de gravações sem servidor — 3/3 a passar com o Edge
- [x] Serviço Windows (`UseWindowsService`, directório da aplicação como raiz), cabeçalhos de segurança (nosniff, DENY, no-referrer)
- [x] Chaves de Data Protection persistidas em `DataProtection:Pasta` e cifradas com DPAPI da máquina — testado: a sessão sobrevive a uma nova instância da aplicação
- [x] Opção `Seguranca:CookieSoHttps` e guia de HTTPS com certificado interno (Kestrel por variáveis do serviço)
- [x] `scripts/publicar.ps1` (frontend em wwwroot, `migracoes.sql` idempotente, scripts, docs) — testado: pacote publicado arrancou em Production, serviu `/pos`, `/api/saude` 200, API 401 sem sessão, Swagger desligado
- [x] `scripts/instalar-servico.ps1`: conta virtual `NT SERVICE\LAMGestRestaurant` só com leitura/escrita na base, pastas em ProgramData com ACL, variáveis do serviço, firewall Domínio/Privada, reinício automático — **não executado aqui** (altera o sistema; corre-se no servidor de produção)
- [x] Backups: `backup-sql.ps1` (CHECKSUM + VERIFYONLY + segunda cópia + retenção só dos seus ficheiros, recusa bases que não sejam LAMGestRestaurant*) e `testar-restauro.ps1` (restaura para `_Restauro`, contagens, elimina) — ambos testados com a base de testes; `registar-backup.ps1` (Agendador de Tarefas) não executado aqui
- [x] Documentação: `docs/INSTALACAO_LAN.md`, `docs/BACKUP.md`, `docs/PRODUCAO.md` (lista de verificação)
- [ ] No servidor de produção: correr `instalar-servico.ps1`, `bootstrap-admin` e `registar-backup.ps1`, e seguir `docs/PRODUCAO.md`

## Instalador (LAMGest-Setup.exe)
- [x] Número de posto nos terminais (único, migration `NumeroPosto` só aditiva), consulta pública `GET /api/postos/{n}`, ecrã Caixa → Terminais com «N.º posto»
- [x] Frontend: atalho com `?posto=N` associa o navegador ao terminal antes de arrancar; barra superior mostra «Posto N · nome» ou «não registado»
- [x] Comando `migrar` (e `migrar --listar`) na API; ficheiro de configuração protegido indicado por `LAMGEST_CONFIG`
- [x] Assistente WinForms (administrador, ficheiro único self-contained ~100 MB): modo Servidor (instâncias locais detectadas, autenticação Windows ou SQL `sa`+palavra-passe, só bases `LAMGestRestaurant*`, análise da base vs. migrations do pacote, recusa bases de outros sistemas e versões mais recentes, cópia de segurança + confirmação antes de actualizar base com dados, primeiro administrador, serviço com conta virtual, login SQL próprio com palavra-passe aleatória, ACLs, firewall, tarefa de backup como SYSTEM com permissões mínimas, verificação de `/api/saude`) e modo Posto (teste do servidor, confirmação do número do posto, atalhos Edge/Chrome em modo aplicação, arranque automático, ecrã inteiro)
- [x] `scripts/criar-setup.ps1` gera o instalador — testado (101 MB)
- [x] Testes: 11 xUnit do instalador (base nova → migrar → administrador → cópia → API responde ao instalador do posto; base de outro sistema recusada; nomes fora do padrão recusados sem ligar; login SQL do serviço lê/escreve mas não altera estrutura; atalhos e registo do posto); E2E do `?posto=N`; capturas de todas as páginas do assistente revistas
- [ ] Executar o modo Servidor completo numa máquina de teste (cria serviço, firewall e tarefa: não foi executado aqui por alterar o sistema)
- [ ] Desinstalação (hoje: parar/remover o serviço e apagar a pasta manualmente)
- [ ] Assinatura digital do Setup.exe (sem ela o Windows SmartScreen pode avisar)
