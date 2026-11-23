using LAMGestRestaurant.Agente.Janela;

namespace LAMGestRestaurant.Tests;

/// <summary>Programa do posto (LAMGest-Agente.exe --app=…): argumentos dos atalhos e regras da janela, sem abrir janelas.</summary>
public class JanelaPostoTests
{
    [Fact]
    public void Le_os_mesmos_argumentos_que_os_atalhos_davam_ao_navegador()
    {
        // Como o Windows os entrega a partir do atalho do instalador: --app=… --start-fullscreen --kiosk-printing --user-data-dir="…"
        var o = OpcoesJanela.Ler(["--app=https://10.0.0.47:8443/pos?posto=1&tactil=1", "--start-fullscreen", "--kiosk-printing",
            @"--user-data-dir=C:\ProgramData\LAMGestRestaurant\navegador"]);
        Assert.NotNull(o);
        Assert.Equal("https://10.0.0.47:8443/pos?posto=1&tactil=1", o.Endereco.ToString());
        Assert.True(o.EcraInteiro);
        Assert.True(o.ImpressaoDirecta);
        Assert.Equal(@"C:\ProgramData\LAMGestRestaurant\navegador", o.PastaPerfil);
        // Os dados da janela ficam ao lado do perfil do Chrome, nunca dentro dele.
        Assert.Equal(@"C:\ProgramData\LAMGestRestaurant\janela", o.PastaDados(@"C:\Users\x\AppData\Local"));

        var simples = OpcoesJanela.Ler(["--app=http://localhost:8080/"]);
        Assert.NotNull(simples);
        Assert.False(simples.EcraInteiro || simples.ImpressaoDirecta);
        Assert.Equal(@"C:\Users\x\AppData\Local\LAMGestRestaurant\janela", simples.PastaDados(@"C:\Users\x\AppData\Local"));
    }

    [Fact]
    public void Sem_endereco_valido_o_executavel_continua_a_ser_o_agente()
    {
        Assert.Null(OpcoesJanela.Ler([]));
        Assert.Null(OpcoesJanela.Ler(["--config", @"C:\x\agente.json"]));
        Assert.Null(OpcoesJanela.Ler(["--app=file:///C:/Windows/win.ini"]));
        Assert.Null(OpcoesJanela.Ler(["--app=javascript:alert(1)"]));
        Assert.Null(OpcoesJanela.Ler(["--app=nao-e-um-endereco"]));
    }

    [Fact]
    public void So_as_paginas_do_servidor_ficam_no_programa_e_o_visor_tem_janela_propria()
    {
        var o = OpcoesJanela.Ler(["--app=https://10.0.0.47:8443/pos?posto=1"])!;
        Assert.True(o.DoServidor(new Uri("https://10.0.0.47:8443/api/documentos/1/pdf")));
        Assert.False(o.DoServidor(new Uri("http://10.0.0.47:8080/pos"))); // outro esquema/porta
        Assert.False(o.DoServidor(new Uri("https://10.0.0.48:8443/pos")));
        Assert.False(o.DoServidor(new Uri("https://www.google.com/search?q=frango")));
        Assert.Equal("https://10.0.0.47:8443/", o.Origem.ToString());

        Assert.True(OpcoesJanela.EVisor(new Uri("https://10.0.0.47:8443/visor?terminal=3&chave=abc")));
        Assert.True(OpcoesJanela.EVisor(new Uri("https://10.0.0.47:8443/visor/")));
        Assert.False(OpcoesJanela.EVisor(new Uri("https://10.0.0.47:8443/configuracoes?aba=visor")));
    }

    [Fact]
    public void Sem_WebView2_abre_o_mesmo_posto_no_navegador_e_as_margens_seguem_o_papel()
    {
        var o = OpcoesJanela.Ler(["--app=http://10.0.0.47:8080/pos?posto=1", "--start-fullscreen", "--kiosk-printing", @"--user-data-dir=C:\P\navegador"])!;
        Assert.Equal(@"--app=http://10.0.0.47:8080/pos?posto=1 --start-fullscreen --kiosk-printing --user-data-dir=""C:\P\navegador""", o.ArgumentosNavegador());
        Assert.Equal(2 / 25.4, OpcoesJanela.Margem(80), 6);
        Assert.Equal(2 / 25.4, OpcoesJanela.Margem(58), 6);
        Assert.Equal(15 / 25.4, OpcoesJanela.Margem(210), 6);
    }
}
