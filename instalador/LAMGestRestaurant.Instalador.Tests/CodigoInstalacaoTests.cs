using LAMGestRestaurant.Instalador.Motor;

namespace LAMGestRestaurant.Instalador.Tests;

public class CodigoInstalacaoTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("admin")]
    [InlineData("admin007#")]
    [InlineData("Admin007")]
    [InlineData(" Admin007# ")]
    public void Codigos_errados_nao_libertam_o_instalador(string? codigo) => Assert.False(CodigoInstalacao.Valido(codigo));

    /// <summary>O código certo não fica no repositório: o teste só corre com LAMGEST_CODIGO_INSTALACAO definido.</summary>
    [Fact]
    public void Codigo_certo_liberta_o_instalador()
    {
        var codigo = Environment.GetEnvironmentVariable("LAMGEST_CODIGO_INSTALACAO");
        if (string.IsNullOrEmpty(codigo))
            return;
        Assert.True(CodigoInstalacao.Valido(codigo));
    }
}
