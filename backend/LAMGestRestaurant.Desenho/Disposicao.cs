using LAMGestRestaurant.Application.Impressao;

namespace LAMGestRestaurant.Infrastructure.Impressao;

public enum EstiloTexto { Normal, Negrito, Grande }

/// <summary>Medição de texto do dispositivo (PDF ou impressora), em milímetros.</summary>
public interface IMedidorTexto
{
    double Largura(string texto, EstiloTexto estilo);
    double Altura(EstiloTexto estilo);
}

public abstract record Operacao;
public sealed record OpTexto(string Texto, EstiloTexto Estilo, double X, double Y, double Largura, Alinhamento Alinhamento, bool Cinzento = false) : Operacao;
public sealed record OpLinha(double X1, double X2, double Y, bool Tracejada, bool Clara = false) : Operacao;
public sealed record OpFundo(double X, double Y, double Largura, double Altura) : Operacao;
public sealed record OpLogotipo(double X, double Y, double Largura, double Altura) : Operacao;
/// <summary>Código de barras: módulos (true = barra) com a largura indicada, a partir de (X, Y).</summary>
public sealed record OpBarras(double X, double Y, double LarguraModulo, double Altura, IReadOnlyList<bool> Modulos) : Operacao;
/// <summary>QR code: matriz de módulos quadrados (true = preto), a partir de (X, Y).</summary>
public sealed record OpMatriz(double X, double Y, double LadoModulo, IReadOnlyList<IReadOnlyList<bool>> Modulos) : Operacao;

/// <summary>Uma página já disposta: tamanho em mm e o que desenhar.</summary>
public sealed record PaginaDisposta(double Largura, double Altura, IReadOnlyList<Operacao> Operacoes);

/// <summary>
/// Disposição comum à impressão directa e ao PDF (as duas saem iguais), em milímetros.
/// A4: logótipo à esquerda e identificação do estabelecimento à direita; tabelas em colunas com títulos sombreados
/// (repetidos no topo de cada página); rodapé com o título e «Página N de M».
/// Talão (58/80 mm): uma só página com a altura do conteúdo; logótipo e identificação centrados; nas tabelas a descrição
/// ocupa uma linha e os valores vão em colunas por baixo.
/// </summary>
public static class Disposicao
{
    /// <summary>Larguras de papel suportadas (mm): talões de 58 e 80 mm, A4 a partir de 210.</summary>
    public const int Largura58 = 58, Largura80 = 80, LarguraA4 = 210;
    public const double MargemA4 = 15, MargemTalao = 2, AlturaA4 = 297;
    private const double EspacoLinha = 0.35, RodapeA4 = 9;

    public static double TamanhoFonte(int larguraPapelMm) =>
        larguraPapelMm >= LarguraA4 ? 10 : larguraPapelMm <= Largura58 ? 7.5 : 9;

