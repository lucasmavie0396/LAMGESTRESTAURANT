import plugin from 'tailwindcss/plugin'

/** Cor ligada ao tema: o valor vem da variável CSS (src/tema.css), que muda no modo escuro. */
const doTema = (nome) =>
  Object.fromEntries(['50', '100', '200', '300', '400', '500', '600', '700', '800', '900', '950'].map((t) => [t, `rgb(var(--c-${nome}-${t}) / <alpha-value>)`]))

/** @type {import('tailwindcss').Config} */
export default {
  content: ['./index.html', './src/**/*.{ts,tsx}'],
  theme: {
    extend: {
      colors: {
        // Cores com tema claro/escuro (botão ☀/☾ na barra de cima).
        slate: doTema('slate'),
        red: doTema('red'),
        green: doTema('green'),
        amber: doTema('amber'),
        // Cor principal do LAMGest (azul).
        primaria: doTema('primaria'),
        // Fundo dos cartões, campos e janelas (branco no claro, ardósia no escuro). O texto branco continua «white».
        superficie: 'rgb(var(--c-superficie) / <alpha-value>)',
        // Cinzentos escuros fixos (não mudam com o tema): botões escuros com texto branco.
        grafite: { 600: '#475569', 700: '#334155', 800: '#1e293b', 900: '#0f172a' },
      },
      fontFamily: {
        sans: ['"Segoe UI"', 'system-ui', '-apple-system', 'Roboto', 'sans-serif'],
      },
    },
  },
  plugins: [
    // tactil: só no posto com «Modo ecrã táctil» (Configurações → Postos → Ecrã), marcado em <html data-tactil>.
    plugin(({ addVariant }) => addVariant('tactil', 'html[data-tactil] &')),
  ],
}
