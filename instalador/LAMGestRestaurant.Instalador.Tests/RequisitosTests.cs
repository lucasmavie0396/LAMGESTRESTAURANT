using System.Net;
using System.Net.Sockets;
using LAMGestRestaurant.Instalador.Motor;

namespace LAMGestRestaurant.Instalador.Tests;

/// <summary>Pré-requisitos do servidor: o assistente avisa antes de instalar, em vez de falhar a meio.</summary>
public class RequisitosTests
{
    [Fact]
    public void Sem_SQL_Server_explica_como_instalar_e_com_o_servico_parado_diz_como_o_iniciar()
    {
        var semSql = Requisitos.AvisoSql([], @".\SQLEXPRESS");
        Assert.Contains("Não encontrei o SQL Server", semSql);
        Assert.Contains(Requisitos.EnderecoSqlExpress, semSql);

        IReadOnlyList<InstanciaSql> instancias = [new(@".\SQLEXPRESS", false), new(".", true)];
        Assert.Contains("SQL Server (SQLEXPRESS)", Requisitos.AvisoSql(instancias, @".\sqlexpress"));
        Assert.Contains("SQL Server (MSSQLSERVER)", Requisitos.AvisoSql([new(".", false)], "."));
        Assert.Null(Requisitos.AvisoSql(instancias, "."));                      // em execução
        Assert.Null(Requisitos.AvisoSql(instancias, @"OUTRO\SQLEXPRESS"));     // noutro computador: decide o teste de ligação
        Assert.Null(Requisitos.AvisoSql([new(@".\X", null)], @".\X"));           // estado desconhecido: não bloqueia
    }

    [Fact]
    public void Sqlcmd_e_procurado_no_PATH_do_sistema_e_nas_pastas_conhecidas()
    {
        var pf = @"C:\Program Files";
        var odbc = Path.Combine(pf, @"Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE");
        var noPath = Path.Combine(@"D:\Ferramentas", "sqlcmd.exe");
        Assert.Equal(noPath, Requisitos.ProcurarSqlcmd(f => f == noPath || f == odbc, @"C:\Windows;D:\Ferramentas", pf)); // o PATH primeiro
        Assert.Equal(odbc, Requisitos.ProcurarSqlcmd(f => f == odbc, @"C:\Windows", pf));
        Assert.Equal(Path.Combine(pf, @"SqlCmd\sqlcmd.exe"), Requisitos.ProcurarSqlcmd(f => f.EndsWith(@"SqlCmd\sqlcmd.exe"), "", pf)); // winget
        Assert.Null(Requisitos.ProcurarSqlcmd(_ => false, @"C:\Windows", pf));

        Assert.Contains(Requisitos.ComandoSqlcmd, Requisitos.AvisoSqlcmd(backupDiario: true, sqlcmd: null));
        Assert.Null(Requisitos.AvisoSqlcmd(backupDiario: false, sqlcmd: null)); // sem cópia diária não é preciso
        Assert.Null(Requisitos.AvisoSqlcmd(backupDiario: true, sqlcmd: odbc));
    }

    [Fact]
    public void Espaco_em_disco_e_porta_ocupada()
    {
        Assert.NotNull(Requisitos.AvisoEspaco(500L * 1024 * 1024));
        Assert.NotNull(Requisitos.AvisoEspaco(null));
        Assert.Null(Requisitos.AvisoEspaco(5L * 1024 * 1024 * 1024));
        Assert.NotNull(Requisitos.EspacoLivre(Path.GetTempPath()));

        using var ouvinte = new TcpListener(IPAddress.Loopback, 0);
        ouvinte.Start();
        var porta = ((IPEndPoint)ouvinte.LocalEndpoint).Port;
        Assert.True(Requisitos.PortaOcupada(porta));
        Assert.Contains($"porta {porta}", Requisitos.AvisoPorta(porta, ocupada: true, lamgestJaInstalado: false));
        Assert.Null(Requisitos.AvisoPorta(porta, ocupada: true, lamgestJaInstalado: true)); // é a instalação anterior a actualizar
        Assert.Null(Requisitos.AvisoPorta(porta, ocupada: false, lamgestJaInstalado: false));
    }

    [Fact]
    public void Instancias_deste_computador_trazem_o_estado_do_servico()
    {
        // Nesta máquina de desenvolvimento há SQL Server (.\SYSON) em execução.
        var instancias = Requisitos.InstanciasComEstado();
        Assert.Contains(instancias, i => i.Nome == @".\SYSON" && i.EmExecucao == true);
    }
}