    /// <param name="proporcaoLogotipo">Largura/altura do logótipo, ou null se não houver logótipo (ou for ilegível).</param>
    public static IReadOnlyList<PaginaDisposta> Dispor(DocumentoImpressao documento, int larguraPapelMm, double? proporcaoLogotipo, IMedidorTexto m)
    {
        if (documento.Etiquetas is { } etiquetas)
            return DisporEtiquetas(etiquetas, documento.Formato ?? new FormatoEtiqueta(50, 30), m);
        var a4 = larguraPapelMm >= LarguraA4;
        var largura = a4 ? 210 : larguraPapelMm;
        var margem = a4 ? MargemA4 : MargemTalao;
        var util = largura - 2 * margem;
        var corpo = new Corpo(m, a4, margem, util);

        // ---- topo: logótipo + identificação do estabelecimento
        var topo = new List<Operacao>();
        double y = margem;
        var cabecalho = documento.Cabecalho ?? [];
        if (a4)
        {
            double alturaLogo = 0, larguraLogo = 0;
            if (proporcaoLogotipo is double p)
            {
                (larguraLogo, alturaLogo) = Ajustar(p, 55, 28);
                topo.Add(new OpLogotipo(margem, y, larguraLogo, alturaLogo));
            }
            // Com logótipo, a identificação fica à direita (alinhada à direita); sem ele, à esquerda.
            var xTexto = larguraLogo > 0 ? margem + larguraLogo + 6 : margem;
            var larguraTexto = margem + util - xTexto;
            var yTexto = y;
            foreach (var linha in cabecalho)
                yTexto = corpo.Paragrafo(topo, linha.Texto, Estilo(linha), xTexto, yTexto, larguraTexto, larguraLogo > 0 ? Alinhamento.Direita : Alinhamento.Esquerda);
            y = Math.Max(y + alturaLogo, yTexto);
            if (alturaLogo > 0 || cabecalho.Count > 0)
            {
                y += 2;
                topo.Add(new OpLinha(margem, margem + util, y, Tracejada: false));
                y += 4;
            }
        }
        else
        {
            if (proporcaoLogotipo is double p)
            {
                var (l, a) = Ajustar(p, util * 0.75, larguraPapelMm <= Largura58 ? 18 : 23);
                topo.Add(new OpLogotipo(margem + (util - l) / 2, y, l, a));
                y += a + 2;
            }
            foreach (var linha in cabecalho)
                y = corpo.Paragrafo(topo, linha.Texto, Estilo(linha), margem, y, util, Alinhamento.Centro);
            if (cabecalho.Count > 0)
            {
                y += 0.8;
                topo.Add(new OpLinha(margem, margem + util, y, Tracejada: true));
                y += 1.6;
            }
        }

        // ---- corpo: blocos com altura conhecida, para poder paginar (A4)
        var blocos = corpo.Blocos(documento.Linhas);
        if (!a4)
        {
            var ops = new List<Operacao>(topo);
            foreach (var b in blocos)
            {
                b.Desenhar(ops, y);
                y += b.Altura;
            }
            return [new PaginaDisposta(largura, y + margem + 4, ops)];
        }

        var paginas = new List<List<Operacao>> { topo };
        var limite = AlturaA4 - margem - RodapeA4;
        Bloco? tituloTabela = null;
        foreach (var b in blocos)
        {
            if (b.TituloTabela) tituloTabela = b;
            else if (!b.LinhaTabela) tituloTabela = null;
            if (y + b.Altura > limite && y > margem + 1)
            {
                paginas.Add([]);
                y = margem;
                // Tabela que continua na página seguinte: repete os títulos das colunas.
                if (b.LinhaTabela && tituloTabela is not null)
                {
                    tituloTabela.Desenhar(paginas[^1], y);
                    y += tituloTabela.Altura;
                }
            }
            b.Desenhar(paginas[^1], y);
            y += b.Altura;
        }
        var total = paginas.Count;
        return [.. paginas.Select((ops, i) =>
        {
            var yRodape = AlturaA4 - margem - RodapeA4 + 3;
            ops.Add(new OpLinha(margem, margem + util, yRodape, Tracejada: false, Clara: true));
            ops.Add(new OpTexto(documento.Titulo, EstiloTexto.Normal, margem, yRodape + 1, util * 0.7, Alinhamento.Esquerda, Cinzento: true));
            ops.Add(new OpTexto($"Página {i + 1} de {total}", EstiloTexto.Normal, margem, yRodape + 1, util, Alinhamento.Direita, Cinzento: true));
            return new PaginaDisposta(largura, AlturaA4, ops);
        })];
    }

    private static (double Largura, double Altura) Ajustar(double proporcao, double maxLargura, double maxAltura)
    {
        var l = Math.Min(maxLargura, maxAltura * proporcao);
        return (l, l / proporcao);
    }

