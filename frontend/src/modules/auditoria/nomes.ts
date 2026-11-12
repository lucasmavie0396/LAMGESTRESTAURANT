/** Nome legível de cada módulo (início do código da acção). */
export const NOMES_MODULO: Record<string, string> = {
  armazem: 'Armazéns', artigo: 'Artigos de stock', caixa: 'Caixa', cartao: 'Cartões Evento', categoria: 'Categorias', cliente: 'Clientes',
  compra: 'Compras', configuracoes: 'Configurações', despesa: 'Despesas', documento: 'Facturação', estacao: 'Estações de preparação',
  fornecedor: 'Fornecedores', funcionario: 'Funcionários', imagem: 'Imagens', impressora: 'Impressoras', inventario: 'Inventários', licenca: 'Licença',
  login: 'Entradas no sistema', 'login-pin': 'Entradas no sistema (código)', logout: 'Saídas do sistema', mesa: 'Mesas', pagamento: 'Pagamentos',
  pedido: 'Pedidos', perfil: 'Perfis', posto: 'Postos', produto: 'Produtos', receita: 'Fichas técnicas', reserva: 'Reservas', sistema: 'Sistema',
  stock: 'Stock', taxa: 'Impostos', terminal: 'Postos (terminais)', unidade: 'Unidades', utilizador: 'Utilizadores', zona: 'Zonas',
}

/** Descrição das acções mais comuns; as outras mostram o código. */
export const NOMES_ACCAO: Record<string, string> = {
  login: 'Entrou no sistema', 'login-pin': 'Entrou com o código', 'login.falhado': 'Tentativa de entrada falhada', 'login.bloqueado': 'Conta bloqueada por tentativas',
  logout: 'Saiu do sistema',
  'pedido.criar': 'Abriu um pedido', 'pedido.cancelar': 'Cancelou um pedido', 'pedido.cancelar-item': 'Anulou um item do pedido', 'pedido.desconto': 'Aplicou um desconto',
  'pedido.fechar': 'Fechou um pedido (pago)', 'pedido.estornar': 'Estornou um pedido', 'pedido.conta': 'Pediu a conta', 'pedido.transferir-mesa': 'Transferiu de mesa',
  'pedido.reimprimir-taloes': 'Reimprimiu os talões', 'pedido.responsavel': 'Passou a mesa a outro responsável',
  'pagamento.registar': 'Registou um pagamento', 'pagamento.anular': 'Anulou um pagamento',
  'documento.emitir': 'Emitiu um documento', 'documento.anular': 'Anulou um documento',
  'caixa.abrir': 'Abriu o caixa', 'caixa.fechar': 'Fechou o caixa', 'caixa.entrada': 'Entrada de dinheiro no caixa', 'caixa.saida': 'Saída de dinheiro do caixa',
  'caixa.abrir-gaveta': 'Abriu a gaveta (botão)', 'caixa.dia-abrir': 'Abriu o dia', 'caixa.dia-fechar': 'Fechou o dia',
  'cartao.registar': 'Registou um Cartão Evento', 'cartao.recarregar': 'Recarregou um cartão', 'cartao.bloquear': 'Bloqueou um cartão', 'cartao.desbloquear': 'Desbloqueou um cartão',
  'cartao.descarregar': 'Descarregou saldo de cartões',
  'produto.criar': 'Criou um produto', 'produto.editar': 'Editou um produto', 'produto.alterar-preco': 'Alterou o preço', 'produto.importar': 'Importou produtos do Excel',
  'produto.activar': 'Activou um produto', 'produto.desactivar': 'Desactivou um produto', 'produto.disponibilidade': 'Mudou a disponibilidade',
  'stock.ajuste': 'Ajuste de stock', 'stock.perda': 'Perda de stock', 'stock.transferencia': 'Transferência entre armazéns',
  'inventario.criar': 'Criou um inventário', 'inventario.confirmar': 'Confirmou um inventário', 'inventario.cancelar': 'Cancelou um inventário',
  'compra.criar': 'Criou uma compra', 'compra.confirmar': 'Confirmou uma compra', 'compra.anular': 'Anulou uma compra',
  'despesa.registar': 'Registou uma despesa', 'despesa.anular': 'Anulou uma despesa',
  'cliente.criar': 'Criou um cliente', 'cliente.editar': 'Editou um cliente', 'cliente.ajustar-pontos': 'Ajustou os pontos',
  'utilizador.criar': 'Criou um utilizador', 'utilizador.editar': 'Editou um utilizador', 'utilizador.desactivar': 'Desactivou um utilizador',
  'utilizador.repor-palavra-passe': 'Repôs a palavra-passe', 'utilizador.alterar-palavra-passe': 'Alterou a palavra-passe', 'utilizador.desbloquear': 'Desbloqueou um utilizador',
  'perfil.criar': 'Criou um perfil', 'perfil.editar': 'Editou um perfil (permissões)', 'perfil.eliminar': 'Eliminou um perfil',
  'configuracoes.alterar': 'Alterou as configurações', 'impressora.criar': 'Criou uma impressora', 'impressora.editar': 'Editou uma impressora',
  'licenca.activar': 'Activou a licença', 'sistema.iniciar-producao': 'Iniciou a produção',
}

export const nomeModulo = (codigo: string) => NOMES_MODULO[codigo] ?? codigo
export const descreverAccao = (accao: string) => NOMES_ACCAO[accao] ?? accao

/** Atalhos para as consultas mais pedidas pela gerência: cada um junta várias acções. */
export const ATALHOS: { id: string; titulo: string; accoes: string[] }[] = [
  {
    id: 'anulacoes', titulo: 'Anulações e estornos',
    accoes: ['pedido.cancelar', 'pedido.cancelar-item', 'pagamento.anular', 'pedido.estornar', 'documento.anular', 'despesa.anular', 'compra.anular', 'inventario.cancelar'],
  },
  { id: 'precos', titulo: 'Descontos e preços', accoes: ['pedido.desconto', 'produto.alterar-preco'] },
  {
    id: 'caixa', titulo: 'Dinheiro no caixa',
    accoes: ['caixa.dia-abrir', 'caixa.dia-fechar', 'caixa.abrir', 'caixa.fechar', 'caixa.entrada', 'caixa.saida', 'caixa.abrir-gaveta', 'cartao.descarregar'],
  },
  {
    id: 'seguranca', titulo: 'Segurança',
    accoes: ['login.falhado', 'login.bloqueado', 'utilizador.repor-palavra-passe', 'utilizador.alterar-palavra-passe', 'utilizador.alterar-pin', 'utilizador.desbloquear',
      'utilizador.criar', 'utilizador.activar', 'utilizador.desactivar', 'perfil.criar', 'perfil.editar', 'perfil.eliminar'],
  },
  {
    id: 'configuracoes', titulo: 'Configurações',
    accoes: ['configuracoes.alterar', 'impressora.criar', 'impressora.editar', 'impressora.activar', 'impressora.desactivar', 'taxa.criar', 'taxa.editar',
      'taxa.activar', 'taxa.desactivar', 'licenca.activar', 'licenca.reaplicar', 'sistema.iniciar-producao', 'posto.tablets-escolher', 'posto.tablets-retirar',
      'posto.tablets-codigo', 'posto.agente.registar', 'posto.agente.revogar'],
  },
]
