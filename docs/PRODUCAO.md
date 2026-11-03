# Lista de verificação de produção

Marque cada ponto antes de usar o sistema com dinheiro real. Detalhes em [INSTALACAO_LAN.md](INSTALACAO_LAN.md) e [BACKUP.md](BACKUP.md).

## Servidor
- [ ] IP fixo (reserva DHCP) e nome do servidor conhecido pelos terminais
- [ ] Suspensão/hibernação desactivadas; UPS ligada
- [ ] Windows Update configurado para não reiniciar em horário de serviço (*Horas de actividade*)
- [ ] Antivírus activo; exclusões só se necessário para as pastas de dados do SQL Server
- [ ] Rede do restaurante marcada como *Privada*; Wi-Fi de clientes separado da rede dos terminais

## Aplicação
- [ ] Pacote gerado com `scripts\publicar.ps1` a partir de um commit identificado (`VERSAO.txt`)
- [ ] `migracoes.sql` aplicado; `/api/saude` responde `baseDados: true` e `migracoesPendentes: false`
- [ ] Serviço `LAMGestRestaurant` instalado com `instalar-servico.ps1`, arranque automático, em execução
- [ ] `ASPNETCORE_ENVIRONMENT=Production` (Swagger desligado; confirmar que `/swagger` não abre)
- [ ] `DataProtection__Pasta` configurada (reiniciar o serviço e confirmar que a sessão continua aberta)
- [ ] Connection string só nas variáveis do serviço — nunca em ficheiros do pacote
- [ ] Porta aberta na firewall só para redes Domínio/Privadas; porta 1433 do SQL Server **fechada**
- [ ] HTTPS configurado (recomendado) e `Seguranca__CookieSoHttps=true`

## Contas e permissões
- [ ] Primeiro administrador criado com `bootstrap-admin`; palavra-passe forte guardada em local seguro
- [ ] Segundo administrador criado (a aplicação impede desactivar o último)
- [ ] Um utilizador por pessoa (sem contas partilhadas) com o perfil adequado: Caixa, Garçom, Cozinha, Stock/Compras, Gerente
- [ ] Permissões sensíveis revistas em **Perfis**: descontos, cancelamentos, estornos (`pagamentos.anular`), preços, ajustes de stock, despesas, relatórios financeiros
- [ ] Contas de teste e de demonstração desactivadas; nenhum dado «DEMO» na base de produção

## Configuração do restaurante
- [ ] **Configurações**: nome, NUIT, endereço, contactos, moeda, taxa de entrega, fidelização
- [ ] Taxas de imposto e métodos de pagamento revistos (referência obrigatória em M-Pesa/e-Mola/Transferência)
- [ ] Terminais de caixa registados e escolhidos em cada computador de caixa
- [ ] Produtos, categorias, preços (com imposto incluído), estações da cozinha, mesas e zonas
- [ ] Artigos de stock, stock inicial (inventário de abertura), fichas técnicas dos pratos e mínimos
- [ ] Funcionários ligados aos utilizadores dos garçons
- [ ] Impressora de talão instalada e testada em cada caixa (recibo de teste)

## Cópias de segurança
- [ ] Tarefa diária registada (`registar-backup.ps1`) e executada uma vez com sucesso
- [ ] Segunda cópia fora do servidor (`-CopiaPara`)
- [ ] Restauro testado (`testar-restauro.ps1`) — repetir todos os meses
- [ ] Pessoa responsável por verificar o log de backups semanalmente

## Ensaio geral (antes do primeiro dia)
- [ ] Abrir caixa → pedido de mesa → enviar à cozinha → pronto → pagamento parcial e total → recibo impresso → fecho de caixa com contagem
- [ ] Delivery/Take Away do início ao fim
- [ ] Estorno com motivo e confirmação do reembolso no caixa
- [ ] Desligar o servidor durante um pedido: os terminais mostram a faixa vermelha e não gravam; ao religar tudo volta sem duplicados
- [ ] Relatórios do dia batem certo com o dinheiro contado