    /// <summary>
    /// Etiquetas: uma página por etiqueta, com o tamanho configurado. Texto centrado em cima (cortado se não couber na largura);
    /// o código de barras ocupa o espaço que sobra, com o número por baixo. Margem branca de 10 módulos dos dois lados.
    /// </summary>
    private static IReadOnlyList<PaginaDisposta> DisporEtiquetas(IReadOnlyList<EtiquetaImpressao> etiquetas, FormatoEtiqueta formato, IMedidorTexto m)
    {
        const double Margem = 1.5, ModuloMaximo = 0.5, AlturaMinimaBarras = 4;
        var (largura, altura) = (formato.LarguraMm, formato.AlturaMm);
        var util = largura - 2 * Margem;
        var paginas = new List<PaginaDisposta>();
        foreach (var e in etiquetas)
        {
            var ops = new List<Operacao>();
            // QR code à esquerda (com a sua margem branca incluída); o resto da etiqueta fica à direita.
            var esquerda = Margem;
            var coluna = util;
            if (e.QrCode is { Length: > 0 } qr)
            {
                var matriz = Qr.Matriz(qr);
                var lado = Math.Min(altura - 2 * Margem, util * 0.5);
                ops.Add(new OpMatriz(Margem, (altura - lado) / 2, lado / matriz.Count, matriz));
                esquerda = Margem + lado + 1;
                coluna = util - lado - 1;
            }
            double y = Margem;
            foreach (var l in e.Linhas)
            {
                var estilo = Estilo(l);
                ops.Add(new OpTexto(Cortar(l.Texto, estilo, coluna, m), estilo, esquerda, y, coluna, Alinhamento.Centro));
                y += m.Altura(estilo) + 0.2;
            }
            if (e.CodigoBarras is { Length: > 0 } codigo && Code128.Valido(codigo))
            {
                var modulos = Code128.Modulos(codigo);
                var modulo = Math.Min(coluna / (modulos.Count + 20), ModuloMaximo);
                var numero = m.Altura(EstiloTexto.Normal);
                var alturaBarras = Math.Max(altura - Margem - numero - y - 0.5, AlturaMinimaBarras);
                var x = esquerda + (coluna - modulos.Count * modulo) / 2;
                ops.Add(new OpBarras(x, y + 0.5, modulo, alturaBarras, modulos));
                ops.Add(new OpTexto(codigo, EstiloTexto.Normal, esquerda, y + 0.5 + alturaBarras, coluna, Alinhamento.Centro));
            }
            paginas.Add(new PaginaDisposta(largura, altura, ops));
        }
        return paginas;
    }

    /// <summary>Texto que não cabe na largura: corta e acaba em «…».</summary>
    private static string Cortar(string texto, EstiloTexto estilo, double largura, IMedidorTexto m)
    {
        if (m.Largura(texto, estilo) <= largura)
            return texto;
        for (var n = texto.Length - 1; n > 0; n--)
        {
            var curto = texto[..n].TrimEnd() + "…";
            if (m.Largura(curto, estilo) <= largura)
                return curto;
        }
        return "…";
    }

    private static EstiloTexto Estilo(LinhaImpressao l) => l.Grande ? EstiloTexto.Grande : l.Negrito ? EstiloTexto.Negrito : EstiloTexto.Normal;

    /// <summary>Parte do documento com altura fixa, desenhada numa posição vertical.</summary>
    private sealed class Bloco(double altura, Action<List<Operacao>, double> desenhar, bool linhaTabela = false, bool tituloTabela = false)
    {
        public double Altura { get; } = altura;
        public bool LinhaTabela { get; } = linhaTabela;
        public bool TituloTabela { get; } = tituloTabela;
        public void Desenhar(List<Operacao> ops, double y) => desenhar(ops, y);
    }

