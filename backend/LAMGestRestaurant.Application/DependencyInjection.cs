using LAMGestRestaurant.Application.Caixa;
using LAMGestRestaurant.Application.Catalogo;
using LAMGestRestaurant.Application.Comum;
using LAMGestRestaurant.Application.Cozinha;
using LAMGestRestaurant.Application.Sala;
using LAMGestRestaurant.Application.Stock;
using LAMGestRestaurant.Application.Vendas;
using Microsoft.Extensions.DependencyInjection;

namespace LAMGestRestaurant.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddAplicacao(this IServiceCollection services)
    {
        services.AddScoped<ConfiguracoesSistema>();
        services.AddScoped<ServicoNumeracao>();
        services.AddScoped<ServicoCatalogo>();
        services.AddScoped<Catalogo.ServicoImportacaoProdutos>();
        services.AddScoped<Catalogo.ServicoStockProdutos>();
        services.AddScoped<ServicoSala>();
        services.AddScoped<ServicoCaixa>();
        services.AddScoped<ServicoPedidos>();
        services.AddScoped<ServicoPagamentos>();
        services.AddScoped<ServicoBaixaStock>();
        services.AddScoped<ArmazensAbate>();
        services.AddScoped<ServicoCozinha>();
        services.AddScoped<Relatorios.ServicoRelatorios>();
        services.AddScoped<Relatorios.ServicoRelatoriosGestao>();
        services.AddScoped<Relatorios.ServicoRelatoriosDetalhe>();
        services.AddScoped<Relatorios.ServicoDocumentosRelatorio>();
        services.AddScoped<MotorStock>();
        services.AddScoped<ServicoStock>();
        services.AddScoped<ServicoReceitas>();
        services.AddScoped<Compras.ServicoCompras>();
        services.AddScoped<Clientes.ServicoClientes>();
        services.AddScoped<Clientes.ServicoFidelizacao>();
        services.AddScoped<Reservas.ServicoReservas>();
        services.AddScoped<Despesas.ServicoDespesas>();
        services.AddScoped<Pessoal.ServicoFuncionarios>();
        services.AddScoped<Sistema.ServicoConfiguracoes>();
        services.AddScoped<Impressao.ServicoImpressao>();
        services.AddScoped<Impressao.ServicoImpressoras>();
        services.AddSingleton<Impressao.EstadoImpressoras>();
        services.AddScoped<Impressao.ServicoAgentes>();
        services.AddScoped<Impressao.ServicoEtiquetas>();
        services.AddScoped<Cartoes.ServicoCartoes>();
        services.AddScoped<Cartoes.ServicoRelatoriosCartoes>();
        services.AddSingleton<Impressao.AvisosPostos>();
        services.AddSingleton<Impressao.DocumentosPdf>();
        // Licença: chave pública do fabricante embutida (a Api substitui-a só nos ambientes de teste).
        services.AddSingleton(_ => new Licencas.ChaveLicencas(LAMGestRestaurant.Licenciamento.ChavesLicenca.PublicaEmbutida()));
        services.AddSingleton<Licencas.CacheEstadoLicenca>();
        services.AddScoped<Licencas.ServicoLicenca>();
        services.AddScoped<Facturacao.ServicoDocumentos>();
        return services;
    }
}
