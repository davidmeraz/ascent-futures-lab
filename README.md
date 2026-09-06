# Ascent Futures Lab

Laboratorio para **analizar empresas de fondeo (Prop Firms)** y **desarrollar estrategias e indicadores de futuros (NQ / ES)** para **NinjaTrader 8** y **TradingView**.

---

## Estructura del proyecto

```text
ascent-futures-lab/
├── prop-firms/                 # Reglas y planes de empresas de fondeo (Markdown + JSON)
│   ├── README.md               # Tabla comparativa y conceptos (Trailing Intradía vs EOD, DLL, Consistencia)
│   ├── rules.schema.json       # Esquema JSON para simulación de cuentas de fondeo
│   ├── apex/                   # Apex Trader Funding
│   ├── topstep/                # Topstep
│   ├── my-funded-futures/      # MyFundedFutures
│   └── take-profit-trader/     # Take Profit Trader
├── platforms/                  # Estrategias e indicadores por plataforma
│   ├── ninjatrader/            # Indicadores y estrategias NinjaScript (.cs)
│   └── tradingview/            # Indicadores y estrategias Pine Script v5 (.pine)
├── ascent-backtester/          # Repositorio Git independiente: Motor de backtesting en C# (.NET 10)
└── docs/                       # Plan y notas de arquitectura
```