    private sealed class Corpo(IMedidorTexto m, bool a4, double margem, double util)
    {
        public List<Bloco> Blocos(IReadOnlyList<LinhaImpressao> linhas)
        {
            var blocos = new List<Bloco>();
            for (var i = 0; i < linhas.Count; i++)
            {
                var linha = linhas[i];
                if (linha.Celulas is { Count: > 2 })
                {
                    var fim = i;
                    while (fim + 1 < linhas.Count && linhas[fim + 1].Celulas is { Count: > 2 }) fim++;
                    blocos.AddRange(Tabela([.. linhas.Skip(i).Take(fim - i + 1)]));
                    i = fim;
                }
                else if (linha.Celulas is { Count: 2 } duas)
                    blocos.Add(Linha(linha with { Texto = duas[0], Direita = duas[1], Celulas = null }, sublinhado: linha.Cabecalho));
                else if (linha.Separador)
                {
                    var altura = m.Altura(EstiloTexto.Normal) * 0.9;
                    blocos.Add(new Bloco(altura, (ops, y) => ops.Add(new OpLinha(margem, margem + util, y + altura / 2, Tracejada: !a4, Clara: a4))));
                }
                else
                    blocos.Add(Linha(linha));
            }
            return blocos;
        }

        /// <summary>Texto com valor à direita na mesma linha; se o valor não couber (58 mm), passa para a linha de baixo.</summary>
        private Bloco Linha(LinhaImpressao linha, bool sublinhado = false)
        {
            var estilo = Estilo(linha);
            var h = m.Altura(estilo);
            var larguraDireita = linha.Direita is null ? 0 : m.Largura(linha.Direita, estilo) + 2;
            var abaixo = larguraDireita > util * 0.55;
            var texto = Quebrar(linha.Texto, estilo, Math.Max(util - (abaixo ? 0 : larguraDireita), 10));
            var altura = h * (texto.Count + (abaixo ? 1 : 0)) + EspacoLinha + (sublinhado ? 0.8 : 0);
            return new Bloco(altura, (ops, y) =>
            {
                for (var i = 0; i < texto.Count; i++)
                    ops.Add(new OpTexto(texto[i], estilo, margem, y + i * h, util - (abaixo ? 0 : larguraDireita), linha.Alinhamento));
                if (linha.Direita is not null)
                    ops.Add(new OpTexto(linha.Direita, estilo, margem, abaixo ? y + texto.Count * h : y, util, Alinhamento.Direita));
                if (sublinhado)
                    ops.Add(new OpLinha(margem, margem + util, y + altura - 0.5, Tracejada: false));
            });
        }

        private IEnumerable<Bloco> Tabela(IReadOnlyList<LinhaImpressao> linhas)
        {
            var colunas = linhas.Max(l => l.Celulas!.Count);
            string Celula(LinhaImpressao l, int c) => c < l.Celulas!.Count ? l.Celulas[c] : "";
            // Talão: a tabela fica numa linha por registo se a descrição e os valores couberem lado a lado; senão, empilha.
            var larguraValores = Enumerable.Range(1, colunas - 1).Sum(c => linhas.Max(l => m.Largura(Celula(l, c), Estilo(l))) + 2);
            var cabeNumaLinha = linhas.Max(l => m.Largura(Celula(l, 0), Estilo(l))) + larguraValores + 1 <= util;
            if (a4 || cabeNumaLinha)
            {
                // Colunas de valores com a largura do maior conteúdo; a descrição fica com o resto.
                var larguras = Enumerable.Range(1, colunas - 1)
                    .Select(c => Math.Max(a4 ? 16 : 0, linhas.Max(l => m.Largura(Celula(l, c), Estilo(l))) + (a4 ? 4 : 2))).ToArray();
                var larguraDescricao = Math.Max(util - larguras.Sum(), util * 0.3);
                foreach (var l in linhas)
                {
                    var estilo = Estilo(l);
                    var h = m.Altura(estilo);
                    var recuo = a4 ? 1 : 0;
                    var texto = Quebrar(Celula(l, 0), estilo, larguraDescricao - 2 * recuo);
                    var altura = h * texto.Count + (a4 ? 1.6 : EspacoLinha + (l.Cabecalho ? 0.8 : 0));
                    var topo = a4 ? 0.8 : 0;
                    yield return new Bloco(altura, (ops, y) =>
                    {
                        if (l.Cabecalho && a4) ops.Add(new OpFundo(margem, y, util, altura));
                        for (var i = 0; i < texto.Count; i++)
                            ops.Add(new OpTexto(texto[i], estilo, margem + recuo, y + topo + i * h, larguraDescricao - 2 * recuo, Alinhamento.Esquerda));
                        var x = margem + larguraDescricao;
                        for (var c = 1; c < colunas; c++)
                        {
                            ops.Add(new OpTexto(Celula(l, c), estilo, x, y + topo, larguras[c - 1] - recuo, Alinhamento.Direita));
                            x += larguras[c - 1];
                        }
                        if (a4 && !l.Cabecalho) ops.Add(new OpLinha(margem, margem + util, y + altura, Tracejada: false, Clara: true));
                        if (!a4 && l.Cabecalho) ops.Add(new OpLinha(margem, margem + util, y + altura - 0.5, Tracejada: false));
                    }, linhaTabela: true, tituloTabela: l.Cabecalho);
                }
                yield break;
            }

            // Talão: descrição numa linha (com quebra), valores em colunas iguais por baixo, alinhados à direita.
            var larguraValor = util / (colunas - 1);
            foreach (var l in linhas)
            {
                var estilo = Estilo(l);
                var h = m.Altura(estilo);
                var texto = Quebrar(Celula(l, 0), estilo, util);
                var altura = h * (texto.Count + 1) + EspacoLinha + (l.Cabecalho ? 0.8 : 0.3);
                yield return new Bloco(altura, (ops, y) =>
                {
                    for (var i = 0; i < texto.Count; i++)
                        ops.Add(new OpTexto(texto[i], estilo, margem, y + i * h, util, Alinhamento.Esquerda));
                    for (var c = 1; c < colunas; c++)
                        ops.Add(new OpTexto(Celula(l, c), estilo, margem + (c - 1) * larguraValor, y + texto.Count * h, larguraValor, Alinhamento.Direita));
                    if (l.Cabecalho) ops.Add(new OpLinha(margem, margem + util, y + altura - 0.5, Tracejada: false));
                }, linhaTabela: true, tituloTabela: l.Cabecalho);
            }
        }

        /// <summary>Parágrafo com quebra de linha; devolve o y seguinte.</summary>
        public double Paragrafo(List<Operacao> ops, string texto, EstiloTexto estilo, double x, double y, double largura, Alinhamento alinhamento)
        {
            var h = m.Altura(estilo);
            foreach (var parte in Quebrar(texto, estilo, largura))
            {
                ops.Add(new OpTexto(parte, estilo, x, y, largura, alinhamento));
                y += h;
            }
            return y + EspacoLinha;
        }

        /// <summary>Quebra por palavras (mantém os espaços iniciais, usados nos recuos «   Ref.»).</summary>
        public List<string> Quebrar(string texto, EstiloTexto estilo, double largura)
        {
            if (texto.Length == 0)
                return [""];
            var linhas = new List<string>();
            var recuo = texto[..(texto.Length - texto.TrimStart().Length)];
            var actual = recuo;
            foreach (var palavra in texto.TrimStart().Split(' '))
            {
                var tentativa = actual.Trim().Length == 0 ? recuo + palavra : actual + " " + palavra;
                if (m.Largura(tentativa, estilo) <= largura || actual.Trim().Length == 0)
                    actual = tentativa;
                else
                {
                    linhas.Add(actual);
                    actual = recuo + palavra;
                }
            }
            linhas.Add(actual);
            return linhas;
        }
    }
}
